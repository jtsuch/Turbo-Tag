using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Spawns and respawns the local player in a level, and initialises the scene HUD and pause
/// menu once that player exists.
///
/// Spawn points: assign hider/hunter spawn transforms in the Inspector, or leave them empty to
/// spread players in a circle around this object (hunters in a tighter circle). Players that
/// fall below <see cref="killHeight"/> are respawned automatically.
/// Attach to: one Spawner GameObject per level scene.
/// </summary>
public class Spawner : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────
    private static Spawner _instance;
    public static Spawner Instance => _instance;

    private const string PlayerPrefab = "Player/ThePlayer";

    [Header("Scene UI (found automatically if left empty)")]
    [SerializeField] private GameObject playerHUD;
    [SerializeField] private GameObject pauseMenu;

    [Header("Spawn Points (optional)")]
    [SerializeField] private Transform[] hiderSpawns;
    [SerializeField] private Transform[] hunterSpawns;
    [SerializeField] private float spawnRadius       = 5f;
    [SerializeField] private float hunterSpawnRadius = 1f;

    [Header("Out of Bounds")]
    [Tooltip("Players below this height are respawned.")]
    [SerializeField] private float killHeight = -100f;

    private bool spawned;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        _instance = this;
        Player.LocalPlayerSpawned += InitializeSceneUI;
    }

    private void OnDestroy()
    {
        Player.LocalPlayerSpawned -= InitializeSceneUI;
        if (_instance == this) _instance = null;
    }

    private void Start()
    {
        // In offline debug mode the room does not exist yet; DebugSceneBootstrapper calls us later
        if (PhotonNetwork.InRoom) SpawnLocalPlayer();
    }

    private void Update()
    {
        var local = Player.Local;
        if (local == null || local.IsCaught || local.transform.position.y >= killHeight) return;
        LocalPlayerFell();
    }

    /// <summary>
    /// The local player fell out of the level (below the kill height, or onto a <see cref="DeathZone"/>):
    /// in gun modes that's a death (a self destruction), otherwise a free trip back to a spawn.
    /// </summary>
    public void LocalPlayerFell()
    {
        var local = Player.Local;
        if (local == null || local.IsCaught) return;
        if (CombatManager.Instance != null) CombatManager.Instance.ReportFall();
        else
        {
            RespawnLocalPlayer(); // Back on solid ground first, so a catch's ragdoll isn't left in the void
            if (GameModeManager.Instance != null) GameModeManager.Instance.ReportFall();
        }
    }

    // ─── Spawning ─────────────────────────────────────────────────────────────

    public void SpawnLocalPlayer()
    {
        // One local player per client, whatever spawns it: a second Spawner in a level used to create
        // a second player that read the same input (others saw two copies moving together)
        if (spawned || Player.Local != null) return;
        if (!PhotonNetwork.InRoom)
        {
            Debug.LogError("[Spawner] Not in a room — cannot spawn.");
            return;
        }

        spawned = true;
        AbilityLoadout.EnsureLocal();
        PenguinSkins.EnsureLocal(); Cosmetics.EnsureLocal();

        GetSpawnPoint(PlayerRoles.Get(PhotonNetwork.LocalPlayer), out Vector3 position, out Quaternion rotation);
        PhotonNetwork.Instantiate(PlayerPrefab, position, rotation);
    }

    /// <summary>Moves the local player to the spawn point for their current role.</summary>
    public void RespawnLocalPlayer()
    {
        var local = Player.Local;
        if (local == null) return;

        GetSpawnPoint(local.Role, out Vector3 position, out Quaternion rotation);
        local.Teleport(position, rotation);
        if (PlayerCam.Local != null) PlayerCam.Local.SnapTo(rotation.eulerAngles.y);
    }

    private void GetSpawnPoint(PlayerRole role, out Vector3 position, out Quaternion rotation)
    {
        var mode = GameModeManager.Instance != null ? GameModeManager.Instance.Mode : null;
        if (mode != null && mode.UsesSpawnPoints && SpawnPoint.All.Count > 0)
        {
            SpawnPoint point = PickSpawnPoint(role);
            position = point.transform.position;
            rotation = Quaternion.Euler(0f, point.transform.eulerAngles.y, 0f);
            return;
        }

        int index = LocalPlayerIndex(out int total);
        bool hunter = role == PlayerRole.Hunter;

        Transform[] points = hunter ? hunterSpawns : hiderSpawns;
        if (points != null && points.Length > 0)
        {
            Transform point = points[index % points.Length];
            position = point.position;
            rotation = point.rotation;
            return;
        }

        // Spread evenly in a circle, facing outward; a spot on a bounce pad turns further round the circle
        float   radius = hunter ? hunterSpawnRadius : spawnRadius;
        float   angle  = 360f / Mathf.Max(1, total) * index;
        Vector3 dir    = Vector3.forward;
        for (int turn = 0; turn < 18; turn++, angle += 20f)
        {
            dir = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad));
            if (!NearBouncePad(transform.position + dir * radius)) break;
        }
        position = transform.position + dir * radius;
        rotation = Quaternion.LookRotation(dir, Vector3.up);
    }

    // A pad within reach of a spawning player's feet (Pinnacle's plaza pad sat on the first ring spot)
    private static bool NearBouncePad(Vector3 feet)
    {
        foreach (var hit in Physics.OverlapSphere(feet + Vector3.up, 3f, ~0, QueryTriggerInteraction.Collide))
            if (hit.GetComponentInParent<BouncePad>() != null) return true;
        return false;
    }

    private static SpawnPoint lastSpawnPoint;

    /// <summary>
    /// Before the match runs (Waiting, Countdown) everyone takes a different point: the list is shuffled with a
    /// seed every client shares (the phase start time, so each match differs), then dealt out by player index.
    /// Teams start on opposite halves of the map (the points split along the level's longer side, red one end,
    /// blue the other), dealt out among teammates.
    /// Mid-match: any point at least <see cref="SafeSpawnDistance"/> from every other living player (only enemies
    /// count in team modes; never the one you last used) is equally likely; if none is that safe, a random one of
    /// the safer half.
    /// </summary>
    private static SpawnPoint PickSpawnPoint(PlayerRole role)
    {
        var points = new List<SpawnPoint>(SpawnPoint.All);
        points.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        bool team = PlayerRoles.IsTeam(role);

        var phase = GameModeManager.Instance != null ? GameModeManager.Instance.Phase : MatchPhase.Waiting;
        if (phase == MatchPhase.Waiting || phase == MatchPhase.Countdown)
        {
            if (team && points.Count >= 2)
            {
                var mode = GameModeManager.Instance != null ? GameModeManager.Instance.Mode : null;
                var chosen = mode?.TeamSpawns(points, role);
                points = chosen != null && chosen.Count > 0 ? chosen : TeamHalf(points, role);
            }
            double start = NetProps.Room(RoomKeys.PhaseStart, 0.0);
            var shuffle = new System.Random((int)(start * 1000.0 % int.MaxValue));
            for (int i = points.Count - 1; i > 0; i--)
            {
                int j = shuffle.Next(i + 1);
                (points[i], points[j]) = (points[j], points[i]);
            }
            int index = team ? TeammateIndex(role) : LocalPlayerIndex(out _);
            return lastSpawnPoint = points[index % points.Count];
        }

        var others = new List<Vector3>();
        foreach (var p in Player.All)
            if (!p.IsLocal && !p.IsDownAfterTag && !(team && p.Role == role)) others.Add(p.transform.position);

        float Safety(SpawnPoint point)
        {
            float nearest = float.MaxValue;
            foreach (var pos in others) nearest = Mathf.Min(nearest, Vector3.Distance(pos, point.transform.position));
            return nearest;
        }

        if (points.Count > 1) points.Remove(lastSpawnPoint);
        var safe = points.FindAll(point => Safety(point) >= SafeSpawnDistance);
        if (safe.Count == 0)
        {
            points.Sort((a, b) => Safety(b).CompareTo(Safety(a)));
            safe = points.GetRange(0, Mathf.Max(1, (points.Count + 1) / 2));
        }
        return lastSpawnPoint = safe[Random.Range(0, safe.Count)];
    }

    private const float SafeSpawnDistance = 20f; // Metres from the nearest other living player

    // The half of the spawn points on this team's end: sorted along the axis the points spread out on most
    public static List<SpawnPoint> TeamHalf(List<SpawnPoint> points, PlayerRole role)
    {
        Bounds b = new(points[0].transform.position, Vector3.zero);
        foreach (var p in points) b.Encapsulate(p.transform.position);
        bool alongX = b.size.x >= b.size.z;
        var sorted = new List<SpawnPoint>(points);
        sorted.Sort((a, c) => (alongX ? a.transform.position.x : a.transform.position.z)
                             .CompareTo(alongX ? c.transform.position.x : c.transform.position.z));
        int half = Mathf.Max(1, sorted.Count / 2);
        return role == PlayerRole.TeamRed ? sorted.GetRange(0, half) : sorted.GetRange(sorted.Count - half, half);
    }

    // The local player's place among their teammates (by actor number), so teammates take different points
    private static int TeammateIndex(PlayerRole role)
    {
        int index = 0;
        foreach (var p in PhotonNetwork.PlayerList) // Sorted by actor number
        {
            if (p.IsLocal) return index;
            if (PlayerRoles.Get(p) == role) index++;
        }
        return index;
    }

    // Stable 0-based index of the local player among everyone in the room
    private static int LocalPlayerIndex(out int total)
    {
        var players = PhotonNetwork.PlayerList; // Sorted by actor number
        total = players.Length;
        for (int i = 0; i < players.Length; i++)
            if (players[i].IsLocal) return i;
        return 0;
    }

    // ─── Scene UI ─────────────────────────────────────────────────────────────

    private void InitializeSceneUI(Player local)
    {
        HUDManager hud = playerHUD != null ? playerHUD.GetComponent<HUDManager>() : HUDManager.Instance;
        if (hud != null)
        {
            hud.gameObject.SetActive(true);
            hud.Initialize(local);
        }
        else Debug.LogWarning("[Spawner] HUDManager not found.");

        PauseMenuManager pause = pauseMenu != null ? pauseMenu.GetComponent<PauseMenuManager>() : PauseMenuManager.Instance;
        if (pause != null) pause.Initialize();
        else Debug.LogWarning("[Spawner] PauseMenuManager not found.");
    }
}
