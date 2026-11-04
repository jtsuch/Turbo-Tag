using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Killcam for gun modes. Every client keeps the last few seconds of every player as seen on this screen:
/// position, facing, look pitch, eye height, movement state, animation values and the gun in hand, plus their
/// shots. When the local player is shot dead it replays <see cref="Before"/> seconds before the death and
/// <see cref="After"/> after, from the killer's eyes, with the killer's gun on the view:
///   - the real bodies (and their guns) are hidden (forceRenderingOff), and each other player is played by a
///     stand-in (a copy of their body, like the ragdoll, with its own Animator fed the recorded values) that
///     walks the recorded path, holding the recorded gun;
///   - the victim's stand-in becomes their ragdoll at the moment of death;
///   - the killer's shots are redrawn from the view model.
/// Space skips it; either way the player then respawns. Added by CombatManager; local only, nothing is networked.
/// </summary>
public class Killcam : MonoBehaviour
{
    public const float Before = 4f;
    public const float After  = 1f;
    public const float Length = Before + After;

    private const float KeepSeconds = Length + 3f;

    private static Killcam _instance;
    public static Killcam Instance => _instance;

    /// <summary>True while a killcam is on screen.</summary>
    public static bool IsPlaying => _instance != null && _instance.playing;

    private struct Sample
    {
        public float      Time;
        public Vector3    Position;
        public Quaternion Rotation;
        public float      Pitch;
        public float      Eye;
        public Player.MovementState State;
        public float      MoveSpeed;
        public float      Look;      // Animator VerticalDirectionLooking (0–1)
        public bool       Grounded;
        public float      Arm;       // Right Arm layer weight
        public string     Gun;       // GunCatalog id in hand, or null
    }

    private sealed class Ghost
    {
        public int        Actor;
        public Transform  Root;
        public Animator   Animator;
        public Transform  Hand;      // The copied pistol (in the right hand)
        public GameObject Pistol;
        public readonly Dictionary<string, GameObject> Guns = new();
        public readonly HashSet<int> Params = new();
    }

    private readonly Dictionary<int, List<Sample>> tracks = new();
    private readonly Dictionary<int, List<(float time, Vector3 end)>> shots = new();

    // Playback
    private bool      playing;
    private int       killer;
    private float     deathTime;
    private float     clock;          // Recorded time being shown
    private Transform view;
    private Transform victimCorpse;
    private Transform corpseBefore;   // The local corpse from an earlier death (the new one arrives just after Play)
    private readonly List<Ghost>    ghosts = new();
    private readonly List<Renderer> hidden = new();

    private static readonly int H_MoveSpeed = Animator.StringToHash("MovementSpeed");
    private static readonly int H_VertLook  = Animator.StringToHash("VerticalDirectionLooking");
    private static readonly int H_OnGround  = Animator.StringToHash("onGround");
    private static readonly int H_Prone     = Animator.StringToHash("Prone");
    private static readonly int H_WallRun   = Animator.StringToHash("IsWallRunning");
    private static readonly int H_Climbing  = Animator.StringToHash("IsClimbing");
    private static readonly int H_Crouching = Animator.StringToHash("IsCrouching");
    private static readonly int H_Hanging   = Animator.StringToHash("IsHanging");

    private void Awake() => _instance = this;

    private void OnDestroy()
    {
        Stop();
        if (_instance == this) _instance = null;
    }

    // ─── Recording ────────────────────────────────────────────────────────────

    public void RecordShot(int actor, Vector3 end)
    {
        if (!shots.TryGetValue(actor, out var list)) shots[actor] = list = new List<(float, Vector3)>();
        list.Add((Time.time, end));
        while (list.Count > 0 && Time.time - list[0].time > KeepSeconds) list.RemoveAt(0);
    }

    private void Record()
    {
        float now = Time.time;
        foreach (var p in Player.All)
        {
            if (p == null) continue;
            if (!tracks.TryGetValue(p.ActorNumber, out var list)) tracks[p.ActorNumber] = list = new List<Sample>();

            var anim = p.Animation;
            // Pitch relative to the body (the camera holder's own angle), so tumbling bodies (Free Spin) replay right
            float pitch = p.IsLocal && PlayerCam.Local != null
                ? Signed(PlayerCam.Local.transform.localEulerAngles.x)
                : anim != null ? anim.LookPitch : 0f;
            var gun = p.Gun != null && !p.IsCaught ? p.Gun.Current : null;

            list.Add(new Sample
            {
                Time = now, Position = p.transform.position, Rotation = p.transform.rotation,
                Pitch = pitch, Eye = p.EyeHeight, State = p.State,
                MoveSpeed = anim != null ? anim.MoveSpeedParam : 0f,
                Look      = anim != null ? anim.VerticalLook : 0.5f,
                Grounded  = anim == null || anim.GroundedParam,
                Arm       = anim != null ? anim.ArmWeight : 0f,
                Gun       = gun?.Id,
            });
            while (list.Count > 0 && now - list[0].Time > KeepSeconds) list.RemoveAt(0);
        }
    }

