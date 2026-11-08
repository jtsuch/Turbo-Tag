using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// A hunter's class in play (see <see cref="HunterClasses"/>), on every player copy (added by Player, so the RPCs
/// line up). The owner applies the passive and fires the left-click tool during the hunt; every screen shows the
/// tools' effects (beacons, traps, darts, sonar), and hunters' screens light up the hiders they find
/// (<see cref="HunterReveal"/>).
///   Speed:   +15% speed (climbing too) · Lunge · Sprint Burst · Pounce
///   Intel:   recent footprints · X-Ray Scan (cone, scanned walls blue) · Beacon · Sonar Pulse
///   Trapper: hiders' ability objects glow red · Alarm Trap · Snare · Tracker Dart
/// Everything placed is cleared when a round starts (<see cref="ClearAll"/>). The tool key is
/// <see cref="InputAction.HunterTool"/> (default left click). Cooldowns scale with the player's cooldown multiplier
/// (the admin console's "cooldown 0" removes them). With cheats on, in modes that don't use left click for guns or
/// the disc, any tool can be bound to a key of its own (<see cref="CheatBindings"/>, the Cheats tab's Hunter page)
/// and fired in any role; whoever fires a tool always sees what it finds.
/// </summary>
public class HunterKit : MonoBehaviourPun
{
    private const string PassiveSource = "HunterClass";
    private static readonly Color Red = new(1f, 0.25f, 0.25f);

    private Player player;
    private Rigidbody body;

    /// <summary>The local player's kit.</summary>
    public static HunterKit Local { get; private set; }

    // ─── Owner state (read by HunterToolHud) ──────────────────────────────────
    public HunterClasses.Class Class { get; private set; }
    public HunterClasses.Tool Tool { get; private set; }
    public bool InPlay { get; private set; }               // Hunting right now with a class
    public float CooldownLeft => Mathf.Max(0f, readyAt - Time.time);
    public float CooldownTotal { get; private set; } = 1f;
    public bool Scanning => Time.time < scanUntil;

    private float readyAt, scanUntil, nextScanWalls, nextPassive;
    private bool awaitingLastFrame, speedApplied;
    private int nextObjectId;
    private readonly List<int> myTraps = new();

    private void Awake()
    {
        player = GetComponent<Player>();
        body = GetComponent<Rigidbody>();
    }

    private bool pickChanged;

    private void Start()
    {
        if (!photonView.IsMine) return;
        Local = this;
        HunterClasses.Changed += OnPickChanged;
    }

    // ─── Catch streaks: quick catches refresh the tool ────────────────────────

    private const float StreakWindow = 20f;
    private float lastCatch = -99f;
    private GameModeManager watched;

    private void WatchTags()
    {
        var manager = GameModeManager.Instance;
        if (manager == watched) return;
        if (watched != null) watched.PlayerTagged -= OnTagged;
        watched = manager;
        if (watched != null) watched.PlayerTagged += OnTagged;
    }

    // The local hunter caught someone: the tool's wait halves, or is over at once on a second catch in quick succession
    private void OnTagged(Photon.Realtime.Player tagger, Photon.Realtime.Player target)
    {
        if (!InPlay || tagger == null || !tagger.IsLocal) return;
        bool streak = Time.time - lastCatch < StreakWindow;
        lastCatch = Time.time;
        readyAt = streak ? Time.time : Time.time + Mathf.Max(0f, readyAt - Time.time) * 0.5f;
        if (streak) Minimap.Pulse(Class != null ? Class.Color : Color.white, ping: false);
    }

    // Everyone can see which class each hunter took (the hiders' HUD): the pick goes to the room when the hunt starts
    private void SyncPick()
    {
        var me = Photon.Pun.PhotonNetwork.LocalPlayer;
        if (Class == null || me == null || !Photon.Pun.PhotonNetwork.InRoom) return;
        if (NetProps.Of(me, PlayerKeys.HunterClass, "") != Class.Id || NetProps.Of(me, PlayerKeys.HunterTool, "") != Tool.Id)
            NetProps.SetLocal(new ExitGames.Client.Photon.Hashtable { [PlayerKeys.HunterClass] = Class.Id, [PlayerKeys.HunterTool] = Tool.Id });
    }

    private void OnPickChanged() => pickChanged = true;

