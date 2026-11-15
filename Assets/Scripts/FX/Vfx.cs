using UnityEngine;

/// <summary>
/// Gameplay particle bursts made in code (no effect prefabs): sparks, dust puffs, coloured bursts, rings and
/// explosions. Two shared world-space particle systems (soft round dots: one floats and fades, one falls under
/// gravity) emit every burst, so effects are cheap and need no setup. Local only: call it on every client that
/// should see the effect (the same places that play the sounds).
/// </summary>
public static class Vfx
{
    private static ParticleSystem floaty, falling;

    /// <summary>A burst of <paramref name="count"/> soft dots flying out from <paramref name="position"/>.</summary>
    public static void Burst(Vector3 position, Color color, int count, float speed, float size, float life = 0.6f,
        bool gravity = false, Vector3? direction = null, float spread = 1f)
    {
        var system = gravity ? Falling : Floaty;
        var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
        for (int i = 0; i < count; i++)
        {
            Vector3 dir = Random.insideUnitSphere;
            if (direction.HasValue) dir = Vector3.Slerp(direction.Value.normalized, dir, spread).normalized;
            p.position = position + Random.insideUnitSphere * size * 0.3f;
            p.velocity = dir * speed * Random.Range(0.5f, 1f);
            p.startColor = Color.Lerp(color, Color.white, Random.Range(0f, 0.25f));
            p.startSize = size * Random.Range(0.6f, 1.2f);
            p.startLifetime = life * Random.Range(0.7f, 1.1f);
            system.Emit(p, 1);
        }
    }

    /// <summary>A flat ring of dust spreading along the ground (landings, launches, bounce pads).</summary>
    public static void Ring(Vector3 position, Color color, int count, float speed, float size, float life = 0.5f)
    {
        var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
        for (int i = 0; i < count; i++)
        {
            float a = i / (float)count * Mathf.PI * 2f + Random.Range(-0.1f, 0.1f);
            var dir = new Vector3(Mathf.Cos(a), 0.15f, Mathf.Sin(a));
            p.position = position + Vector3.up * 0.1f;
            p.velocity = dir * speed * Random.Range(0.7f, 1f);
            p.startColor = color;
            p.startSize = size * Random.Range(0.7f, 1.2f);
            p.startLifetime = life * Random.Range(0.8f, 1.1f);
            Floaty.Emit(p, 1);
        }
    }

    /// <summary>Hot sparks (gunshot impacts).</summary>
    public static void Sparks(Vector3 position, Vector3 normal, Color color, int count = 8) =>
        Burst(position, color, count, 7f, 0.09f, 0.35f, gravity: true, direction: normal, spread: 0.55f);

    /// <summary>A fiery explosion with smoke, scaled to the blast radius.</summary>
    public static void Explosion(Vector3 position, float radius)
    {
        float r = Mathf.Clamp(radius, 1f, 30f);
        Burst(position, new Color(1f, 0.85f, 0.35f), 30, r * 2.5f, r * 0.35f, 0.35f);
        Burst(position, new Color(1f, 0.45f, 0.1f), 30, r * 1.8f, r * 0.45f, 0.55f);
        Burst(position, new Color(0.25f, 0.22f, 0.25f, 0.7f), 26, r * 0.9f, r * 0.6f, 1.4f, direction: Vector3.up, spread: 0.8f);
        Burst(position, new Color(1f, 0.7f, 0.2f), 24, r * 3f, 0.12f, 0.8f, gravity: true);
    }

    private static ParticleSystem Floaty => floaty != null ? floaty : floaty = Make("Vfx Floaty", 0f, 2.5f);
    private static ParticleSystem Falling => falling != null ? falling : falling = Make("Vfx Falling", 1.6f, 0.5f);

    private static ParticleSystem Make(string name, float gravity, float drag)
    {
        var go = new GameObject(name);
        Object.DontDestroyOnLoad(go);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true; // Keeps simulating forever (emission is off: only Emit adds particles)
        main.playOnAwake = false;
        main.maxParticles = 3000;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = gravity;
        main.startSpeed = 0f;

        var emission = ps.emission;
        emission.enabled = false;
        var shape = ps.shape;
        shape.enabled = false;

        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.drag = drag;
        limit.dampen = 0f;
        limit.limit = 1000f;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.2f, 1f), new Keyframe(1f, 0.1f)));

        var color = ps.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        var material = new Material(Shader.Find("Sprites/Default")) { mainTexture = UISprites.Blob.texture };
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        ps.Play();
        return ps;
    }
}
