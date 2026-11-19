using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// End-of-match highlight (Justin, Nov 18): when a match ends, everyone watches the best moment of it from a chase
/// camera behind the player who made it, with the moment itself in slow motion.
///
/// <b>What counts as best</b> is scored on the host as things happen, and depends on the mode through what each mode
/// does: kills in gun modes (a multi-kill within a few seconds, a headshot, a knock-off, a long shot: <see cref="OfferKill"/>),
/// tags in chase modes (a chain of tags, the match's final tag, the President: <see cref="OfferTag"/>, plus the mode's
/// <see cref="GameMode.TagHighlightBonus"/>), a disc steal in Keep Away, a plant or defuse in Search &amp; Destroy, and
/// the winning finish in a Race. Modes add their own with <see cref="Offer"/>.
///
/// <b>How everyone sees the same thing</b> without sending the replay: every client keeps the last
/// <see cref="KeepSeconds"/> of every player as it saw them (position, facing, animation values, gun in hand, shots).
/// Each time the host scores a moment it tells everyone (an id, who, when, its score); each client then cuts that
/// window out of its own recording and keeps the best few. When the match ends the host names the best moment's id
/// in the room (<see cref="RoomKeys.Highlight"/>) and lengthens MatchEnd by <see cref="PlaySeconds"/>
/// (<see cref="GameModeManager.SetPhase"/>); every client then replays its own copy with stand-ins (copies of the
/// players' bodies, like the killcam's), the live players hidden. A client that joined after the moment has no copy
/// and sees the normal end. Nothing in practice modes.
/// Added next to the GameModeManager; its RPCs share that PhotonView (prefixed HL).
/// </summary>
public class Highlights : MonoBehaviourPun
{
    public const float Before = 5f, After = 2f;
    private const float KeepSeconds = Before + After + 4f;
    private const float SampleInterval = 1f / 30f;
    private const int   KeepClips = 8;
    // Playback speed round the moment: slow within 0.6 s of it, part speed within 1.2 s, real time otherwise
    private const float SlowNear = 0.6f, SlowRate = 0.35f, HalfNear = 1.2f, HalfRate = 0.7f;

    /// <summary>
    /// Real seconds a highlight takes to play (the slow-motion stretch included). MatchEnd is lengthened by this
    /// (<see cref="GameModeManager.SetPhase"/>) and the highlight starts this long before MatchEnd's clock runs out,
    /// so the order is: the finish, a few seconds to wander, the highlight, straight to the Scoreboard.
    /// </summary>
    public const float PlaySeconds = (SlowNear * 2f) / SlowRate + ((HalfNear - SlowNear) * 2f) / HalfRate
                                   + (Before + After - HalfNear * 2f) + 0.3f;

    public static Highlights Instance { get; private set; }

    // ─── Recording (every client) ─────────────────────────────────────────────

    private struct Sample
    {
        public float Time;
        public Vector3 Position;
        public Quaternion Rotation;
        public float Pitch;
        public Player.MovementState State;
        public float MoveSpeed, Look, Arm;
        public bool Grounded, Visible;
        public string Gun;
        public int Abilities;   // Abilities used this match so far (for the score: abilities in the moment)
    }

    private readonly Dictionary<int, List<Sample>> tracks = new();
    private readonly Dictionary<int, List<(float time, Vector3 end)>> shots = new();
    private float lastSample = -1f;

    private sealed class Clip
    {
        public int Id, Focus, Victim;
        public float Score, EventTime, StartTime;
        public string Label;
        public readonly Dictionary<int, List<Sample>> Tracks = new();
        public readonly Dictionary<int, List<(float time, Vector3 end)>> Shots = new();
    }

    private readonly List<Clip> clips = new();

    private void Awake()
    {
        Instance = this;
        photonView.RefreshRpcMonoBehaviourCache();   // Added at runtime next to the manager: its RPCs must be found
    }

    private void OnDestroy()
    {
        Stop();
        if (Instance == this) Instance = null;
    }

    /// <summary>A shot by <paramref name="actor"/> ending at <paramref name="end"/> (tracers in the replay).</summary>
    public static void RecordShot(int actor, Vector3 end)
    {
        if (Instance == null) return;
        if (!Instance.shots.TryGetValue(actor, out var list)) Instance.shots[actor] = list = new List<(float, Vector3)>();
        list.Add((Time.time, end));
        while (list.Count > 0 && Time.time - list[0].time > KeepSeconds) list.RemoveAt(0);
    }

