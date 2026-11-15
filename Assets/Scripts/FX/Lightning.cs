using UnityEngine;

/// <summary>
/// Lightning (the Rock's rare strike): a jagged white-blue bolt from the sky onto each struck point, crackling arcs
/// between the links of the chain, a flash and a thunder clap. Local visuals; every screen calls
/// <see cref="Strike"/> from the same RPC.
/// </summary>
public class Lightning : MonoBehaviour
{
    private const float Life = 0.55f;
    private static Material material;

    private LineRenderer[] lines;
    private float born;

    /// <param name="points">Where each one was struck (chests).</param>
    /// <param name="links">For each point, the index it chained from (-1: the first, straight from the sky).</param>
    public static void Strike(Vector3[] points, int[] links)
    {
        if (points == null || points.Length == 0) return;
        if (material == null) material = new Material(Shader.Find("Sprites/Default"));

        var bolt = new GameObject("Lightning").AddComponent<Lightning>();
        bolt.born = Time.time;
        bolt.lines = new LineRenderer[points.Length * 2];
        for (int i = 0; i < points.Length; i++)
        {
            // From the sky onto every one of them, and an arc from whoever it jumped from
            bolt.lines[i * 2] = bolt.Bolt(points[i] + Vector3.up * 45f + Random.insideUnitSphere * 4f, points[i], 0.35f, 22);
            if (links != null && i < links.Length && links[i] >= 0 && links[i] < points.Length)
                bolt.lines[i * 2 + 1] = bolt.Bolt(points[links[i]], points[i], 0.12f, 12);

            Vfx.Burst(points[i], new Color(0.75f, 0.85f, 1f), 26, 7f, 0.25f, 0.5f);
            Vfx.Ring(points[i] + Vector3.down * 0.9f, new Color(0.7f, 0.85f, 1f), 36, 5f, 0.3f, 0.4f);
            Sfx.At(SfxKind.Explosion, points[i], 1f, 120f, 0.55f);
            Sfx.At(SfxKind.Snap, points[i], 1f, 60f, 0.6f);
        }

        // A jolt for anyone nearby, harder the closer they are
        var cam = PlayerCam.Local != null ? PlayerCam.Local.Camera : Camera.main;
        if (cam != null)
        {
            float nearest = float.MaxValue;
            foreach (var p in points) nearest = Mathf.Min(nearest, Vector3.Distance(cam.transform.position, p));
            if (nearest < 60f) Fx.Shake(Mathf.Lerp(0.5f, 0.1f, nearest / 60f));
        }
    }

    private LineRenderer Bolt(Vector3 from, Vector3 to, float width, int segments)
    {
        var go = new GameObject("Bolt");
        go.transform.SetParent(transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.material = material;
        line.widthMultiplier = width;
        line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.positionCount = segments + 1;
        float jitter = Vector3.Distance(from, to) * 0.04f;
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            Vector3 p = Vector3.Lerp(from, to, t);
            if (i > 0 && i < segments) p += Random.insideUnitSphere * jitter;
            line.SetPosition(i, p);
        }
        return line;
    }

    private void Update()
    {
        float age = Time.time - born;
        if (age > Life) { Destroy(gameObject); return; }
        // Flickers bright a couple of times, then fades
        float flicker = age < 0.25f ? (Mathf.Sin(age * 70f) > -0.3f ? 1f : 0.35f) : 1f - (age - 0.25f) / (Life - 0.25f);
        var c = new Color(0.85f, 0.92f, 1f, flicker);
        foreach (var l in lines) if (l != null) { l.startColor = c; l.endColor = c; }
    }
}
