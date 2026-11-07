using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// Puts <see cref="Donut"/>s at random spots around the level and hands out their effects.
///   - Master: every <see cref="MatchRules.EffectsRespawn"/> seconds (while the round is live and the Donuts rule is on)
///     places one, up to a few at once (more with more players), on open, walkable ground away from players and
///     other donuts; each lasts <see cref="Lifetime"/> s. RPC_SpawnDonut to everyone (and to late joiners).
///   - Every client: touching one asks the master for it (RPC_RequestDonut); the first asker gets it
///     (RPC_TakeDonut to all: it pops everywhere, and the taker rolls a random effect on their own player).
/// Added next to GameModeManager (shares its scene PhotonView).
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class DonutManager : MonoBehaviourPunCallbacks
{
    private static DonutManager _instance;
    public static DonutManager Instance => _instance;

    private const float Lifetime        = 75f;
    private const float MinApart        = 10f; // Metres between donuts
    private const float AwayFromPlayers = 5f;
    private const int   StructureLayer  = 6;

    private readonly Dictionary<int, Donut> donuts = new();

    // Master
    private readonly Dictionary<int, float> expiry = new();
    private int   nextId = 1;
    private float nextSpawnAt = -1f;
    private bool  wasMaster;

    // Local
    private float lastRequest;
    private int   stareActor = -1; // Fish Rain: whose fish everyone is staring at
    private float stareUntil;

    private void Awake() => _instance = this;

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    // ─── Update ───────────────────────────────────────────────────────────────

    private void Update()
    {
        AdoptIfNewMaster();
        CheckTouch();
        DropLeftCarriers();
        UpdateStare();
        if (PhotonNetwork.IsMasterClient) MasterUpdate();
    }

    private static bool Live
    {
        get
        {
            var manager = GameModeManager.Instance;
            if (manager == null || manager.Mode == null || !MatchRules.Donuts) return false;
            var phase = manager.Phase;
            return (phase == MatchPhase.Active || phase == MatchPhase.Hiding) && !manager.Clock.IsExpired;
        }
    }

    /// <summary>Chance a placed donut is golden.</summary>
    public const float GoldenChance = 0.06f;

    private void MasterUpdate()
    {
        // Out of play (countdown, round over, rule off): no donuts on the map
        if (!Live)
        {
            if (donuts.Count > 0) photonView.RPC(nameof(RPC_ClearDonuts), RpcTarget.All);
            expiry.Clear();
            nextSpawnAt = -1f;
            return;
        }

        bool party = RoundModifiers.Current?.Id == "DonutParty"; // Round modifier: four times as often, more at once
        float interval = Mathf.Max(10f, MatchRules.EffectsRespawn) * (party ? 0.25f : 1f);
        if (nextSpawnAt < 0f) nextSpawnAt = Time.time + interval * 0.5f; // The first one comes sooner

        // Old ones go stale
        List<int> stale = null;
        foreach (var pair in expiry)
            if (Time.time >= pair.Value) (stale ??= new List<int>()).Add(pair.Key);
        if (stale != null)
            foreach (int id in stale)
            {
                expiry.Remove(id);
                photonView.RPC(nameof(RPC_TakeDonut), RpcTarget.All, id, 0);
            }

        if (Time.time < nextSpawnAt) return;

        int max = 2 + PhotonNetwork.CurrentRoom.PlayerCount / 4 + (party ? 4 : 0); // 2 for up to 3 players … 5 for 12
        if (expiry.Count >= max) { nextSpawnAt = Time.time + 3f; return; } // Full: check again soon
        if (!TryFindSpot(out Vector3 ground)) { nextSpawnAt = Time.time + 2f; return; } // Unlucky: try again shortly
        nextSpawnAt = Time.time + interval * Random.Range(0.8f, 1.2f);

        int newId = nextId++;
        expiry[newId] = Time.time + Lifetime;
        photonView.RPC(nameof(RPC_SpawnDonut), RpcTarget.All, newId, ground, Random.value < GoldenChance);
    }

    // A new host takes over the donuts still out (fresh lifetimes)
    private void AdoptIfNewMaster()
    {
        bool master = PhotonNetwork.IsMasterClient;
        if (master && !wasMaster)
            foreach (var id in donuts.Keys)
            {
                expiry[id] = Time.time + Lifetime;
                nextId = Mathf.Max(nextId, id + 1);
            }
        wasMaster = master;
    }

    public override void OnPlayerEnteredRoom(PhotonPlayer newPlayer)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        foreach (var pair in donuts)
            if (pair.Value != null)
                photonView.RPC(nameof(RPC_SpawnDonut), newPlayer, pair.Key, pair.Value.transform.position - Vector3.up * Donut.HoverHeight, pair.Value.Golden);
    }

    // ─── Where ────────────────────────────────────────────────────────────────

    // Open, flat, walkable ground with headroom, inside the level, away from players and other donuts
    private bool TryFindSpot(out Vector3 ground)
    {
        ground = default;
        var bounds = LevelInfo.Bounds;
        if (!bounds.HasValue) return false;
        var b = bounds.Value;
        int mask = 1 << StructureLayer;
        var anchors = Anchors();

        for (int attempt = 0; attempt < 60; attempt++)
        {
            // Half the tries anywhere in the level, half near places players use (spawns, players): levels with
            // a lot of void or scenery around them (Improvement's outer islands) miss often when only random
            Vector3 top;
            float drop;
            if (attempt % 2 == 1 && anchors.Count > 0)
            {
                Vector3 anchor = anchors[Random.Range(0, anchors.Count)];
                Vector2 offset = Random.insideUnitCircle * 35f;
                top  = new Vector3(anchor.x + offset.x, anchor.y + 12f, anchor.z + offset.y);
                drop = 40f;
            }
            else
            {
                top  = new Vector3(Random.Range(b.min.x, b.max.x), b.max.y + 2f, Random.Range(b.min.z, b.max.z));
                drop = b.size.y + 4f;
            }
            if (!Physics.Raycast(top, Vector3.down, out RaycastHit hit, drop, mask, QueryTriggerInteraction.Ignore)) continue;
            if (hit.normal.y < 0.8f || hit.collider.GetComponentInParent<DeathZone>() != null) continue;
            if (IsBoundary(hit.collider)) continue; // Never on top of the boundary walls

            // Room to stand: nothing just above, and floor all round (not the top of a thin wall or a post)
            if (Physics.SphereCast(hit.point + Vector3.up * 0.5f, 0.4f, Vector3.up, out _, 2.2f, mask, QueryTriggerInteraction.Ignore)) continue;
            if (!FloorAround(hit.point, mask)) continue;
            if (TooClose(hit.point)) continue;

            ground = hit.point;
            return true;
        }
        return false;
    }

    // Where players actually are or start: spawn points, the Spawner, living players
    private static List<Vector3> Anchors()
    {
        var list = new List<Vector3>();
        foreach (var point in SpawnPoint.All) if (point != null) list.Add(point.transform.position);
        if (Spawner.Instance != null) list.Add(Spawner.Instance.transform.position);
        foreach (var p in Player.All) if (p != null && !p.IsDownAfterTag) list.Add(p.transform.position);
        return list;
    }

    private static bool FloorAround(Vector3 point, int mask)
    {
        foreach (var offset in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
        {
            Vector3 from = point + offset * 1f + Vector3.up * 0.6f;
            if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, 1.2f, mask, QueryTriggerInteraction.Ignore)) return false;
            if (Mathf.Abs(hit.point.y - point.y) > 0.4f) return false;
        }
        return true;
    }

    private static bool IsBoundary(Collider col)
    {
        string n = col.name.ToLowerInvariant();
        return n.Contains("border") || n.Contains("boundary");
    }

    private bool TooClose(Vector3 point)
    {
        foreach (var donut in donuts.Values)
            if (donut != null && Vector3.Distance(donut.transform.position, point) < MinApart) return true;
        foreach (var p in Player.All)
            if (p != null && !p.IsDownAfterTag && Vector3.Distance(p.transform.position, point) < AwayFromPlayers) return true;
        return false;
    }


    // ─── Touching (each client, its own player) ───────────────────────────────

    private void CheckTouch()
    {
        var local = Player.Local;
        if (local == null || local.IsCaught || Time.time - lastRequest < 0.3f) return;
        if (!local.TryGetComponent(out CapsuleCollider body)) return;

        // Hunter mode ghosts get nothing from donuts: they pick them up (F) and carry them to the living
        if (local.Role == PlayerRole.Ghost)
        {
            GhostPickUp(local, body);
            return;
        }
        // Touching a ghost who carries one hands it over
        foreach (var pair in carried)
        {
            var ghost = Player.Find(pair.Key);
            if (ghost == null || Vector3.Distance(body.ClosestPoint(ghost.transform.position + Vector3.up), ghost.transform.position + Vector3.up) > HandoffRange) continue;
            lastRequest = Time.time;
            photonView.RPC(nameof(RPC_RequestHandoff), RpcTarget.MasterClient, pair.Key);
            return;
        }
        if (donuts.Count == 0) return;

        foreach (var pair in donuts)
        {
            if (pair.Value == null) continue;
            Vector3 at = pair.Value.transform.position;
            if (Vector3.Distance(body.ClosestPoint(at), at) > Donut.TouchRadius) continue;
            lastRequest = Time.time;
            photonView.RPC(nameof(RPC_RequestDonut), RpcTarget.MasterClient, pair.Key);
            return;
        }
    }

    // ─── Ghosts carry donuts (Hunter mode) ────────────────────────────────────

    private const float HandoffRange = 1.6f; // From a living player's body to a carrying ghost
    private readonly Dictionary<int, Donut> carried = new(); // Ghost actor -> the donut over their head

    private void GhostPickUp(Player local, CapsuleCollider body)
    {
        if (carried.ContainsKey(local.ActorNumber)) return; // One at a time
        foreach (var pair in donuts)
        {
            if (pair.Value == null) continue;
            Vector3 at = pair.Value.transform.position;
            if (Vector3.Distance(body.ClosestPoint(at), at) > Donut.TouchRadius + 0.6f) continue;
            KeyCode key = Keybinds.Get(InputAction.Grab);
            local.ClaimGrabKey();
            HUDManager.Prompt(key, "Pick up the donut (carry it to a teammate)");
            if (!PauseMenuManager.IsPaused && Input.GetKeyDown(key))
            {
                lastRequest = Time.time;
                photonView.RPC(nameof(RPC_RequestCarry), RpcTarget.MasterClient, pair.Key);
            }
            return;
        }
    }

    [PunRPC]
    private void RPC_RequestCarry(int id, PhotonMessageInfo info)
    {
        if (!PhotonNetwork.IsMasterClient || info.Sender == null || PlayerRoles.Get(info.Sender) != PlayerRole.Ghost) return;
        if (carried.ContainsKey(info.Sender.ActorNumber) || !expiry.Remove(id)) return;
        photonView.RPC(nameof(RPC_CarryDonut), RpcTarget.All, id, info.Sender.ActorNumber);
    }

    [PunRPC]
    private void RPC_CarryDonut(int id, int ghostActor)
    {
        if (!donuts.TryGetValue(id, out var donut)) return;
        donuts.Remove(id);
        var ghost = Player.Find(ghostActor);
        if (donut == null || ghost == null) { if (donut != null) donut.Pop(false); return; }
        donut.CarryBy(ghost.transform);
        carried[ghostActor] = donut;
        Sfx.At(SfxKind.Pop, donut.transform.position, 0.7f, 25f, 1.5f);
    }

    [PunRPC]
    private void RPC_RequestHandoff(int ghostActor, PhotonMessageInfo info)
    {
        if (!PhotonNetwork.IsMasterClient || info.Sender == null || !carried.ContainsKey(ghostActor)) return;
        var role = PlayerRoles.Get(info.Sender);
        if (role == PlayerRole.Ghost || role == PlayerRole.None) return;
        MatchStats.Add(info.Sender, PlayerKeys.StatDonuts);
        photonView.RPC(nameof(RPC_HandDonut), RpcTarget.All, ghostActor, info.Sender.ActorNumber);
    }

    [PunRPC]
    private void RPC_HandDonut(int ghostActor, int receiver)
    {
        if (!carried.TryGetValue(ghostActor, out var donut)) return;
        carried.Remove(ghostActor);
        bool golden = donut != null && donut.Golden;
        if (donut != null) donut.Pop(true);
        if (receiver == PhotonNetwork.LocalPlayer.ActorNumber && Player.Local != null) GiveLocal(golden);
    }

    // A carrying ghost who left: their donut goes
    private void DropLeftCarriers()
    {
        if (carried.Count == 0) return;
        foreach (var actor in new List<int>(carried.Keys))
        {
            if (Player.Find(actor) != null) continue;
            if (carried[actor] != null) carried[actor].Pop(false);
            carried.Remove(actor);
        }
    }

    // ─── RPCs ─────────────────────────────────────────────────────────────────

    [PunRPC]
    private void RPC_SpawnDonut(int id, Vector3 ground, bool golden)
    {
        if (donuts.ContainsKey(id)) return;
        donuts[id] = Donut.Create(id, ground, golden);
        if (golden) Sfx.At(SfxKind.Horn, ground, 0.5f, 120f, 1.4f); // A golden donut announces itself
    }

    /// <summary>Lucky Streak: asks the host for a donut near <paramref name="position"/>.</summary>
    public void RequestNear(Vector3 position) => photonView.RPC(nameof(RPC_SpawnNear), RpcTarget.MasterClient, position);

    [PunRPC]
    private void RPC_SpawnNear(Vector3 position)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        int mask = 1 << StructureLayer;
        for (int attempt = 0; attempt < 40; attempt++)
        {
            Vector2 offset = Random.insideUnitCircle * Mathf.Lerp(4f, 14f, attempt / 40f);
            Vector3 top = position + new Vector3(offset.x, 8f, offset.y);
            if (!Physics.Raycast(top, Vector3.down, out RaycastHit hit, 20f, mask, QueryTriggerInteraction.Ignore)) continue;
            if (hit.normal.y < 0.8f || hit.collider.GetComponentInParent<DeathZone>() != null || !FloorAround(hit.point, mask)) continue;
            if (Vector3.Distance(hit.point, position) < 3f) continue; // Not right on top of them
            int id = nextId++;
            expiry[id] = Time.time + Lifetime;
            photonView.RPC(nameof(RPC_SpawnDonut), RpcTarget.All, id, hit.point, false);
            return;
        }
    }

    /// <summary>The donuts out right now (the minimap marks them).</summary>
    public IEnumerable<Donut> Donuts => donuts.Values;

    [PunRPC]
    private void RPC_RequestDonut(int id, PhotonMessageInfo info)
    {
        if (!PhotonNetwork.IsMasterClient || info.Sender == null || !expiry.Remove(id)) return;
        MatchStats.Add(info.Sender, PlayerKeys.StatDonuts);
        photonView.RPC(nameof(RPC_TakeDonut), RpcTarget.All, id, info.Sender.ActorNumber);
    }

    // takenBy 0: it timed out
    [PunRPC]
    private void RPC_TakeDonut(int id, int takenBy)
    {
        bool golden = false;
        if (donuts.TryGetValue(id, out var donut))
        {
            if (donut != null)
            {
                golden = donut.Golden;
                donut.Pop(takenBy > 0);
            }
            donuts.Remove(id);
        }
        if (golden && takenBy > 0)
        {
            var taker = PhotonNetwork.CurrentRoom?.GetPlayer(takenBy);
            string name = taker == null ? "Someone" : string.IsNullOrEmpty(taker.NickName) ? $"Player {takenBy}" : taker.NickName;
            CombatHud.Post($"<color=#FFD23F><b>GOLDEN!</b> {name} grabbed a golden donut</color>"); // The fonts have no star glyph
        }
        if (takenBy <= 0 || takenBy != PhotonNetwork.LocalPlayer.ActorNumber || Player.Local == null) return;
        GiveLocal(golden);
    }

    // The local player gets a donut's effect (or a golden donut's boons)
    private void GiveLocal(bool golden)
    {
        var entry = golden ? new Donut.Entry(typeof(GoldenDonutEffect), 3f, 0f, Donut.Kind.Good) : Donut.Roll();
        if (entry.Everyone)
        {
            photonView.RPC(nameof(RPC_EveryoneEffect), RpcTarget.All, entry.Effect.Name, entry.Seconds);
            return;
        }
        var applied = PlayerEffect.Apply(entry.Effect, Player.Local, entry.Seconds);
        if (applied != null) Sfx.Here(applied.IsChaos ? SfxKind.Horn : applied.IsGood ? SfxKind.Heal : SfxKind.ZapDown, 0.8f);

        // Golden Donut: three more boons on top
        if (entry.Effect == typeof(GoldenDonutEffect))
            for (int i = 0; i < 3; i++)
            {
                var extra = Donut.Roll(boonsOnly: true, extra: true);
                if (extra.Everyone) photonView.RPC(nameof(RPC_EveryoneEffect), RpcTarget.All, extra.Effect.Name, extra.Seconds);
                else PlayerEffect.Apply(extra.Effect, Player.Local, extra.Seconds);
            }
    }

    // ─── Chaos effects that reach other players ───────────────────────────────

    // A donut effect for every player (Low Gravity Party)
    [PunRPC]
    private void RPC_EveryoneEffect(string typeName, float seconds)
    {
        var type = System.Type.GetType(typeName);
        if (type == null || Player.Local == null) return;
        PlayerEffect.Apply(type, Player.Local, seconds);
        Sfx.Here(SfxKind.Horn, 0.6f);
    }

    /// <summary>Swap: tells <paramref name="other"/> to move to <paramref name="position"/> (where the swapper was).</summary>
    public void SendSwap(Player other, Vector3 position)
    {
        if (other == null || other.Owner == null) return;
        photonView.RPC(nameof(RPC_SwapTo), other.Owner, position);
    }

    [PunRPC]
    private void RPC_SwapTo(Vector3 position, PhotonMessageInfo info)
    {
        var local = Player.Local;
        if (local == null || local.IsCaught) return;
        local.Teleport(position, local.transform.rotation);
        Fx.Respawn(position);
        string who = info.Sender != null ? info.Sender.NickName : "Someone";
        if (HUDManager.Instance != null) HUDManager.Instance.ShowBanner($"{who} swapped places with you!", 2.5f);
    }

    /// <summary>Fish Rain: fish fall around the local player and everyone else stares at them.</summary>
    public void SendFishRain(float seconds) =>
        photonView.RPC(nameof(RPC_FishRain), RpcTarget.All, PhotonNetwork.LocalPlayer.ActorNumber, seconds);

    [PunRPC]
    private void RPC_FishRain(int actor, float seconds)
    {
        var target = Player.Find(actor);
        if (target == null) return;
        FallingFish.Rain(target.transform.position, 10);

        var local = Player.Local;
        if (local == null || local == target || local.IsCaught || !local.AffectedBy(actor)) return;
        stareActor = actor;
        stareUntil = Time.time + seconds;
    }

    private void UpdateStare()
    {
        if (stareActor < 0) return;
        var target = Player.Find(stareActor);
        if (Time.time > stareUntil || target == null || PlayerCam.Local == null) { stareActor = -1; return; }
        PlayerCam.Local.Lure(target.transform.position + Vector3.up * target.EyeHeight);
    }

    [PunRPC]
    private void RPC_ClearDonuts()
    {
        foreach (var donut in donuts.Values)
            if (donut != null) donut.Pop(false);
        donuts.Clear();
    }
}