    private void Record()
    {
        float now = Time.time;
        if (now - lastSample < SampleInterval) return;
        lastSample = now;
        foreach (var p in Player.All)
        {
            if (p == null) continue;
            if (!tracks.TryGetValue(p.ActorNumber, out var list)) tracks[p.ActorNumber] = list = new List<Sample>();
            var anim = p.Animation;
            var gun = p.Gun != null && !p.IsCaught ? p.Gun.Current : null;
            list.Add(new Sample
            {
                Time = now, Position = p.transform.position, Rotation = p.transform.rotation,
                Pitch = anim != null ? anim.LookPitch : 0f, State = p.State,
                MoveSpeed = anim != null ? anim.MoveSpeedParam : 0f,
                Look = anim != null ? anim.VerticalLook : 0.5f,
                Arm = anim != null ? anim.ArmWeight : 0f,
                Grounded = anim == null || anim.GroundedParam,
                Visible = !p.IsCaught && !p.IsDownAfterTag && !(GameModeManager.Instance != null && GameModeManager.Instance.Mode != null
                          && GameModeManager.Instance.Mode.InHunterRoom(p.Role, GameModeManager.Instance.Phase)),
                Gun = gun?.Id,
                Abilities = (int)NetProps.Of(p.photonView.Owner, PlayerKeys.StatAbilitiesUsed, 0f),
            });
            while (list.Count > 0 && now - list[0].Time > KeepSeconds) list.RemoveAt(0);
        }
    }

    // ─── Offers (host) ────────────────────────────────────────────────────────

    private int nextId;
    // Host: every moment's score, its base until its clip is cut (then with the abilities and Free Spin bonus)
    private readonly Dictionary<int, float> scores = new();
    private bool published;

    /// <summary>Host: anything offered this match (MatchEnd then makes room for a highlight).</summary>
    public bool HasMoments => scores.Count > 0;

    private int BestId()
    {
        int best = 0;
        float top = float.MinValue;
        foreach (var pair in scores) if (pair.Value > top) { top = pair.Value; best = pair.Key; }
        return best;
    }

    private static bool Recording
    {
        get
        {
            var manager = GameModeManager.Instance;
            return Instance != null && PhotonNetwork.IsMasterClient && manager != null && manager.Mode != null
                && manager.Mode.GivesXp && manager.Phase == MatchPhase.Active;
        }
    }

    /// <summary>
    /// Host: a moment worth replaying, centred on <paramref name="focus"/> (the player the camera follows), with
    /// <paramref name="victim"/> (or −1) at the moment, scored against the match's others.
    /// </summary>
    public static void Offer(int focus, int victim, float score, string label)
    {
        if (!Recording) return;
        var self = Instance;
        int id = ++self.nextId;
        self.scores[id] = score;
        self.photonView.RPC(nameof(RPC_HLMoment), RpcTarget.All, id, focus, victim, score, label ?? "", PhotonNetwork.Time);
    }

    // Kills: a run of them inside a few seconds is the big one
    private readonly Dictionary<int, List<float>> killTimes = new();
    private const float MultiKillWindow = 4f;

    /// <summary>Host: <paramref name="killer"/> took out <paramref name="victim"/> (gun modes, CombatManager).</summary>
    public static void OfferKill(int killer, int victim, bool headshot, bool knockOff)
    {
        if (!Recording) return;
        var self = Instance;
        if (!self.killTimes.TryGetValue(killer, out var times)) self.killTimes[killer] = times = new List<float>();
        times.Add(Time.time);
        times.RemoveAll(t => Time.time - t > MultiKillWindow);
        int run = times.Count;

        var a = Player.Find(killer);
        var b = Player.Find(victim);
        float distance = a != null && b != null ? Vector3.Distance(a.transform.position, b.transform.position) : 0f;
        float score = 10f + (run - 1) * 14f + (headshot ? 5f : 0f) + (knockOff ? 7f : 0f) + Mathf.Min(distance, 80f) * 0.12f;
        string who = NameOf(killer);
        string what = run >= 4 ? "Rampage" : run == 3 ? "Triple kill" : run == 2 ? "Double kill"
                    : knockOff ? "Knock-off" : headshot ? "Headshot" : distance > 45f ? "Long shot" : "Takedown";
        Offer(killer, victim, score, $"{what} by {who}");
    }

    private readonly Dictionary<int, List<float>> tagTimes = new();
    private const float TagChainWindow = 10f;

