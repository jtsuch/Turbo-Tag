using System;
using UnityEngine;

/// <summary>
/// A donut pickup: floats a little off the ground, slowly spins with a slight tilt and glints. Touching it gives
/// a random effect from <see cref="Pool"/> (good or bad). Purely local visuals: <see cref="DonutManager"/> places
/// donuts on every screen and decides who got one.
/// The model is Resources/Pickups/Donut (Justin's donut, colliders removed, about 0.8 m across).
/// </summary>
public class Donut : MonoBehaviour
{
    /// <summary>Whether a donut effect is a boon, a curse or chaos (Lucky Streak and Golden Donut only roll boons).</summary>
    public enum Kind { Good, Bad, Chaos }

    /// <summary>Where an effect can roll.</summary>
    public enum Where
    {
        Any,
        Guns,     // The player holds a gun in this mode (Free for All, Team Deathmatch, the Hunter)
        Escape,   // Tag, Hunter, Protect the President, and not the hunter
        Chaser,   // The hunter in those modes, or anyone in Keep Away
        KeepAway, // Keep Away only
    }

    /// <summary>One thing a donut can do.</summary>
    public readonly struct Entry
    {
        public readonly Type  Effect;
        public readonly float Seconds;
        public readonly float Weight;   // Relative chance
        public readonly Kind  Type;
        public readonly Where Mode;
        public readonly bool  Everyone; // Every player gets it, not just the one who ate the donut

        public Entry(Type effect, float seconds, float weight, Kind kind, Where where = Where.Any, bool everyone = false)
        {
            Effect = effect; Seconds = seconds; Weight = weight; Type = kind; Mode = where; Everyone = everyone;
        }
    }

