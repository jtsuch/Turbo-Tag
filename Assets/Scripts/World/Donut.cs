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
    /// <summary>One thing a donut can do.</summary>
    public readonly struct Entry
    {
        public readonly Type  Effect;
        public readonly float Seconds;
        public readonly float Weight;   // Relative chance
        public readonly bool  GunsOnly; // Only in gun modes (ammo, healing on hits)
        public readonly bool  Everyone; // Every player gets it, not just the one who ate the donut

        public Entry(Type effect, float seconds, float weight = 1f, bool gunsOnly = false, bool everyone = false)
        {
            Effect = effect; Seconds = seconds; Weight = weight; GunsOnly = gunsOnly; Everyone = everyone;
        }
    }

    /// <summary>
    /// What a donut can do. Add an effect: a PlayerEffect subclass and a line here.
    /// Balance: a donut should be worth the detour, so boons are a little likelier than curses (weights 1 vs 0.8,
    /// chaos 0.6), curses that take control away (frame rate, flipped or spinning view, scrambled keys) are short,
    /// and the strongest boons (no cooldowns, bubble, x-ray) are brief or rarer.
    /// </summary>
    public static readonly Entry[] Pool =
    {
        // Boons
        new(typeof(DoubleJumpEffect),      20f),
        new(typeof(NoCooldownEffect),      10f, 0.6f),  // Ten seconds of spam is plenty (Nukes included)
        new(typeof(AirControlEffect),      15f),
        new(typeof(XRayEffect),             6f, 0.8f),  // Finds every hider: brief
        new(typeof(SpeedEffect),           15f),
        new(typeof(MoonBootsEffect),       15f),
        new(typeof(GhostEffect),            8f),
        new(typeof(BottomlessMagEffect),   15f, gunsOnly: true),
        new(typeof(VampireEffect),         20f, gunsOnly: true),
        new(typeof(RocketJumpEffect),      15f),
        new(typeof(BubbleEffect),           8f, 0.8f),  // Immune to every ability: shorter
        new(typeof(RadarEffect),           15f),
        new(typeof(RefillEffect),           3f, 0.7f),
        // Curses
        new(typeof(ShrinkFOVEffect),       15f, 0.8f),
        new(typeof(KeybindSwitchEffect),   12f, 0.8f),
        new(typeof(DropFrameRateEffect),    8f, 0.8f),
        new(typeof(ButterfingersEffect),   10f, 0.8f),
        new(typeof(GiantEffect),           15f, 0.8f),
        new(typeof(BeaconEffect),          10f, 0.8f),
        new(typeof(UpsideDownEffect),       8f, 0.8f),
        new(typeof(DrunkEffect),           12f, 0.8f),
        new(typeof(StickyFeetEffect),       8f, 0.8f),
        new(typeof(SpinCycleEffect),       12f, 0.8f),
        // Chaos
        new(typeof(LowGravityPartyEffect), 15f, 0.6f, everyone: true),
        new(typeof(SwapEffect),             3f, 0.6f),
        new(typeof(FishRainEffect),         5f, 0.6f),
    };

    public const float HoverHeight = 1f;   // Above the ground it sits over
    public const float TouchRadius = 0.9f; // From the donut to the nearest point of a body

    private static GameObject model;
    private static Material   beamMaterial;
    private static Material BeamMaterial => beamMaterial != null ? beamMaterial : beamMaterial = new Material(Shader.Find("Sprites/Default"));
    private Transform visual;
    private Vector3   basePosition;
    private float     phase, nextGlint;

    public int Id { get; private set; }

    /// <summary>Makes a donut at <paramref name="ground"/> (it hovers above it).</summary>
    public static Donut Create(int id, Vector3 ground)
    {
        var root = new GameObject($"Donut {id}");
        var donut = root.AddComponent<Donut>();
        donut.Id = id;
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
        light.color = new Color(1f, 0.6f, 0.8f);
        light.shadows = LightShadows.None;

        // A soft pink beam into the sky, so donuts can be spotted from across the map
        var beam = new GameObject("Beam").AddComponent<LineRenderer>();
        beam.transform.SetParent(root.transform, false);
        beam.useWorldSpace = false;
        beam.positionCount = 2;
        beam.SetPosition(0, Vector3.down * HoverHeight);
        beam.SetPosition(1, Vector3.up * 9f);
        beam.widthCurve = new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(1f, 0.05f));
        beam.sharedMaterial = BeamMaterial;
        var fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.55f, 0.85f), 0f), new GradientColorKey(new Color(1f, 0.8f, 0.95f), 1f) },
            new[] { new GradientAlphaKey(0.45f, 0f), new GradientAlphaKey(0f, 1f) });
        beam.colorGradient = fade;
        beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        beam.receiveShadows = false;

        Vfx.Ring(ground, new Color(1f, 0.6f, 0.85f, 0.8f), 18, 3f, 0.3f, 0.5f);
        return donut;
    }

    private void Update()
    {
        float t = Time.time + phase;
        transform.position = basePosition + Vector3.up * (Mathf.Sin(t * 2f) * 0.15f);
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

    /// <summary>A random entry from <see cref="Pool"/> that fits this mode, by weight.</summary>
    public static Entry Roll()
    {
        var manager = GameModeManager.Instance;
        bool guns = manager != null && manager.Mode != null && manager.Mode.UsesGuns;

        float total = 0f;
        foreach (var e in Pool) if (guns || !e.GunsOnly) total += e.Weight;
        float roll = UnityEngine.Random.value * total;
        foreach (var e in Pool)
        {
            if (!guns && e.GunsOnly) continue;
            roll -= e.Weight;
            if (roll <= 0f) return e;
        }
        return Pool[0];
    }
}