    /// <summary>Host: <paramref name="tagger"/> tagged <paramref name="target"/> (chase modes, GameModeManager).</summary>
    public static void OfferTag(Photon.Realtime.Player tagger, Photon.Realtime.Player target)
    {
        if (!Recording || tagger == null || target == null) return;
        var self = Instance;
        int actor = tagger.ActorNumber;
        if (!self.tagTimes.TryGetValue(actor, out var times)) self.tagTimes[actor] = times = new List<float>();
        times.Add(Time.time);
        times.RemoveAll(t => Time.time - t > TagChainWindow);
        int chain = times.Count;

        var mode = GameModeManager.Instance.Mode;
        float bonus = mode.TagHighlightBonus(target, out string special);
        float score = 12f + (chain - 1) * 10f + bonus;
        string label = !string.IsNullOrEmpty(special) ? special.Replace("{tagger}", NameOf(actor)).Replace("{target}", NameOf(target.ActorNumber))
                     : chain >= 2 ? $"{NameOf(actor)} tags {chain} in a row"
                     : $"{NameOf(actor)} tags {NameOf(target.ActorNumber)}";
        Offer(actor, target.ActorNumber, score, label);
    }

    private static string NameOf(int actor)
    {
        var p = PhotonNetwork.CurrentRoom?.GetPlayer(actor);
        return p != null && !string.IsNullOrEmpty(p.NickName) ? p.NickName : $"Player {actor}";
    }

    // ─── Capturing a moment (every client) ────────────────────────────────────

    [PunRPC]
    private void RPC_HLMoment(int id, int focus, int victim, float score, string label, double atNetworkTime)
    {
        // The moment in this client's own clock (the recording is in Time.time)
        float at = Time.time - (float)(PhotonNetwork.Time - atNetworkTime);
        StartCoroutine(Capture(new Clip { Id = id, Focus = focus, Victim = victim, Score = score, Label = label, EventTime = at }));
    }

    private IEnumerator Capture(Clip clip)
    {
        // Wait until what follows the moment is recorded too
        while (Time.time < clip.EventTime + After) yield return null;
        clip.StartTime = clip.EventTime - Before;
        float end = clip.EventTime + After;
        foreach (var pair in tracks)
        {
            var cut = pair.Value.FindAll(s => s.Time >= clip.StartTime - 0.1f && s.Time <= end + 0.1f);
            if (cut.Count > 1) clip.Tracks[pair.Key] = cut;
        }
        foreach (var pair in shots)
            clip.Shots[pair.Key] = pair.Value.FindAll(s => s.time >= clip.StartTime && s.time <= end);
        if (!clip.Tracks.ContainsKey(clip.Focus)) yield break;   // Never saw the player it's about

        clip.Score += MomentBonus(clip.Tracks[clip.Focus]);
        if (PhotonNetwork.IsMasterClient && scores.ContainsKey(clip.Id)) scores[clip.Id] = clip.Score;

        clips.Add(clip);
        clips.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (clips.Count > KeepClips) clips.RemoveRange(KeepClips, clips.Count - KeepClips);
    }

    /// <summary>
    /// What the player at the centre did round the moment, beyond the moment itself: abilities used in it (up to
    /// +16), and Free Spin whipping the view about (a little, up to +6: fast turns while tipped over count, sitting
    /// upside down doesn't).
    /// </summary>
    private static float MomentBonus(List<Sample> focus)
    {
        if (focus.Count < 2) return 0f;
        int used = focus[focus.Count - 1].Abilities - focus[0].Abilities;
        float abilities = Mathf.Min(Mathf.Max(0, used) * 4f, 16f);

        float whip = 0f;
        for (int i = 1; i < focus.Count; i++)
        {
            Sample a = focus[i - 1], b = focus[i];
            float dt = b.Time - a.Time;
            if (dt <= 0f) continue;
            bool tipped = Vector3.Angle(a.Rotation * Vector3.up, Vector3.up) > 35f || Vector3.Angle(b.Rotation * Vector3.up, Vector3.up) > 35f;
            float turn = Quaternion.Angle(a.Rotation, b.Rotation);
            if (tipped && turn / dt > 200f) whip += turn;
        }
        return abilities + Mathf.Min(whip / 360f * 1.5f, 6f);
    }

    // ─── Playback (every client, at the end of the match) ─────────────────────

    private sealed class Ghost
    {
        public int Actor;
        public Transform Root;
        public Animator Animator;
        public Transform Hand;
        public GameObject Pistol;
        public readonly Dictionary<string, GameObject> Guns = new();
        public readonly HashSet<int> Params = new();
    }