    /// <summary>
    /// What a donut can do. Add an effect: a PlayerEffect subclass and a line here.
    /// Balance: a donut should be worth the detour, so boons are likelier than curses (most boons 1, curses 0.8,
    /// chaos 0.6), curses that take control away are short, and the broken boons (one shot, aimbot, invulnerable,
    /// time warp, freeze) are brief and rarer. Mode-specific boons only roll where they make sense.
    /// </summary>
    public static readonly Entry[] Pool =
    {
        // Boons, anywhere
        new(typeof(DoubleJumpEffect),      20f, 1f,   Kind.Good),
        new(typeof(NoCooldownEffect),      10f, 0.6f, Kind.Good),
        new(typeof(AirControlEffect),      15f, 1f,   Kind.Good),
        new(typeof(XRayEffect),             6f, 0.8f, Kind.Good),
        new(typeof(SpeedEffect),           15f, 1f,   Kind.Good),
        new(typeof(MoonBootsEffect),       15f, 1f,   Kind.Good),
        new(typeof(GhostEffect),            8f, 1f,   Kind.Good),
        new(typeof(RocketJumpEffect),      15f, 1f,   Kind.Good),
        new(typeof(BubbleEffect),           8f, 0.8f, Kind.Good),
        new(typeof(RadarEffect),           15f, 1f,   Kind.Good),
        new(typeof(RefillEffect),           3f, 0.7f, Kind.Good),
        new(typeof(JetpackEffect),          6f, 0.9f, Kind.Good),
        new(typeof(TeleportGunEffect),     30f, 0.8f, Kind.Good),
        new(typeof(GiantSlayerEffect),     10f, 0.8f, Kind.Good),
        new(typeof(TimeWarpEffect),         5f, 0.4f, Kind.Good),
        new(typeof(OverdriveEffect),       15f, 0.9f, Kind.Good),
        new(typeof(LuckyStreakEffect),      3f, 0.6f, Kind.Good),
        // (Golden Donut isn't rolled: it comes from the rare golden donuts on the map, DonutManager.GoldenChance)
        // Boons with a gun
        new(typeof(BottomlessMagEffect),   15f, 1f,   Kind.Good, Where.Guns),
        new(typeof(VampireEffect),         20f, 1f,   Kind.Good, Where.Guns),
        new(typeof(AimbotEffect),           5f, 0.5f, Kind.Good, Where.Guns),
        new(typeof(BigBulletsEffect),      12f, 1f,   Kind.Good, Where.Guns),
        new(typeof(ExplosiveRoundsEffect), 10f, 0.8f, Kind.Good, Where.Guns),
        new(typeof(RicochetEffect),        15f, 0.9f, Kind.Good, Where.Guns),
        new(typeof(WallbangEffect),        10f, 0.7f, Kind.Good, Where.Guns),
        new(typeof(MinigunEffect),          8f, 0.8f, Kind.Good, Where.Guns),
        new(typeof(OneShotEffect),          6f, 0.4f, Kind.Good, Where.Guns),
        new(typeof(OverhealEffect),         3f, 1f,   Kind.Good, Where.Guns),
        new(typeof(InvulnerableEffect),     6f, 0.5f, Kind.Good, Where.Guns),
        new(typeof(SecondLifeEffect),      90f, 0.8f, Kind.Good, Where.Guns),
        // Boons for getting away
        new(typeof(PhantomEffect),          6f, 0.8f, Kind.Good, Where.Escape),
        new(typeof(JammerEffect),          20f, 1f,   Kind.Good, Where.Escape),
        new(typeof(DecoysEffect),           3f, 1f,   Kind.Good, Where.Escape),
        new(typeof(SmokeBombEffect),        3f, 1f,   Kind.Good, Where.Escape),
        new(typeof(SuperSpeedEffect),       5f, 1f,   Kind.Good, Where.Escape),
        new(typeof(RepelFieldEffect),      10f, 0.8f, Kind.Good, Where.Escape),
        new(typeof(FreezeOthersEffect),     3f, 0.4f, Kind.Good, Where.Escape),
        // Boons for chasing
        new(typeof(BloodhoundEffect),      10f, 1f,   Kind.Good, Where.Chaser),
        new(typeof(LongArmsEffect),        10f, 1f,   Kind.Good, Where.Chaser),
        new(typeof(MagnetEffect),           8f, 0.8f, Kind.Good, Where.Chaser),
        // Boons for Keep Away
        new(typeof(ButterHandsEffect),     15f, 1f,   Kind.Good, Where.KeepAway),
        new(typeof(HomingThrowEffect),     15f, 1f,   Kind.Good, Where.KeepAway),
        new(typeof(RocketDiscEffect),      10f, 1f,   Kind.Good, Where.KeepAway),
        new(typeof(PointFrenzyEffect),     10f, 0.8f, Kind.Good, Where.KeepAway),
        // Curses
        new(typeof(ShrinkFOVEffect),       15f, 0.8f, Kind.Bad),
        new(typeof(KeybindSwitchEffect),   12f, 0.8f, Kind.Bad),
        new(typeof(DropFrameRateEffect),    8f, 0.8f, Kind.Bad),
        new(typeof(ButterfingersEffect),   10f, 0.8f, Kind.Bad),
        new(typeof(GiantEffect),           15f, 0.8f, Kind.Bad),
        new(typeof(BeaconEffect),          10f, 0.8f, Kind.Bad),
        new(typeof(UpsideDownEffect),       8f, 0.8f, Kind.Bad),
        new(typeof(DrunkEffect),           12f, 0.8f, Kind.Bad),
        new(typeof(StickyFeetEffect),       8f, 0.8f, Kind.Bad),
        new(typeof(SpinCycleEffect),       12f, 0.8f, Kind.Bad),
        // Chaos
        new(typeof(LowGravityPartyEffect), 15f, 0.6f, Kind.Chaos, everyone: true),
        new(typeof(SwapEffect),             3f, 0.6f, Kind.Chaos),
        new(typeof(FishRainEffect),         5f, 0.6f, Kind.Chaos),
    };

    /// <summary>Lucky Streak: this many more rolls on this screen are boons only.</summary>
    public static int LuckyRolls { get; set; }

