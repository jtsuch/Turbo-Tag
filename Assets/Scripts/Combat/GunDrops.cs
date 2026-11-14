using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Guns on the floor in modes with an economy (Search &amp; Destroy), like Valorant: a player who dies drops their best
/// gun, and one swapped for a bought or picked-up gun drops at their feet. Walking over a dropped gun whose slot is
/// empty (no primary; only the free Pebble) picks it up; otherwise the Grab key (F) swaps it for the one you carry.
/// The host hands out drop ids and grants pickups (the first asker gets it); every screen shows the guns lying there.
/// They're cleared when the next round's pre-round starts. Added next to the GameModeManager in gun modes (shares
/// its PhotonView; RPCs prefixed Drop).
/// </summary>
public class GunDrops : MonoBehaviourPun
{
    public static GunDrops Instance { get; private set; }

    private const float PickupRange = 1.8f;

    private sealed class Drop { public string Gun; public Vector3 At; public GameObject Model; }
    private readonly Dictionary<int, Drop> drops = new();
    private int nextId = 1;
    private float nextAsk;
    private GameModeManager manager;

    private void Awake()
    {
        Instance = this;
        manager = GetComponent<GameModeManager>();
    }

    private void OnEnable()
    {
        if (manager == null) manager = GetComponent<GameModeManager>();
        if (manager != null) manager.PhaseChanged += HandlePhase;
    }

    private void OnDisable()
    {
        if (manager != null) manager.PhaseChanged -= HandlePhase;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        ClearAll();
    }

    private void HandlePhase(MatchPhase phase)
    {
        if (phase == MatchPhase.Countdown || phase == MatchPhase.MatchEnd) ClearAll();
    }

    private void ClearAll()
    {
        foreach (var d in drops.Values) if (d.Model != null) Destroy(d.Model);
        drops.Clear();
    }

    // ─── Dropping ─────────────────────────────────────────────────────────────

    /// <summary>Local: drop <paramref name="gunId"/> at <paramref name="at"/> (the host gives it an id and shows it everywhere).</summary>
    public void RequestDrop(string gunId, Vector3 at)
    {
        if (GunCatalog.Find(gunId) == null) return;
        if (PhotonNetwork.InRoom) photonView.RPC(nameof(RPC_DropRequest), RpcTarget.MasterClient, gunId, at);
        else RPC_Dropped(nextId++, gunId, at);
    }

    [PunRPC]
    private void RPC_DropRequest(string gunId, Vector3 at)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        foreach (int id in drops.Keys) nextId = Mathf.Max(nextId, id + 1);   // A new host carries on the numbering
        photonView.RPC(nameof(RPC_Dropped), RpcTarget.All, nextId++, gunId, at);
    }

    [PunRPC]
    private void RPC_Dropped(int id, string gunId, Vector3 at)
    {
        var gun = GunCatalog.Find(gunId);
        if (gun == null || drops.ContainsKey(id)) return;
        // On the floor under where it was dropped; nothing under it (fell into the void): the gun is gone. Every
        // screen decides the same from the same level
        if (!FloorUnder(at, out at)) return;
        drops[id] = new Drop { Gun = gunId, At = at, Model = BuildModel(gun, at, id) };
    }

    private static bool FloorUnder(Vector3 at, out Vector3 floor)
    {
        floor = at;
        int count = Physics.RaycastNonAlloc(at + Vector3.up * 1.5f, Vector3.down, floorHits, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            var c = floorHits[i].collider;
            if (c.GetComponentInParent<Player>() != null || c.GetComponentInParent<SpawnBarrier>() != null) continue;
            if (floorHits[i].distance < best) { best = floorHits[i].distance; floor = floorHits[i].point; }
        }
        return best < float.MaxValue;
    }
    private static readonly RaycastHit[] floorHits = new RaycastHit[16];

    // The gun's model lying on its side (no collider: a pickup is a distance check)
    private static GameObject BuildModel(GunInfo gun, Vector3 at, int id)
    {
        var prefab = gun.Model != null ? Resources.Load<GameObject>(gun.Model) : null;
        GameObject go = prefab != null ? Instantiate(prefab) : GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = $"Dropped {gun.Name}";
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
        go.transform.localScale = prefab != null ? Vector3.one * gun.WorldLength : new Vector3(0.15f, 0.25f, 0.5f);
        go.transform.SetPositionAndRotation(at + Vector3.up * 0.15f, Quaternion.Euler(0f, id * 47f % 360f, 90f));
        return go;
    }

    // ─── Picking up ───────────────────────────────────────────────────────────

    private void Update()
    {
        var loadout = Loadout.Instance;
        var local = Player.Local;
        if (drops.Count == 0 || loadout == null || !loadout.HasEconomy || local == null || local.IsCaught || manager == null) return;
        if (manager.Phase != MatchPhase.Countdown && manager.Phase != MatchPhase.Active) return;

        int nearest = -1;
        float best = PickupRange;
        foreach (var pair in drops)
        {
            Vector3 d = pair.Value.At - local.transform.position;
            if (Mathf.Abs(d.y) > 2f) continue;
            float flat = new Vector2(d.x, d.z).magnitude;
            if (flat < best) { best = flat; nearest = pair.Key; }
        }
        if (nearest < 0) return;

        var gun = GunCatalog.Find(drops[nearest].Gun);
        bool ask = loadout.SlotFree(gun);
        if (!ask)
        {
            KeyCode grab = Keybinds.Get(InputAction.Grab);
            HUDManager.Prompt(grab, $"Swap for the {gun.Name}");
            if (Input.GetKeyDown(grab)) { local.ClaimGrabKey(); ask = true; nextAsk = 0f; }
        }
        if (!ask || Time.time < nextAsk) return;
        nextAsk = Time.time + 0.5f;
        if (PhotonNetwork.InRoom) photonView.RPC(nameof(RPC_DropPickup), RpcTarget.MasterClient, nearest);
        else RPC_DropTaken(nearest, 0);
    }

    [PunRPC]
    private void RPC_DropPickup(int id, PhotonMessageInfo info)
    {
        if (!PhotonNetwork.IsMasterClient || !drops.ContainsKey(id) || info.Sender == null) return;
        photonView.RPC(nameof(RPC_DropTaken), RpcTarget.All, id, info.Sender.ActorNumber);
    }

    [PunRPC]
    private void RPC_DropTaken(int id, int actor)
    {
        if (!drops.TryGetValue(id, out var drop)) return;
        drops.Remove(id);
        if (drop.Model != null) Destroy(drop.Model);
        bool mine = !PhotonNetwork.InRoom || actor == PhotonNetwork.LocalPlayer.ActorNumber;
        if (mine && Loadout.Instance != null) Loadout.Instance.TakeDrop(drop.Gun);
    }
}