    private Clip playing;
    private float clock;
    private bool played, burst;
    private Transform view;
    private Vector3 camPos;
    private bool camPlaced;
    private readonly List<Ghost> ghosts = new();
    private readonly List<Renderer> hidden = new();

    /// <summary>True while a highlight is on screen.</summary>
    public static bool IsPlaying => Instance != null && Instance.playing != null;

    private void LateUpdate()
    {
        Record();
        var manager = GameModeManager.Instance;
        if (manager == null) return;

        if (manager.Phase != MatchPhase.MatchEnd) { Stop(); return; }   // On to the Scoreboard
        float remaining = manager.Clock != null ? manager.Clock.Remaining : 0f;

        // Host, a second before it's due: names the best moment (by then its clip is cut, with its bonus)
        if (PhotonNetwork.IsMasterClient && !published && HasMoments && remaining <= PlaySeconds + 1f)
        {
            published = true;
            NetProps.SetRoom(RoomKeys.Highlight, BestId());
        }

        // Everyone: after the wander, the highlight runs to the end of MatchEnd (then straight to the Scoreboard)
        if (playing == null && !played && remaining > 0f && remaining <= PlaySeconds)
        {
            int id = NetProps.Room(RoomKeys.Highlight, 0);
            if (id > 0 || remaining < PlaySeconds - 1f)
            {
                played = true;
                var clip = clips.Find(c => c.Id == id);
                if (clip != null) Play(clip);
            }
        }
        if (playing != null) Advance();
    }

    private void Play(Clip clip)
    {
        var cam = PlayerCam.Local;
        if (cam == null) return;
        if (Killcam.IsPlaying) Killcam.Instance.Stop();

        playing = clip;
        clock = clip.StartTime;
        burst = false;
        camPlaced = false;
        view = new GameObject("Highlight View").transform;
        cam.SetViewOverride(view);

        foreach (var p in Player.All)
        {
            if (p == null) continue;
            foreach (var r in p.GetComponentsInChildren<Renderer>(true))
            {
                if (r.forceRenderingOff) continue;
                r.forceRenderingOff = true;
                hidden.Add(r);
            }
            if (!clip.Tracks.ContainsKey(p.ActorNumber)) continue;
            var ghost = BuildGhost(p);
            if (ghost != null) ghosts.Add(ghost);
        }
        PlayerCorpse.ClearAll();   // The live ragdolls would lie about in the replay

        if (HUDManager.Instance != null) HUDManager.Instance.ShowBanner($"HIGHLIGHT\n<size=55%>{clip.Label}</size>", 3.5f);
        Sfx.Here(SfxKind.Fanfare, 0.5f);
    }

    private void Advance()
    {
        var clip = playing;
        // Real time (the match-end slow motion may still be easing out), slowed round the moment itself
        float near = Mathf.Abs(clock - clip.EventTime);
        float rate = near < SlowNear ? SlowRate : near < HalfNear ? HalfRate : 1f;
        float before = clock;
        clock += Time.unscaledDeltaTime * rate;
        if (clock >= clip.EventTime + After) { Stop(); return; }

        foreach (var ghost in ghosts) PoseGhost(ghost, clip);
        ReplayShots(clip, before, clock);

        // The moment: a burst where the victim was
        if (!burst && clock >= clip.EventTime)
        {
            burst = true;
            if (clip.Victim >= 0 && TrySample(clip, clip.Victim, clip.EventTime - 0.05f, out Sample v))
                Fx.Tagged(v.Position, GameModeManager.Instance != null && GameModeManager.Instance.Mode != null && GameModeManager.Instance.Mode.UsesGuns);
        }

        // The chase camera: behind and above the focus, looking at them, smoothed
        if (TrySample(clip, clip.Focus, clock, out Sample f))
        {
            Vector3 flat = Vector3.ProjectOnPlane(f.Rotation * Vector3.forward, Vector3.up);
            if (flat.sqrMagnitude < 0.01f) flat = Vector3.forward;
            flat.Normalize();
            Vector3 target = f.Position - flat * 6.5f + Vector3.up * 3.4f;
            // Pull in rather than sit behind a wall
            Vector3 look = f.Position + Vector3.up * 1.7f;
            if (Physics.Linecast(look, target, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<Player>() == null)
                target = hit.point + (look - target).normalized * 0.4f;
            camPos = camPlaced ? Vector3.Lerp(camPos, target, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime)) : target;
            camPlaced = true;
            view.SetPositionAndRotation(camPos, Quaternion.LookRotation(look - camPos));
        }
    }

