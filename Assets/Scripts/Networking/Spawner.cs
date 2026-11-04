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

        // Gun modes: falling off is a death (a self destruction), not a free trip back
        if (CombatManager.Instance != null) CombatManager.Instance.ReportFall();
        else RespawnLocalPlayer();
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
        PenguinSkins.EnsureLocal();

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
            SpawnPoint point = PickSpawnPoint();
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

        // Spread evenly in a circle, facing outward
        float   angle  = 360f / Mathf.Max(1, total) * index * Mathf.Deg2Rad;
        Vector3 dir    = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        position = transform.position + dir * (hunter ? hunterSpawnRadius : spawnRadius);
        rotation = Quaternion.LookRotation(dir, Vector3.up);
    }

    /// <summary>
    /// Before the match runs (Waiting, Countdown) everyone takes a different point, spread through the list.
    /// Mid-match: a random one of the three points furthest from the nearest other living player.
    /// </summary>
    private static SpawnPoint PickSpawnPoint()
    {
        var points = new List<SpawnPoint>(SpawnPoint.All);
        points.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        var phase = GameModeManager.Instance != null ? GameModeManager.Instance.Phase : MatchPhase.Waiting;
        if (phase == MatchPhase.Waiting || phase == MatchPhase.Countdown)
        {
            int index = LocalPlayerIndex(out int total);
            int step  = Mathf.Max(1, points.Count / Mathf.Max(1, total));
            return points[(index * step) % points.Count];
        }

        var others = new List<Vector3>();
        foreach (var p in Player.All)
            if (!p.IsLocal && !p.IsDownAfterTag) others.Add(p.transform.position);

        float Safety(SpawnPoint point)
        {
            float nearest = float.MaxValue;
            foreach (var pos in others) nearest = Mathf.Min(nearest, Vector3.Distance(pos, point.transform.position));
            return nearest;
        }

        points.Sort((a, b) => Safety(b).CompareTo(Safety(a)));
        return points[Random.Range(0, Mathf.Min(3, points.Count))];
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