    private void OnDestroy()
    {
        if (Local == this) { Local = null; EndScan(); Overlay.Clear(); HunterClasses.Changed -= OnPickChanged; }
        if (watched != null) watched.PlayerTagged -= OnTagged;
    }

    // ─── Owner ────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (!photonView.IsMine || player == null) return;
        var manager = GameModeManager.Instance;
        var mode = manager != null ? manager.Mode : null;
        if (Class == null || pickChanged) { Class = HunterClasses.Chosen; Tool = HunterClasses.ChosenTool; pickChanged = false; }
        bool wasInPlay = InPlay;
        InPlay = mode != null && mode.HasHunterClasses && player.Role == PlayerRole.Hunter && !player.IsCaught
                 && manager.Phase == MatchPhase.Active;
        if (InPlay && !wasInPlay) SyncPick();
        WatchTags();

        // Speed's passive: a stat multiplier while hunting (climbing reads the speed stat too)
        bool wantSpeed = InPlay && Class.Id == "Speed";
        if (wantSpeed != speedApplied)
        {
            if (wantSpeed) player.Modifiers.Set(Stat.Speed, PassiveSource, 1.15f);
            else player.Modifiers.Remove(Stat.Speed, PassiveSource);
            speedApplied = wantSpeed;
        }

        if (InPlay && Time.time >= nextPassive)
        {
            nextPassive = Time.time + 0.3f;
            if (Class.Id == "Intel") ShowFootprints();
            if (Class.Id == "Trapper") TintEnemyObjects(true);
        }
        if (!(InPlay && Class.Id == "Trapper")) TintEnemyObjects(false);

        if (Scanning) UpdateScan(); else if (scanWalls.Count > 0 || wave != null) EndScan();
        if (InPlay && Class.Id == "Trapper") WatchAlarms();
        CheckSnares();