    private static float Signed(float angle) => angle > 180f ? angle - 360f : angle;

    private bool TrySample(int actor, float time, out Sample sample)
    {
        sample = default;
        if (!tracks.TryGetValue(actor, out var list) || list.Count == 0) return false;
        if (time < list[0].Time) return false; // Not recorded that far back (just joined or spawned in)

        for (int i = 0; i < list.Count - 1; i++)
        {
            Sample a = list[i], b = list[i + 1];
            if (time > b.Time) continue;
            float t = Mathf.InverseLerp(a.Time, b.Time, time);
            sample = t < 0.5f ? a : b; // Discrete values from the nearer sample
            sample.Time      = time;
            sample.Position  = Vector3.Lerp(a.Position, b.Position, t);
            sample.Rotation  = Quaternion.Slerp(a.Rotation, b.Rotation, t);
            sample.Pitch     = Mathf.Lerp(a.Pitch, b.Pitch, t);
            sample.Eye       = Mathf.Lerp(a.Eye, b.Eye, t);
            sample.MoveSpeed = Mathf.Lerp(a.MoveSpeed, b.MoveSpeed, t);
            sample.Look      = Mathf.Lerp(a.Look, b.Look, t);
            sample.Arm       = Mathf.Lerp(a.Arm, b.Arm, t);
            return true;
        }
        sample = list[list.Count - 1];
        return true;
    }

    // ─── Playback ─────────────────────────────────────────────────────────────

    /// <summary>Local player was just killed by <paramref name="killerActor"/>: replays it. False if it can't.</summary>
    public bool Play(int killerActor, string title)
    {
        var local = Player.Local;
        if (playing || local == null || PlayerCam.Local == null) return false;
        if (!tracks.ContainsKey(killerActor) || Player.Find(killerActor) == null) return false;

        playing   = true;
        killer    = killerActor;
        deathTime = Time.time;
        clock     = deathTime - Before;

        view = new GameObject("Killcam View").transform;
        PlayerCam.Local.SetViewOverride(view);

        // Hide the live world's players (bodies, their guns); stand-ins play them instead
        foreach (var p in Player.All)
        {
            if (p == null) continue;
            foreach (var r in p.GetComponentsInChildren<Renderer>(true))
            {
                if (r.forceRenderingOff || IsUnderCamera(r.transform)) continue;
                r.forceRenderingOff = true;
                hidden.Add(r);
            }
            if (p.ActorNumber != killerActor)
            {
                var ghost = BuildGhost(p);
                if (ghost != null) ghosts.Add(ghost);
            }
        }

        corpseBefore = local.Corpse;
        victimCorpse = null;

        CombatHud.ShowKillcam(title);
        CombatHud.SetKillcamProgress(0f);
        Advance();
        return true;
    }

    private static bool IsUnderCamera(Transform t) =>
        PlayerCam.Local != null && PlayerCam.Local.Camera != null && t.IsChildOf(PlayerCam.Local.Camera.transform);

    /// <summary>Ends the killcam (finished, skipped or the scene is closing) and lets the local player respawn.</summary>
    public void Stop()
    {
        if (!playing) return;
        playing = false;

        foreach (var r in hidden) if (r != null) r.forceRenderingOff = false;
        hidden.Clear();
        foreach (var ghost in ghosts) if (ghost.Root != null) Destroy(ghost.Root.gameObject);
        ghosts.Clear();
        SetCorpseVisible(true);
        victimCorpse = null;

        var local = Player.Local;
        if (local != null) local.Gun.ShowKillcamGun(null);
        if (PlayerCam.Local != null) PlayerCam.Local.SetViewOverride(null);
        if (view != null) Destroy(view.gameObject);
        CombatHud.ShowKillcam(null);

        if (local != null) local.EndRespawnWait();
    }

    private void LateUpdate()
    {
        Record();
        if (!playing) return;

        if (Input.GetKeyDown(KeyCode.Space) && !PauseMenuManager.IsPaused) { Stop(); return; }

        float before = clock;
        clock += Time.deltaTime;
        if (clock >= deathTime + After || Player.Find(killer) == null) { Stop(); return; }

        Advance();
        ReplayShots(before, clock);
    }

    private void Advance()
    {
        var local = Player.Local;
        if (TrySample(killer, clock, out Sample k))
        {
            // The eye and look ride on the body's full rotation: backflips and barrel rolls replay as they happened
            view.SetPositionAndRotation(k.Position + k.Rotation * (Vector3.up * k.Eye), k.Rotation * Quaternion.Euler(k.Pitch, 0f, 0f));
            if (local != null) local.Gun.ShowKillcamGun(k.Gun);
        }

        foreach (var ghost in ghosts) PoseGhost(ghost);

        if (victimCorpse == null && local != null && local.Corpse != null && local.Corpse != corpseBefore)
            victimCorpse = local.Corpse.root;
        SetCorpseVisible(clock >= deathTime);
        CombatHud.SetKillcamProgress((clock - (deathTime - Before)) / Length);
    }