    private void Stop()
    {
        if (playing == null) return;
        playing = null;
        foreach (var r in hidden) if (r != null) r.forceRenderingOff = false;
        hidden.Clear();
        foreach (var ghost in ghosts) if (ghost.Root != null) Destroy(ghost.Root.gameObject);
        ghosts.Clear();
        if (PlayerCam.Local != null) PlayerCam.Local.SetViewOverride(null);
        if (view != null) Destroy(view.gameObject);
    }

    private void ReplayShots(Clip clip, float from, float to)
    {
        foreach (var pair in clip.Shots)
        {
            var ghost = ghosts.Find(g => g.Actor == pair.Key);
            if (ghost == null) continue;
            foreach (var (time, end) in pair.Value)
            {
                if (time <= from || time > to) continue;
                Vector3 tip = ghost.Hand != null ? ghost.Hand.position : ghost.Root.position + Vector3.up * 1.6f;
                GunFx.Tracer(tip, end);
                GunFx.Flash(tip);
            }
        }
    }

    private static bool TrySample(Clip clip, int actor, float time, out Sample sample)
    {
        sample = default;
        if (!clip.Tracks.TryGetValue(actor, out var list) || list.Count == 0) return false;
        if (time < list[0].Time) { sample = list[0]; return true; }
        for (int i = 0; i < list.Count - 1; i++)
        {
            Sample a = list[i], b = list[i + 1];
            if (time > b.Time) continue;
            float t = Mathf.InverseLerp(a.Time, b.Time, time);
            sample = t < 0.5f ? a : b;
            sample.Position  = Vector3.Lerp(a.Position, b.Position, t);
            sample.Rotation  = Quaternion.Slerp(a.Rotation, b.Rotation, t);
            sample.Pitch     = Mathf.Lerp(a.Pitch, b.Pitch, t);
            sample.MoveSpeed = Mathf.Lerp(a.MoveSpeed, b.MoveSpeed, t);
            sample.Look      = Mathf.Lerp(a.Look, b.Look, t);
            sample.Arm       = Mathf.Lerp(a.Arm, b.Arm, t);
            return true;
        }
        sample = list[list.Count - 1];
        return true;
    }

    // ─── Stand-ins (copies of the bodies with their own Animators, as the killcam builds them) ───

    private static readonly int H_MoveSpeed = Animator.StringToHash("MovementSpeed");
    private static readonly int H_VertLook  = Animator.StringToHash("VerticalDirectionLooking");
    private static readonly int H_OnGround  = Animator.StringToHash("onGround");
    private static readonly int H_Prone     = Animator.StringToHash("Prone");
    private static readonly int H_WallRun   = Animator.StringToHash("IsWallRunning");
    private static readonly int H_Climbing  = Animator.StringToHash("IsClimbing");
    private static readonly int H_Crouching = Animator.StringToHash("IsCrouching");
    private static readonly int H_Hanging   = Animator.StringToHash("IsHanging");

    private Ghost BuildGhost(Player p)
    {
        var root = PlayerCorpse.CreateCopy(p, $"Highlight Stand-in ({p.DisplayName})", out Transform bones, keepGuns: true);
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
            anim.updateMode = AnimatorUpdateMode.UnscaledTime;
            anim.Rebind();
            foreach (var parameter in anim.parameters) ghost.Params.Add(parameter.nameHash);
            ghost.Animator = anim;
        }
        foreach (var t in bones.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != "Pistol") continue;
            ghost.Hand = t;
            ghost.Pistol = t.gameObject;
            ghost.Pistol.SetActive(false);
            break;
        }
        return ghost;
    }

    private void PoseGhost(Ghost ghost, Clip clip)
    {
        bool have = TrySample(clip, ghost.Actor, clock, out Sample s) && s.Visible;
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
            void Weight(string layer, float w) { int i = anim.GetLayerIndex(layer); if (i >= 0) anim.SetLayerWeight(i, w); }
            Weight("RightArm", s.Arm);
            Weight("Wall Run Layer", s.State == Player.MovementState.WallRun ? 1f : 0f);
            Weight("Climb Layer", s.State == Player.MovementState.Climb ? 1f : 0f);
            Weight("HeadLayer", 1f);
            Weight("Crouch Layer", s.State == Player.MovementState.Crouch ? 1f : 0f);
        }

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
        Vector3 grip = ghost.Hand != null ? ghost.Hand.position : s.Position + Vector3.up * 1.5f;
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