        // Left click fires the tool, unless an ability is using the Action key (aiming, placing)
        bool awaiting = player.Abilities != null && player.Abilities.IsAwaitingAction;
        bool free = InPlay && !awaiting && !awaitingLastFrame && player.CanUseAbilities && !player.ViewingCamera
                    && !PauseMenuManager.IsPaused && !HunterClassPicker.IsOpen && !PingWheel.IsOpen && PlayerCam.Local != null;
        awaitingLastFrame = awaiting;
        if (free && Time.time >= readyAt && Input.GetKeyDown(Keybinds.Get(InputAction.HunterTool)) && Use(Tool))
        {
            CooldownTotal = Mathf.Max(0.01f, Tool.Cooldown * player.CooldownMultiplier);
            readyAt = Time.time + Tool.Cooldown * player.CooldownMultiplier;
        }
        UpdateCheatTools();
    }

    // ─── Cheats: any tool on a key of its own ─────────────────────────────────

    /// <summary>Tool id → key, set from the Cheats tab (this match; cleared when cheats go off).</summary>
    public static readonly Dictionary<string, KeyCode> CheatBindings = new();
    private readonly Dictionary<string, float> cheatReady = new();

    public static void SetCheatBinding(string id, KeyCode key)
    {
        if (key == KeyCode.None) CheatBindings.Remove(id);
        else CheatBindings[id] = key;
    }

    /// <summary>Modes where the hunter tools can be bound and used through cheats: not where left click is taken (guns, the disc).</summary>
    public static bool CheatToolsAllowed()
    {
        var mode = GameModeManager.Instance != null ? GameModeManager.Instance.Mode : null;
        return mode != null && !mode.UsesGuns && mode is not KeepAwayMode;
    }

    private void UpdateCheatTools()
    {
        if (!Cheats.Enabled) { if (CheatBindings.Count > 0) CheatBindings.Clear(); return; }
        if (CheatBindings.Count == 0 || !CheatToolsAllowed() || PauseMenuManager.IsPaused || !player.CanUseAbilities
            || player.ViewingCamera || PlayerCam.Local == null || HunterClassPicker.IsOpen) return;
        foreach (var pair in CheatBindings)
        {
            if (!Input.GetKeyDown(pair.Value)) continue;
            var (_, tool) = HunterClasses.FindAnywhere(pair.Key);
            if (tool == null) continue;
            cheatReady.TryGetValue(tool.Id, out float ready);
            if (Time.time < ready) continue;
            if (Use(tool)) cheatReady[tool.Id] = Time.time + tool.Cooldown * player.CooldownMultiplier;
            break;
        }
    }

    // A quick widening of the view (movement tools), back to normal after `hold` seconds
    private System.Collections.IEnumerator FovKick(float add, float hold)
    {
        var cam = PlayerCam.Local;
        if (cam == null) yield break;
        cam.ChangeFov(cam.DefaultFov + add, 0.08f);
        yield return new WaitForSeconds(hold);
        if (cam != null) cam.ResetFov(0.35f);
    }

    // Returns false when nothing happened (no floor for a trap): no cooldown
    private bool Use(HunterClasses.Tool tool)
    {
        var cam = PlayerCam.Local.Camera.transform;
        Vector3 flat = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
        switch (tool.Id)
        {
            case "Lunge":
                body.linearVelocity += flat * 14f + Vector3.up * (player.IsGrounded ? 4f : 1.5f);
                player.NotePushed(0, null); // Movement doesn't brake it away at once
                StartCoroutine(FovKick(10f, 0.18f));
                break;
            case "SprintBurst":
                player.ApplyTimedModifier(Stat.Speed, "SprintBurst", 1.4f, 3f);
                StartCoroutine(FovKick(7f, 2.8f));
                Vfx.Burst(transform.position + Vector3.up, new Color(1f, 0.6f, 0.2f), 14, 4f, 0.2f, 0.4f);
                break;
            case "Pounce":
            {
                Vector3 target = Aim(cam, 25f, out _);
                Vector3 to = target - transform.position;
                float t = Mathf.Clamp(to.magnitude / 18f, 0.45f, 1.1f);
                float g = -Physics.gravity.y * player.Modifiers.Get(Stat.Gravity);
                Vector3 horizontal = Vector3.ProjectOnPlane(to, Vector3.up) / t;
                body.linearVelocity = horizontal + Vector3.up * ((to.y + 0.5f * g * t * t) / t + 1f);
                player.NotePushed(0, null);
                StartCoroutine(FovKick(8f, t));
                break;
            }
            case "XRayScan":
                scanUntil = Time.time + 3f;
                nextScanWalls = 0f;
                break;
            case "Beacon":
                photonView.RPC(nameof(RPC_Beacon), RpcTarget.All, cam.position + cam.forward * 0.8f, cam.forward * 55f + Vector3.up * 4f);
                break;
            case "Sonar":
                photonView.RPC(nameof(RPC_Sonar), RpcTarget.All, transform.position);
                break;
            case "Alarm":
            case "Snare":
            {
                if (!FloorAhead(cam, out Vector3 floor)) { Sfx.Here(SfxKind.Empty, 0.6f); return false; }
                int id = photonView.OwnerActorNr * 10000 + (++nextObjectId);
                myTraps.Add(id);
                // Too many of this kind out: the oldest goes
                var same = myTraps.FindAll(t => traps.TryGetValue(t, out var o) && o.Kind == tool.Id);
                if (same.Count >= tool.MaxOut && same.Count > 0)
                {
                    photonView.RPC(nameof(RPC_TrapGone), RpcTarget.All, same[0]);
                    myTraps.Remove(same[0]);
                }
                photonView.RPC(nameof(RPC_Trap), RpcTarget.All, id, floor, tool.Id);
                break;
            }
            case "Tracker":
            {
                Vector3 hit = Aim(cam, 45f, out Player victim);
                bool good = victim != null && victim != player && !IsHunterSide(victim);
                photonView.RPC(nameof(RPC_Dart), RpcTarget.All, cam.position + cam.forward * 0.6f, hit, good ? victim.ActorNumber : 0);
                break;
            }
        }
        photonView.RPC(nameof(RPC_ToolFx), RpcTarget.All, tool.Id);
        return true;
    }

    // Where the crosshair points (past your own body), up to range; the player hit, if any
    private Vector3 Aim(Transform cam, float range, out Player hitPlayer)
    {
        hitPlayer = null;
        Vector3 point = cam.position + cam.forward * range;
        float nearest = float.MaxValue;
        foreach (var hit in Physics.RaycastAll(cam.position, cam.forward, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(transform) || hit.distance >= nearest) continue;
            nearest = hit.distance;
            point = hit.point;
            hitPlayer = hit.collider.GetComponentInParent<Player>();
        }
        return point;
    }

    // Traps go on a floor: the spot under the crosshair (within 12 m), or the floor at your feet
    private bool FloorAhead(Transform cam, out Vector3 floor)
    {
        Vector3 aim = Aim(cam, 12f, out _);
        foreach (var from in new[] { aim + Vector3.up * 0.5f, transform.position + Vector3.up * 0.5f })
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && hit.normal.y > 0.6f && hit.collider.GetComponentInParent<Player>() == null)
            {
                floor = hit.point;
                return true;
            }
        floor = default;
        return false;
    }

    // ─── Passives ─────────────────────────────────────────────────────────────

    // Intel: hiders' last 3 seconds of footprints
    private void ShowFootprints()
    {
        if (TrailHistory.Instance == null) return;
        foreach (var p in Player.All)
        {
            if (p == null || p == player || IsHunterSide(p)) continue;
            foreach (var (time, at) in TrailHistory.Instance.Of(p.ActorNumber))
            {
                float age = Time.time - time;
                if (age > 3f) continue;
                Vfx.Burst(at + Vector3.up * 0.12f, new Color(0.4f, 0.75f, 1f, Mathf.Lerp(0.9f, 0.2f, age / 3f)), 1, 0.02f, 0.3f, 0.35f);
            }
        }
    }

    // Trapper: anything the other side placed or threw glows red (on this screen)
    private readonly HashSet<Renderer> tinted = new();
    private static Material redGlow, blueGlow;
    private static Material RedGlow => redGlow != null ? redGlow : redGlow = Glow(new Color(1f, 0.15f, 0.15f, 0.45f));
    private static Material BlueGlow => blueGlow != null ? blueGlow : blueGlow = Glow(new Color(0.3f, 0.6f, 1f, 0.3f));
    private static Material Glow(Color c) => new(Shader.Find("Sprites/Default")) { color = c, renderQueue = 3100 };

    private void TintEnemyObjects(bool on)
    {
        if (!on)
        {
            if (tinted.Count == 0) return;
            foreach (var r in tinted) Overlay.Remove(r);
            tinted.Clear();
            return;
        }
        foreach (var view in FindObjectsByType<PhotonView>(FindObjectsSortMode.None))
        {
            var data = view.InstantiationData;
            if (data == null || data.Length < 3 || data[2] is not int || view.GetComponent<Player>() != null) continue;
            var owner = Player.Find(view.OwnerActorNr);
            if (owner == null || IsHunterSide(owner)) continue;
            foreach (var r in view.GetComponentsInChildren<Renderer>())
                if (r is MeshRenderer or SkinnedMeshRenderer && tinted.Add(r)) Overlay.Add(r, RedGlow);
        }
        tinted.RemoveWhere(r => r == null);
    }

    // Not a target for the tools: hunters and ghosts (anyone else counts, so cheat tools work in Freeplay too)
    private static bool IsHunterSide(Player p) => p.Role == PlayerRole.Hunter || p.Role == PlayerRole.Ghost;

    // ─── X-Ray Scan (owner) ───────────────────────────────────────────────────

    private const float ScanRange = 30f, ScanAngle = 32f;
    private readonly HashSet<Renderer> scanWalls = new();

    private void UpdateScan()
    {
        var cam = PlayerCam.Local != null ? PlayerCam.Local.Camera.transform : null;
        if (cam == null) return;
        // Anyone on the other side inside the cone and within range shows through everything, the whole time
        foreach (var p in Player.All)
        {
            if (p == null || p == player || IsHunterSide(p) || p.IsDownAfterTag) continue;
            Vector3 to = p.transform.position + Vector3.up * 1.2f - cam.position;
            if (to.magnitude <= ScanRange && Vector3.Angle(cam.forward, to) <= ScanAngle) HunterReveal.Reveal(p.ActorNumber, 0.25f);
        }
        // A blue wave of light sweeps out from the hunter every second of the scan, stopping on the walls it reaches
        // and staying painted on them (blue walls: what the scan sees through), only on the hunter's screen.
        // (Not an extra material on the walls: level geometry is static-batched and that didn't draw.)
        if (Time.time >= nextScanWalls)
        {
            nextScanWalls = Time.time + 1f;
            wave ??= new ScanWave();
            wave.Start(cam, ScanRange, ScanAngle);
        }
        wave?.Update();
    }

    private ScanWave wave;

    /// <summary>
    /// The X-Ray Scan's light: a curved sheet of blue across the cone that rushes outward (40 m/s); each part stops
    /// where its ray meets a wall and the wall lights up as the sheet arrives. A mesh rebuilt each frame.
    /// </summary>
    private sealed class ScanWave
    {
        private const int Columns = 15, Rows = 7;
        private const float Speed = 40f, Pitch = 18f;
        private readonly GameObject go;
        private readonly Mesh mesh;
        private readonly Material material;
        private readonly Vector3[] dirs = new Vector3[Columns * Rows], verts = new Vector3[Columns * Rows];
        private readonly float[] stop = new float[Columns * Rows];
        private readonly Renderer[] walls = new Renderer[Columns * Rows];
        private readonly bool[] lit = new bool[Columns * Rows];
        private readonly Color[] colors = new Color[Columns * Rows];
        private Vector3 origin;
        private float startedAt, range;
        private readonly List<Renderer> reached = new();

        public ScanWave()
        {
            go = new GameObject("X-Ray Scan Wave");
            mesh = new Mesh { name = "Scan Wave" };
            mesh.MarkDynamic();
            var tris = new List<int>();
            for (int x = 0; x < Columns - 1; x++)
            for (int y = 0; y < Rows - 1; y++)
            {
                int i = x * Rows + y;
                tris.AddRange(new[] { i, i + 1, i + Rows, i + 1, i + Rows + 1, i + Rows });
                tris.AddRange(new[] { i, i + Rows, i + 1, i + 1, i + Rows, i + Rows + 1 }); // Both sides
            }
            mesh.vertices = verts;
            mesh.colors = colors;
            mesh.SetTriangles(tris, 0);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            material = new Material(Shader.Find("Sprites/Default"));
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
        }

        public void Start(Transform cam, float range, float angle)
        {
            this.range = range;
            origin = cam.position;
            startedAt = Time.time;
            for (int x = 0; x < Columns; x++)
            for (int y = 0; y < Rows; y++)
            {
                int i = x * Rows + y;
                float yaw = Mathf.Lerp(-angle, angle, x / (Columns - 1f));
                float pitch = Mathf.Lerp(-Pitch, Pitch, y / (Rows - 1f));
                dirs[i] = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(pitch, cam.right) * cam.forward;
                stop[i] = range;
                walls[i] = null;
                lit[i] = false;
                if (Physics.Raycast(origin, dirs[i], out RaycastHit hit, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && hit.collider.GetComponentInParent<Player>() == null)
                {
                    stop[i] = hit.distance;
                    var r = hit.collider.GetComponent<Renderer>();
                    if (r == null) r = hit.collider.GetComponentInParent<Renderer>();
                    walls[i] = r;
                }
            }
            go.SetActive(true);
            Sfx.Here(SfxKind.Warp, 0.35f, 1.6f);
        }

        // Moves the sheet out; returns the walls it reached this frame
        public List<Renderer> Update()
        {
            reached.Clear();
            if (!go.activeSelf) return reached;
            float reach = (Time.time - startedAt) * Speed;
            float fade = 1f - Mathf.Clamp01(reach / range);
            for (int i = 0; i < verts.Length; i++)
            {
                float r = Mathf.Min(reach, stop[i] - 0.15f); // Just off the wall (sitting on it flickered)
                verts[i] = origin + dirs[i] * Mathf.Max(0.6f, r);
                bool atWall = reach >= stop[i] - 0.15f && stop[i] < range;
                // On a wall it stays lit (until the next sweep); in the open it fades as it goes
                colors[i] = new Color(0.35f, 0.7f, 1f, atWall ? 0.42f : 0.35f * fade);
                if (atWall && !lit[i]) { lit[i] = true; if (walls[i] != null) reached.Add(walls[i]); }
            }
            mesh.vertices = verts;
            mesh.colors = colors;
            mesh.RecalculateBounds();
            return reached;
        }

        public void Destroy()
        {
            if (go != null) Object.Destroy(go);
            if (material != null) Object.Destroy(material);
        }
    }

    private void EndScan()
    {
        foreach (var r in scanWalls) Overlay.Remove(r);
        scanWalls.Clear();
        wave?.Destroy();
        wave = null;
    }

    // ─── Traps (every screen keeps the visuals; the placer watches alarms, each player their own snares) ───

    private sealed class TrapObject { public int Id, Owner; public string Kind; public GameObject Visual; public Vector3 Point; public Renderer[] Renderers; }
    private static readonly Dictionary<int, TrapObject> traps = new();
    private float nextAlarmCheck;

    [PunRPC]
    private void RPC_Trap(int id, Vector3 point, string kind)
    {
        var root = new GameObject(kind == "Alarm" ? "Alarm Trap" : "Snare");
        root.transform.position = point + Vector3.up * 0.03f;
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(disc.GetComponent<Collider>());
        disc.transform.SetParent(root.transform, false);
        disc.transform.localScale = kind == "Alarm" ? new Vector3(1.1f, 0.03f, 1.1f) : new Vector3(0.8f, 0.05f, 0.8f);
        var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(core.GetComponent<Collider>());
        core.transform.SetParent(root.transform, false);
        core.transform.localScale = Vector3.one * 0.25f;
        core.transform.localPosition = Vector3.up * 0.08f;
        var mat = new Material(Shader.Find("Sprites/Default")) { color = kind == "Alarm" ? new Color(1f, 0.3f, 0.25f, 0.8f) : new Color(1f, 0.75f, 0.2f, 0.8f) };
        foreach (var r in root.GetComponentsInChildren<Renderer>()) { r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
        traps[id] = new TrapObject { Id = id, Owner = photonView.OwnerActorNr, Kind = kind, Visual = root, Point = point, Renderers = root.GetComponentsInChildren<Renderer>() };
        Sfx.At(SfxKind.Place, point, 0.7f, 20f);
    }

    [PunRPC]
    private void RPC_TrapGone(int id)
    {
        if (!traps.TryGetValue(id, out var trap)) return;
        if (trap.Visual != null) Destroy(trap.Visual);
        traps.Remove(id);
    }

    // An alarm went off or a snare caught someone
    [PunRPC]
    private void RPC_TrapFired(int id, int victim)
    {
        if (!traps.TryGetValue(id, out var trap)) return;
        traps.Remove(id);
        if (trap.Visual != null)
        {
            Vfx.Ring(trap.Point + Vector3.up * 0.2f, Red, 24, 6f, 0.3f, 0.6f);
            Destroy(trap.Visual);
        }
        Sfx.At(trap.Kind == "Alarm" ? SfxKind.Tick : SfxKind.Snap, trap.Point, 1f, 40f, trap.Kind == "Alarm" ? 0.7f : 1f);
        var local = Player.Local;
        var who = Player.Find(victim);
        string name = who != null ? who.DisplayName : "Someone";
        if (local != null && (local.Role == PlayerRole.Hunter || trap.Owner == local.ActorNumber))
        {
            HunterReveal.Reveal(victim, trap.Kind == "Alarm" ? 6f : 3f);
            if (HUDManager.Instance != null) HUDManager.Instance.ShowBanner(trap.Kind == "Alarm" ? $"<size=70%>ALARM</size>\n{name} SPOTTED" : $"<size=70%>SNARE</size>\n{name} IS STUCK", 2f);
            Sfx.Here(SfxKind.Horn, 0.4f, 1.6f);
        }
        else if (local != null && local.ActorNumber == victim)
            Minimap.Pulse(new Color(1f, 0.25f, 0.3f)); // You tripped it: quietly
    }

    // The placer's screen: a hider within 10 m of one of their alarms sets it off
    private void WatchAlarms()
    {
        if (Time.time < nextAlarmCheck) return;
        nextAlarmCheck = Time.time + 0.2f;
        foreach (var id in myTraps)
        {
            if (!traps.TryGetValue(id, out var trap) || trap.Kind != "Alarm") continue;
            foreach (var p in Player.All)
            {
                if (p == null || p == player || IsHunterSide(p) || p.IsCaught || p.IsDownAfterTag) continue;
                if (Vector3.Distance(p.transform.position, trap.Point) > 10f) continue;
                photonView.RPC(nameof(RPC_TrapFired), RpcTarget.All, id, p.ActorNumber);
                myTraps.Remove(id);
                return;
            }
        }
    }

    // Each screen: its own player stepping on a snare is stuck
    private static void CheckSnares()
    {
        var local = Player.Local;
        if (local == null || IsHunterSide(local) || local.IsCaught || traps.Count == 0) return;
        foreach (var trap in traps.Values)
        {
            if (trap.Kind != "Snare" || trap.Owner == local.ActorNumber || Vector3.Distance(local.transform.position, trap.Point) > 1.3f) continue;
            local.HoldFor(1.5f);
            var owner = Player.Find(trap.Owner);
            if (owner != null && owner.TryGetComponent(out HunterKit kit)) kit.photonView.RPC(nameof(RPC_TrapFired), RpcTarget.All, trap.Id, local.ActorNumber);
            return;
        }
    }

    // Hunters see their traps; hiders only make them out faintly up close (alarm 15 m, snare 8 m)
    private void LateUpdate()
    {
        if (!photonView.IsMine || traps.Count == 0) return;
        bool hunter = player.Role == PlayerRole.Hunter;
        foreach (var trap in traps.Values)
        {
            if (trap.Visual == null) continue;
            float near = trap.Kind == "Alarm" ? 15f : 8f;
            float d = Vector3.Distance(transform.position, trap.Point);
            bool show = hunter || d <= near;
            foreach (var r in trap.Renderers) if (r != null) r.enabled = show;
            if (!show || trap.Renderers.Length == 0 || trap.Renderers[0] == null) continue;
            var c = trap.Renderers[0].sharedMaterial.color;
            c.a = hunter ? 0.55f + 0.3f * Mathf.Sin(Time.time * 3f) : Mathf.Lerp(0.25f, 0.06f, d / near);
            trap.Renderers[0].sharedMaterial.color = c;
        }
    }

    // ─── Beacon, Sonar, Dart (every screen) ───────────────────────────────────

    private static readonly List<GameObject> beacons = new();

    [PunRPC]
    private void RPC_Beacon(Vector3 origin, Vector3 velocity)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Beacon";
        go.transform.position = origin;
        go.transform.localScale = Vector3.one * 0.3f;
        go.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.4f, 0.75f, 1f) };
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 0.3f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.linearVelocity = velocity;
        var col = go.GetComponent<Collider>();
        foreach (var p in Player.All) if (p != null) foreach (var c in p.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(col, c);
        go.AddComponent<BeaconDevice>().Mine = photonView.IsMine;
        beacons.Add(go);
        Destroy(go, 8f);
    }

    [PunRPC]
    private void RPC_Sonar(Vector3 at)
    {
        Vfx.Ring(at + Vector3.up * 1f, new Color(0.4f, 0.75f, 1f), 48, 15f, 0.35f, 0.9f);
        Sfx.At(SfxKind.Warp, at, 1f, 45f, 0.6f);
        var local = Player.Local;
        if (local == null) return;
        if (local.Role == PlayerRole.Hunter || photonView.IsMine)
        {
            foreach (var p in Player.All)
                if (p != null && p != local && !IsHunterSide(p) && Vector3.Distance(p.transform.position, at) <= 15f) HunterReveal.Reveal(p.ActorNumber, 2.5f);
        }
        else if (Vector3.Distance(local.transform.position, at) <= 15f)
            Minimap.Pulse(new Color(0.4f, 0.75f, 1f));
    }

    [PunRPC]
    private void RPC_Dart(Vector3 from, Vector3 to, int victim)
    {
        GunFx.Tracer(from, to);
        Sfx.At(SfxKind.Thwip, from, 0.9f, 35f, 1.3f);
        if (victim == 0) return;
        Vfx.Burst(to, new Color(0.5f, 1f, 0.4f), 8, 2f, 0.15f, 0.4f);
        var local = Player.Local;
        if (local == null) return;
        if (local.Role == PlayerRole.Hunter || photonView.IsMine) HunterReveal.Reveal(victim, 8f);
        else if (local.ActorNumber == victim) Minimap.Pulse(new Color(0.5f, 1f, 0.4f));
    }

    // The tool's sound for everyone (and the sound visualizer)
    [PunRPC]
    private void RPC_ToolFx(string id)
    {
        Vector3 at = transform.position + Vector3.up * 1.2f;
        SfxKind sound = id switch
        {
            "Lunge" => SfxKind.Whoosh, "SprintBurst" => SfxKind.ZapUp, "Pounce" => SfxKind.Jump,
            "XRayScan" => SfxKind.Warp, "Beacon" => SfxKind.Toss, "Tracker" => SfxKind.Thwip, _ => SfxKind.Place,
        };
        Sfx.At(sound, at, 0.9f, 40f);
        if (!photonView.IsMine) SoundVisualizer.Notify(transform.position, 0.9f, photonView.OwnerActorNr);
    }

    // ─── Round reset ──────────────────────────────────────────────────────────

    /// <summary>Every screen, when a round starts: traps, beacons, reveals and the local cooldown go.</summary>
    public static void ClearAll()
    {
        foreach (var trap in traps.Values) if (trap.Visual != null) Destroy(trap.Visual);
        traps.Clear();
        foreach (var b in beacons) if (b != null) Destroy(b);
        beacons.Clear();
        HunterReveal.Clear();
        if (Local != null) { Local.readyAt = 0f; Local.scanUntil = 0f; Local.myTraps.Clear(); }
    }

    // ─── Material overlays (scanned walls, red enemy objects): an extra material on the renderer, local ───

    private static class Overlay
    {
        private static readonly Dictionary<Renderer, Material[]> originals = new();

        public static void Add(Renderer r, Material m)
        {
            if (r == null || originals.ContainsKey(r)) return;
            var mats = r.sharedMaterials;
            originals[r] = mats;
            var with = new Material[mats.Length + 1];
            mats.CopyTo(with, 0);
            with[mats.Length] = m;
            r.sharedMaterials = with;
        }

        public static void Remove(Renderer r)
        {
            if (r == null || !originals.TryGetValue(r, out var mats)) { originals.Remove(r); return; }
            r.sharedMaterials = mats;
            originals.Remove(r);
        }

        public static void Clear()
        {
            foreach (var pair in originals) if (pair.Key != null) pair.Key.sharedMaterials = pair.Value;
            originals.Clear();
        }
    }
}

