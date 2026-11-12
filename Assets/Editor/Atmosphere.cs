using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The sunny-day setting for the floating-island maps, applied by a level builder to the scene it's building:
///   - a sky (Environment/Sky/SunnyDaySky.shader): blue gradient, a sun, slow drifting clouds, a sea of clouds below
///   - lighting meant to make everywhere easy to see without looking flat: a warm sun with soft, light shadows
///     (strength ~0.5) and a bright three-colour ambient (sky / horizon / ground), so ceilings, undersides and rooms
///     under tall walls are lit from every side; a little distance haze
///   - a gentle colour grade on the level's own volume profile (neutral tonemapping, a touch of bloom and colour)
///   - wind: a WindZone and two particle systems blowing across the map (faint streaks, drifting dust), which die on
///     walls so they don't blow through buildings
/// The sky material, particle material and volume profile are shared assets in Environment/Sky.
/// </summary>
public static class Atmosphere
{
    private const string Folder = "Assets/Environment/Sky";
    private const string SkyPath = Folder + "/SunnyDaySky.mat";
    private const string WindMatPath = Folder + "/WindParticle.mat";
    private const string ProfilePath = Folder + "/SunnyDayVolume.asset";

    private static readonly Color Horizon = new(0.72f, 0.85f, 0.97f);

    /// <param name="parent">Where the wind objects go (rebuilt with the level).</param>
    /// <param name="wind">The wind's direction (horizontal).</param>
    /// <param name="area">The space the wind fills (the playable map, a little beyond).</param>
    public static void SunnyDay(Transform parent, Vector3 wind, Bounds area)
    {
        var sun = Sun();
        var sky = Material(SkyPath, "TurboTag/Sunny Day Sky");
        sky.SetVector("_SunDir", -sun.transform.forward);
        EditorUtility.SetDirty(sky);

        RenderSettings.skybox = sky;
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.66f, 0.74f, 0.86f);
        RenderSettings.ambientEquatorColor = new Color(0.60f, 0.62f, 0.64f);
        RenderSettings.ambientGroundColor = new Color(0.52f, 0.50f, 0.47f);
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = Horizon;
        RenderSettings.fogStartDistance = 140f;
        RenderSettings.fogEndDistance = 650f;
        DynamicGI.UpdateEnvironment();

        Grade();
        Wind(parent, wind.normalized, area);
    }

    // A warm late-morning sun, high enough that roofs and decks are lit, with soft light shadows
    private static Light Sun()
    {
        Light sun = null;
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) { sun = l; break; }
        if (sun == null) sun = new GameObject("Directional Light").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
        sun.color = new Color(1f, 0.95f, 0.86f);
        sun.useColorTemperature = false;
        sun.intensity = 1.35f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.5f;
        return sun;
    }

    private static Material Material(string path, string shaderName)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader = Shader.Find(shaderName);
        if (mat == null)
        {
            System.IO.Directory.CreateDirectory(Folder);
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        else if (mat.shader != shader) mat.shader = shader;
        return mat;
    }

    // The level's global volume gets its own profile (the template's shared one stays as it is)
    private static void Grade()
    {
        var volume = Object.FindFirstObjectByType<Volume>();
        if (volume == null) return;
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }
        T Get<T>() where T : VolumeComponent
        {
            if (!profile.TryGet(out T c))
            {
                c = profile.Add<T>(false);
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, profile);
            }
            return c;
        }
        var tone = Get<Tonemapping>();
        tone.mode.Override(TonemappingMode.Neutral);
        var bloom = Get<Bloom>();
        bloom.threshold.Override(1.1f);
        bloom.intensity.Override(0.3f);
        bloom.scatter.Override(0.6f);
        var colour = Get<ColorAdjustments>();
        colour.postExposure.Override(0.15f);
        colour.contrast.Override(6f);
        colour.saturation.Override(8f);
        // Lift the darkest tones a little: shadows stay readable without going flat
        var smh = Get<ShadowsMidtonesHighlights>();
        smh.shadows.Override(new Vector4(1f, 1f, 1.02f, 0.08f));
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        volume.sharedProfile = profile;
        EditorUtility.SetDirty(volume);
    }

    private static void Wind(Transform parent, Vector3 dir, Bounds area)
    {
        var g = new GameObject("Atmosphere").transform;
        g.SetParent(parent, false);
        var zone = new GameObject("Wind Zone").AddComponent<WindZone>();
        zone.transform.SetParent(g, false);
        zone.transform.rotation = Quaternion.LookRotation(dir);
        zone.mode = WindZoneMode.Directional;
        zone.windMain = 0.6f;
        zone.windTurbulence = 0.4f;
        zone.windPulseMagnitude = 0.3f;
        zone.windPulseFrequency = 0.1f;

        var mat = Material(WindMatPath, "TurboTag/Wind Particle");
        // Faint streaks: long, thin, quick, a little curl
        Particles(g, "Wind Streaks", mat, dir, area, rate: 70f, max: 400, life: (2.2f, 3.6f), speed: 15f,
                  size: (0.05f, 0.11f), alpha: 0.16f, stretch: 0.28f, noise: 1.2f, tint: new Color(1f, 1f, 1f));
        // Dust and fluff drifting along: small, slow, lazily wandering
        Particles(g, "Wind Dust", mat, dir, area, rate: 30f, max: 400, life: (8f, 12f), speed: 3.5f,
                  size: (0.05f, 0.1f), alpha: 0.55f, stretch: 0f, noise: 0.7f, tint: new Color(1f, 0.97f, 0.9f));
    }

    private static void Particles(Transform parent, string name, Material mat, Vector3 dir, Bounds area, float rate, int max,
                                  (float min, float max) life, float speed, (float min, float max) size, float alpha,
                                  float stretch, float noise, Color tint)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = area.center;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life.min, life.max);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(size.min, size.max);
        main.startColor = new Color(tint.r, tint.g, tint.b, 1f);
        main.maxParticles = max;
        main.gravityModifier = 0f;

        var emission = ps.emission;
        emission.rateOverTime = rate;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = area.size;

        // Blown along the wind, in world space, gusting a little in strength
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(dir.x * speed * 0.8f, dir.x * speed * 1.2f);
        velocity.y = new ParticleSystem.MinMaxCurve(-0.2f, 0.4f);
        velocity.z = new ParticleSystem.MinMaxCurve(dir.z * speed * 0.8f, dir.z * speed * 1.2f);

        var n = ps.noise;
        n.enabled = true;
        n.strength = noise;
        n.frequency = 0.15f;
        n.scrollSpeed = 0.2f;
        n.quality = ParticleSystemNoiseQuality.Medium;

        // Fade in and out so nothing pops
        var colour = ps.colorOverLifetime;
        colour.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(alpha, 0.2f), new GradientAlphaKey(alpha, 0.75f), new GradientAlphaKey(0f, 1f) });
        colour.color = gradient;

        // Walls and floors stop them, so the wind doesn't blow through buildings
        var collision = ps.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.quality = ParticleSystemCollisionQuality.Medium;
        collision.lifetimeLoss = 1f;
        collision.collidesWith = 1 << 6; // Structure

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        if (stretch > 0f)
        {
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = stretch;
            r.lengthScale = 2f;
        }
        else r.renderMode = ParticleSystemRenderMode.Billboard;

        ps.Play();
    }
}