    /// <summary>Whether <paramref name="entry"/> can roll for the local player right now.</summary>
    public static bool Fits(Entry entry)
    {
        var manager = GameModeManager.Instance;
        var mode = manager != null ? manager.Mode : null;
        var local = Player.Local;
        var role = local != null ? local.Role : PlayerRole.None;
        bool chase = mode != null && mode.UsesRoles && mode is not TeamMode;
        return entry.Mode switch
        {
            Where.Guns     => mode != null && mode.UsesGuns && mode.FixedGun(role) != "",
            Where.Escape   => chase && role != PlayerRole.Hunter && role != PlayerRole.Ghost,
            Where.Chaser   => (chase && role == PlayerRole.Hunter) || mode is KeepAwayMode,
            Where.KeepAway => mode is KeepAwayMode,
            _              => true,
        };
    }

    public const float HoverHeight = 1f;   // Above the ground it sits over
    public const float TouchRadius = 0.9f; // From the donut to the nearest point of a body

    private static GameObject model;
    private static Material   beamMaterial;
    private static Material BeamMaterial => beamMaterial != null ? beamMaterial : beamMaterial = new Material(Shader.Find("Sprites/Default"));
    private Transform visual;
    private Vector3   basePosition;
    private float     phase, nextGlint;

    public int Id { get; private set; }

    /// <summary>A rare golden donut: always the Golden Donut (its boons), announced to everyone.</summary>
    public bool Golden { get; private set; }

    private static readonly Color Gold = new(1f, 0.78f, 0.2f);
    private static Material goldMaterial;

    // Shiny gold, glowing a little
    private static Material GoldMaterial
    {
        get
        {
            if (goldMaterial != null) return goldMaterial;
            goldMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Golden Donut" };
            goldMaterial.SetColor("_BaseColor", Gold);
            goldMaterial.SetFloat("_Metallic", 0.9f);
            goldMaterial.SetFloat("_Smoothness", 0.8f);
            goldMaterial.EnableKeyword("_EMISSION");
            goldMaterial.SetColor("_EmissionColor", new Color(1f, 0.55f, 0.1f) * 0.6f);
            return goldMaterial;
        }
    }

    /// <summary>Makes a donut at <paramref name="ground"/> (it hovers above it).</summary>
    public static Donut Create(int id, Vector3 ground, bool golden = false)
    {
        var root = new GameObject(golden ? $"Golden Donut {id}" : $"Donut {id}");
        var donut = root.AddComponent<Donut>();
        donut.Id = id;
        donut.Golden = golden;
        donut.basePosition = ground + Vector3.up * HoverHeight;
        donut.phase = UnityEngine.Random.value * 10f;
        root.transform.position = donut.basePosition;

        if (model == null) model = Resources.Load<GameObject>("Pickups/Donut");
        if (model != null)
        {
            donut.visual = Instantiate(model, root.transform).transform;
            donut.visual.localPosition = Vector3.zero;
        }
        else
        {
            // No model: a stand-in torus-ish ring of spheres would be overkill, a flattened sphere does
            var stand = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(stand.GetComponent<Collider>());
            stand.transform.SetParent(root.transform, false);
            stand.transform.localScale = new Vector3(0.7f, 0.25f, 0.7f);
            donut.visual = stand.transform;
        }

        // A soft warm glow so it stands out in dark corners
        var light = new GameObject("Glow").AddComponent<Light>();
        light.transform.SetParent(root.transform, false);
        light.type = LightType.Point;
        light.range = 3f;
        light.intensity = 1.2f;
        light.color = golden ? Gold : new Color(1f, 0.6f, 0.8f);
        if (golden)
        {
            light.range = 6f;
            light.intensity = 2.5f;
            if (donut.visual != null)
            {
                donut.visual.localScale *= 1.35f;
                foreach (var r in donut.visual.GetComponentsInChildren<Renderer>())
                    r.sharedMaterials = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Repeat(GoldMaterial, r.sharedMaterials.Length));
            }
        }
        light.shadows = LightShadows.None;