/// <summary>
/// An Intel hunter's thrown beacon (a local copy on every screen): sticks to the first surface, then after a beat
/// flashes, and on hunters' screens every hider it can see lights up for 2 s.
/// </summary>
public class BeaconDevice : MonoBehaviour
{
    /// <summary>Thrown from this screen: its thrower always sees what it finds (cheat tools in any role).</summary>
    public bool Mine;
    private bool stuck;
    private Light glow;

    private void Start()
    {
        glow = gameObject.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = new Color(0.4f, 0.75f, 1f);
        glow.range = 5f;
        glow.intensity = 2f;
        glow.shadows = LightShadows.None;
    }

    // Blinks (faster once stuck and about to go off)
    private void Update()
    {
        if (glow == null) return;
        float rate = stuck ? 14f : 6f;
        glow.intensity = Mathf.Sin(Time.time * rate) > 0f ? 2.5f : 0.4f;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (stuck || collision.collider.GetComponentInParent<Player>() != null) return;
        stuck = true;
        var rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        Sfx.At(SfxKind.Place, transform.position, 0.7f, 25f, 1.4f);
        // A hider near where it lands gets a moment's warning (a blue map pulse) before it goes off
        var local = Player.Local;
        if (!Mine && local != null && local.Role != PlayerRole.Hunter && Vector3.Distance(local.transform.position, transform.position) < 20f)
            Minimap.Pulse(new Color(0.4f, 0.75f, 1f));
        Invoke(nameof(Activate), 0.6f);
    }

