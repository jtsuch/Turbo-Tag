using System;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>One kill, as announced to everyone (kill feed, killcam).</summary>
public readonly struct KillInfo
{
    public readonly int    Killer;       // Actor number; the victim themself for a self destruction
    public readonly int    Victim;
    public readonly string Weapon;       // GunCatalog id ("" for a fall)
    public readonly bool   Headshot;
    public readonly bool   SelfDestruct; // Fell off the map

    public KillInfo(int killer, int victim, string weapon, bool headshot, bool selfDestruct)
    {
        Killer = killer; Victim = victim; Weapon = weapon; Headshot = headshot; SelfDestruct = selfDestruct;
    }
}

/// <summary>
/// Scene-level gun-mode plumbing, added next to GameModeManager when the mode <see cref="GameMode.UsesGuns"/>.
///   - Deaths: the victim's client reports its own death (PlayerHealth at 0 hp, or a fall); the master scores it
///     (<see cref="GameMode.OnPlayerKilled"/>), announces it (<see cref="Killed"/>: kill feed, killcam), drops a
///     health pack where a shot player died, and runs the normal tag flow (ragdoll, caught view, respawn).
///   - Health drops: master-owned ids; the first player to touch one (and asked the master) gets full health.
///   - Local loop: full health and the gun picker on every spawn; the combat HUD and the killcam.
/// RPCs go through the GameModeManager's scene PhotonView (same GameObject).
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class CombatManager : MonoBehaviourPun
{
    // ─── Singleton ────────────────────────────────────────────────────────────
    private static CombatManager _instance;
    public static CombatManager Instance => _instance;

    /// <summary>Every client: a player died.</summary>
    public event Action<KillInfo> Killed;

    private const float PickupRadius   = 1.6f;
    private const float PickupLifetime = 30f;
    private const float DeathDedupe    = 1f;

    // Local
    private bool  deathReported;
    private Player subscribed;
    private float  lastPickupRequest;
    private readonly Dictionary<int, HealthPickup> pickups = new();

    // Master
    private int nextPickupId = 1;
    private readonly Dictionary<int, float> livePickups = new(); // id → expiry (Time.time)
    private readonly Dictionary<int, float> lastDeath   = new(); // actor → Time.time

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        _instance = this;
        if (!TryGetComponent(out Killcam _)) gameObject.AddComponent<Killcam>();
        CombatHud.Create();
    }

    private void OnEnable()
    {
        Player.LocalPlayerSpawned += HandleLocalSpawned;
        if (GameModeManager.Instance != null) GameModeManager.Instance.PhaseChanged += HandlePhaseChanged;
    }

    private void OnDisable()
    {
        Player.LocalPlayerSpawned -= HandleLocalSpawned;
        if (GameModeManager.Instance != null) GameModeManager.Instance.PhaseChanged -= HandlePhaseChanged;
        if (subscribed != null)
        {
            subscribed.Respawned -= HandleLocalRespawned;
            subscribed.Caught    -= HandleLocalCaught;
        }
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    private void Update()
    {
        // The local player may have spawned before this component existed
        if (subscribed == null && Player.Local != null) HandleLocalSpawned(Player.Local);

        UpdatePickups();
        if (PhotonNetwork.IsMasterClient) ExpirePickups();
    }

    // ─── Local spawn loop ─────────────────────────────────────────────────────

    private void HandleLocalSpawned(Player local)
    {
        if (subscribed == local) return;
        subscribed = local;
        local.Respawned += HandleLocalRespawned;
        local.Caught    += HandleLocalCaught;
        local.Health.Refill();
        GunPicker.Open();
    }

    private void HandleLocalCaught()
    {
        if (Player.Local != null) Player.Local.Gun.Equip(null);
        GunPicker.Close(pick: false);
    }

    private void HandleLocalRespawned()
    {
        deathReported = false;
        if (Player.Local == null) return;
        Player.Local.Health.Refill();
        GunPicker.Open();
    }

    private void HandlePhaseChanged(MatchPhase phase)
    {
        // New match: everyone starts full and picks again (the mode respawns them at a spawn point)
        if (phase != MatchPhase.Countdown || Player.Local == null) return;
        deathReported = false;
        Player.Local.Health.Refill();
        GunPicker.Open();
    }

    // ─── Deaths ───────────────────────────────────────────────────────────────

    /// <summary>Local player: fell off the map.</summary>
    public void ReportFall() => ReportLocalDeath(-1, "", false);

    /// <summary>Local player died (<paramref name="killerActor"/> -1 for a fall). Sent once per life.</summary>
    public void ReportLocalDeath(int killerActor, string weapon, bool headshot)
    {
        var local = Player.Local;
        if (deathReported || local == null || local.IsCaught) return;
        deathReported = true;
        photonView.RPC(nameof(RPC_ReportDeath), RpcTarget.MasterClient, killerActor, weapon ?? "", headshot, local.transform.position);
    }

    [PunRPC]
    private void RPC_ReportDeath(int killerActor, string weapon, bool headshot, Vector3 position, PhotonMessageInfo info)
    {
        var manager = GameModeManager.Instance;
        if (!PhotonNetwork.IsMasterClient || manager == null || manager.Mode == null) return;

        PhotonPlayer victim = info.Sender;
        if (victim == null) return;
        if (lastDeath.TryGetValue(victim.ActorNumber, out float at) && Time.time - at < DeathDedupe) return;
        lastDeath[victim.ActorNumber] = Time.time;

        PhotonPlayer killer = killerActor >= 0 ? PhotonNetwork.CurrentRoom.GetPlayer(killerActor) : null;
        bool selfDestruct = killer == null || killer == victim;
        bool live = manager.Phase == MatchPhase.Active;

        if (live) manager.Mode.OnPlayerKilled(victim, selfDestruct ? null : killer, weapon, headshot);

        photonView.RPC(nameof(RPC_Killed), RpcTarget.All,
            selfDestruct ? victim.ActorNumber : killer.ActorNumber, victim.ActorNumber, weapon, headshot, selfDestruct);

        if (live && !selfDestruct && MatchRules.HealthDrops) DropHealth(position);

        manager.BroadcastTag(selfDestruct ? victim : killer, victim);
    }

    [PunRPC]
    private void RPC_Killed(int killer, int victim, string weapon, bool headshot, bool selfDestruct)
    {
        // Shot dead: watch it from the killer's eyes (the tag that follows starts the respawn wait)
        bool mine = victim == PhotonNetwork.LocalPlayer.ActorNumber;
        if (mine && !selfDestruct && MatchRules.Killcam && Killcam.Instance != null)
        {
            var room  = PhotonNetwork.CurrentRoom;
            var who   = room?.GetPlayer(killer);
            string by = who != null && !string.IsNullOrEmpty(who.NickName) ? who.NickName : $"Player {killer}";
            string gun = GunCatalog.Find(weapon)?.Name ?? "";
            Killcam.Instance.Play(killer, $"Eliminated by <color=#FF8C1A>{by}</color>  ·  {gun}{(headshot ? "  ·  headshot" : "")}");
        }

        Killed?.Invoke(new KillInfo(killer, victim, weapon, headshot, selfDestruct));
    }

    // ─── Health drops ─────────────────────────────────────────────────────────

    // Master: a pack on the ground under where the player died (none if they died over the void)
    private void DropHealth(Vector3 position)
    {
        if (!Physics.Raycast(position + Vector3.up, Vector3.down, out RaycastHit hit, 30f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return;

        int id = nextPickupId++;
        livePickups[id] = Time.time + PickupLifetime;
        photonView.RPC(nameof(RPC_SpawnPickup), RpcTarget.All, id, hit.point);
    }

    private void ExpirePickups()
    {
        if (livePickups.Count == 0) return;
        List<int> expired = null;
        foreach (var pair in livePickups)
            if (Time.time >= pair.Value) (expired ??= new List<int>()).Add(pair.Key);
        if (expired == null) return;
        foreach (int id in expired)
        {
            livePickups.Remove(id);
            photonView.RPC(nameof(RPC_RemovePickup), RpcTarget.All, id, -1);
        }
    }

    [PunRPC]
    private void RPC_SpawnPickup(int id, Vector3 position)
    {
        if (!pickups.ContainsKey(id)) pickups[id] = HealthPickup.Create(position);
    }

    // Local: touching a pack asks the master for it (at full health too: anyone can deny it to others)
    private void UpdatePickups()
    {
        var local = Player.Local;
        if (local == null || local.IsCaught || pickups.Count == 0) return;
        if (Time.time - lastPickupRequest < 0.3f) return;

        Vector3 body = local.transform.position + Vector3.up * 0.9f;
        foreach (var pair in pickups)
        {
            if (pair.Value == null || Vector3.Distance(body, pair.Value.transform.position) > PickupRadius) continue;
            lastPickupRequest = Time.time;
            photonView.RPC(nameof(RPC_RequestPickup), RpcTarget.MasterClient, pair.Key);
            return;
        }
    }

    [PunRPC]
    private void RPC_RequestPickup(int id, PhotonMessageInfo info)
    {
        if (!PhotonNetwork.IsMasterClient || info.Sender == null || !livePickups.Remove(id)) return;
        photonView.RPC(nameof(RPC_RemovePickup), RpcTarget.All, id, info.Sender.ActorNumber);
    }

    [PunRPC]
    private void RPC_RemovePickup(int id, int takenBy)
    {
        if (pickups.TryGetValue(id, out var pickup))
        {
            if (pickup != null) Destroy(pickup.gameObject);
            pickups.Remove(id);
        }
        if (takenBy == PhotonNetwork.LocalPlayer.ActorNumber && Player.Local != null)
        {
            Player.Local.Health.Refill();
            CombatHud.Healed();
        }
    }
}