    private void ReplayShots(float from, float to)
    {
        var local = Player.Local;
        if (local == null || !shots.TryGetValue(killer, out var list)) return;
        foreach (var (time, end) in list)
        {
            if (time <= from || time > to) continue;
            Vector3 tip = local.Gun.Muzzle(true).position;
            GunFx.Tracer(tip, end);
            GunFx.Flash(tip);
            local.Gun.KillcamKick();
        }
    }

    private void SetCorpseVisible(bool visible)
    {
        if (victimCorpse == null) return;
        foreach (var r in victimCorpse.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = !visible;
    }

    // ─── Stand-ins ────────────────────────────────────────────────────────────

    // A copy of the player's body (like the ragdoll) with its own Animator, fed the recorded values each frame
    private Ghost BuildGhost(Player p)
    {
        var root = PlayerCorpse.CreateCopy(p, $"Killcam Stand-in ({p.DisplayName})", out Transform bones, keepGuns: true);
        if (root == null) return null;

        var ghost = new Ghost { Actor = p.ActorNumber, Root = root };
        var source = p.Animation != null ? p.Animation.Animator : null;
        if (source != null && source.runtimeAnimatorController != null)
        {
            var anim = root.gameObject.AddComponent<Animator>();
            anim.avatar = source.avatar;
            anim.runtimeAnimatorController = source.runtimeAnimatorController;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            anim.Rebind();
            foreach (var parameter in anim.parameters) ghost.Params.Add(parameter.nameHash);
            ghost.Animator = anim;
        }

        foreach (var t in bones.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != "Pistol") continue;
            ghost.Hand   = t;
            ghost.Pistol = t.gameObject;
            ghost.Pistol.SetActive(false);
            break;
        }
        return ghost;
    }

    private void PoseGhost(Ghost ghost)
    {
        var local = Player.Local;
        bool dead = local != null && ghost.Actor == local.ActorNumber && clock >= deathTime;
        Sample s = default;
        bool have = !dead && TrySample(ghost.Actor, clock, out s);
        if (ghost.Root.gameObject.activeSelf != have) ghost.Root.gameObject.SetActive(have);
        if (!have) return;

        ghost.Root.SetPositionAndRotation(s.Position, s.Rotation);

        var anim = ghost.Animator;
        if (anim != null)
        {
            SetFloat(ghost, H_MoveSpeed, s.MoveSpeed);
            SetFloat(ghost, H_VertLook,  s.Look);
            SetBool(ghost, H_OnGround,  s.Grounded);
            SetBool(ghost, H_Prone,     s.State == Player.MovementState.Prone);
            SetBool(ghost, H_WallRun,   s.State == Player.MovementState.WallRun);
            SetBool(ghost, H_Climbing,  s.State == Player.MovementState.Climb);
            SetBool(ghost, H_Crouching, s.State == Player.MovementState.Crouch);
            SetBool(ghost, H_Hanging,   s.State == Player.MovementState.Hang);
            int layers = anim.layerCount;
            if (layers > 1) anim.SetLayerWeight(1, s.Arm);
            if (layers > 2) anim.SetLayerWeight(2, s.State == Player.MovementState.WallRun ? 1f : 0f);
            if (layers > 3) anim.SetLayerWeight(3, s.State == Player.MovementState.Climb ? 1f : 0f);
            if (layers > 4) anim.SetLayerWeight(4, 1f);
        }

        // The gun they held then: the copied pistol, or that gun's model in the hand
        var gun = GunCatalog.Find(s.Gun);
        if (ghost.Pistol != null) ghost.Pistol.SetActive(gun != null && gun.Model == null);
        foreach (var pair in ghost.Guns) pair.Value.SetActive(gun != null && pair.Key == gun.Id);
        if (gun == null || gun.Model == null) return;

        if (!ghost.Guns.TryGetValue(gun.Id, out var model) || model == null)
        {
            var prefab = Resources.Load<GameObject>(gun.Model);
            if (prefab == null) return;
            model = Instantiate(prefab, ghost.Root);
            model.transform.localScale = Vector3.one * gun.WorldLength;
            ghost.Guns[gun.Id] = model;
        }
        Quaternion aim = s.Rotation * Quaternion.Euler(s.Pitch, 0f, 0f);
        Vector3 grip = ghost.Hand != null ? ghost.Hand.position : s.Position + s.Rotation * (Vector3.up * s.Eye * 0.75f);
        model.transform.SetPositionAndRotation(grip + aim * Vector3.forward * (gun.WorldLength * 0.15f), aim);
    }

    private static void SetFloat(Ghost ghost, int hash, float value)
    {
        if (ghost.Params.Contains(hash)) ghost.Animator.SetFloat(hash, value);
    }

    private static void SetBool(Ghost ghost, int hash, bool value)
    {
        if (ghost.Params.Contains(hash)) ghost.Animator.SetBool(hash, value);
    }
}