    private void Activate()
    {
        Vector3 at = transform.position;
        Vfx.Ring(at, new Color(0.4f, 0.75f, 1f), 36, 10f, 0.35f, 0.7f);
        if (glow != null) { glow.range = 18f; glow.intensity = 8f; enabled = false; } // One bright flash
        Sfx.At(SfxKind.Shimmer, at, 1f, 50f, 0.8f);
        var local = Player.Local;
        if (local != null && (local.Role == PlayerRole.Hunter || Mine))
            foreach (var p in Player.All)
            {
                if (p == null || p == local || p.Role == PlayerRole.Hunter || p.Role == PlayerRole.Ghost || p.IsDownAfterTag) continue;
                Vector3 chest = p.transform.position + Vector3.up * 1.2f;
                if (Vector3.Distance(chest, at) > 40f) continue;
                // In its line of sight: nothing but the hider between them
                bool seen = true;
                foreach (var hit in Physics.RaycastAll(at, (chest - at).normalized, Vector3.Distance(chest, at), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    if (hit.collider.GetComponentInParent<Player>() == null && hit.collider.gameObject != gameObject) { seen = false; break; }
                if (seen) HunterReveal.Reveal(p.ActorNumber, 2f);
            }
        Destroy(gameObject, 1.2f);
    }
}