        // A soft pink beam into the sky, so donuts can be spotted from across the map
        var beam = new GameObject("Beam").AddComponent<LineRenderer>();
        beam.transform.SetParent(root.transform, false);
        beam.useWorldSpace = false;
        beam.positionCount = 2;
        beam.SetPosition(0, Vector3.down * HoverHeight);
        beam.SetPosition(1, Vector3.up * (golden ? 18f : 9f));
        beam.widthCurve = new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(1f, 0.05f));
        beam.sharedMaterial = BeamMaterial;
        var fade = new Gradient();
        fade.SetKeys(
            golden ? new[] { new GradientColorKey(Gold, 0f), new GradientColorKey(new Color(1f, 0.95f, 0.7f), 1f) }
                   : new[] { new GradientColorKey(new Color(1f, 0.55f, 0.85f), 0f), new GradientColorKey(new Color(1f, 0.8f, 0.95f), 1f) },
            new[] { new GradientAlphaKey(0.45f, 0f), new GradientAlphaKey(0f, 1f) });
        beam.colorGradient = fade;
        beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        beam.receiveShadows = false;

        Vfx.Ring(ground, golden ? Gold : new Color(1f, 0.6f, 0.85f, 0.8f), 18, 3f, 0.3f, 0.5f);
        return donut;
    }

    private Transform carrier;

    /// <summary>Hunter mode: a ghost carries it (smaller, no beam), floating over their head until handed over.</summary>
    public void CarryBy(Transform ghost)
    {
        carrier = ghost;
        transform.localScale *= 0.7f;
        foreach (var beam in GetComponentsInChildren<LineRenderer>()) beam.enabled = false;
    }

    private void Update()
    {
        float t = Time.time + phase;
        Vector3 anchor = carrier != null ? carrier.position + Vector3.up * 3.3f : basePosition;
        transform.position = anchor + Vector3.up * (Mathf.Sin(t * 2f) * 0.15f);
        if (visual != null) visual.localRotation = Quaternion.Euler(18f * Mathf.Sin(t * 0.7f), t * 70f, 12f);

        if (Time.time >= nextGlint)
        {
            nextGlint = Time.time + UnityEngine.Random.Range(0.3f, 0.7f);
            Color sprinkle = Color.HSVToRGB(UnityEngine.Random.value, 0.6f, 1f);
            Vfx.Burst(transform.position + UnityEngine.Random.insideUnitSphere * 0.4f, sprinkle, 2, 0.6f, 0.1f, 0.6f, direction: Vector3.up, spread: 0.8f);
        }
    }

    /// <summary>Someone took it (or it timed out): a pop of sprinkles, then gone.</summary>
    public void Pop(bool eaten)
    {
        if (eaten)
        {
            Sfx.At(SfxKind.Pop, transform.position, 1f, 35f, 1.2f);
            for (int i = 0; i < 4; i++)
                Vfx.Burst(transform.position, Color.HSVToRGB(i / 4f, 0.7f, 1f), 8, 5f, 0.15f, 0.6f, gravity: true);
        }
        else
        {
            Vfx.Burst(transform.position, new Color(1f, 0.6f, 0.85f, 0.6f), 10, 2f, 0.25f, 0.5f);
        }
        Destroy(gameObject);
    }

    /// <summary>
    /// A random entry from <see cref="Pool"/> that fits this mode and role, by weight. <paramref name="boonsOnly"/>
    /// (or a Lucky Streak) limits it to boons; <paramref name="extra"/> also skips Golden Donut and Lucky Streak.
    /// </summary>
    public static Entry Roll(bool boonsOnly = false, bool extra = false)
    {
        bool lucky = !extra && LuckyRolls > 0;
        if (lucky) LuckyRolls--;
        boonsOnly |= lucky;

        bool Allowed(Entry e) => Fits(e)
            && (!boonsOnly || e.Type == Kind.Good)
            && (!extra || (e.Effect != typeof(GoldenDonutEffect) && e.Effect != typeof(LuckyStreakEffect)));

        float total = 0f;
        foreach (var e in Pool) if (Allowed(e)) total += e.Weight;
        float roll = UnityEngine.Random.value * total;
        foreach (var e in Pool)
        {
            if (!Allowed(e)) continue;
            roll -= e.Weight;
            if (roll <= 0f) return e;
        }
        return Pool[0];
    }
}
