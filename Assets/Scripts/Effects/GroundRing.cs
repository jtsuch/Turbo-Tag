using UnityEngine;

/// <summary>
/// A flat ring drawn on the ground for a moment (local, every screen calls it from an RPC): shows exactly how far an
/// effect reached (Echo Stomp). It fades out over its lifetime. <see cref="Show"/> makes one.
/// </summary>
public class GroundRing : MonoBehaviour
{
    private const int Points = 72;
    private LineRenderer line;
    private float born, life;
    private Color color;

    /// <summary>A ring of <paramref name="radius"/> round <paramref name="centre"/> (its height is the ground), fading over <paramref name="seconds"/>.</summary>
    public static void Show(Vector3 centre, float radius, Color color, float seconds) => Make(centre, radius, color, seconds);

    /// <summary>A ring that stays (no fade) until destroyed; move it with <see cref="Place"/> (the Drone's reach).</summary>
    public static GroundRing Held(Vector3 centre, float radius, Color color) => Make(centre, radius, color, 0f);

    private static GroundRing Make(Vector3 centre, float radius, Color color, float seconds)
    {
        var ring = new GameObject("Ground Ring").AddComponent<GroundRing>();
        ring.born = Time.time;
        ring.life = seconds > 0f ? Mathf.Max(0.1f, seconds) : 0f;
        ring.color = color;
        var line = ring.line = ring.gameObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = true;
        line.positionCount = Points;
        line.widthMultiplier = 0.14f;
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.Place(centre, radius);
        ring.Update();
        return ring;
    }

    /// <summary>Moves the ring to <paramref name="centre"/> with <paramref name="radius"/>.</summary>
    public void Place(Vector3 centre, float radius)
    {
        for (int i = 0; i < Points; i++)
        {
            float a = i * Mathf.PI * 2f / Points;
            line.SetPosition(i, centre + new Vector3(Mathf.Cos(a) * radius, 0.08f, Mathf.Sin(a) * radius));
        }
    }

    private void Update()
    {
        if (life <= 0f) { var held = color; held.a = 0.75f; line.material.color = held; return; }
        float t = (Time.time - born) / life;
        if (t >= 1f) { Destroy(gameObject); return; }
        var c = color;
        c.a = (1f - t) * 0.9f;
        line.material.color = c;
    }
}
