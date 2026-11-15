using UnityEngine;

/// <summary>
/// A ring drawn on the ground (local, every screen calls it from an RPC): shows exactly how far an effect reaches
/// (Echo Stomp, the Drone). It lies on the real floor: every point of it is dropped onto whatever surface is under it
/// (<see cref="Drape"/>), so over a platform's edge it goes down to the floor below instead of hanging in the air, and
/// for a sphere-shaped reach it follows where the sphere meets the ground at that height. <see cref="Show"/> makes
/// one that fades; <see cref="Held"/> one that stays (move it with <see cref="Place"/>).
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

    /// <summary>Lays the ring on the ground where a sphere of <paramref name="radius"/> round <paramref name="centre"/> meets it.</summary>
    public void Place(Vector3 centre, float radius)
    {
        Drape(centre, radius, points, null);
        for (int i = 0; i < Points; i++) points[i].y += 0.08f;
        line.SetPositions(points);
    }

    private readonly Vector3[] points = new Vector3[Points];
    private static readonly RaycastHit[] hits = new RaycastHit[16];

    /// <summary>
    /// Fills <paramref name="into"/> with a ring on the floor where a sphere (<paramref name="centre"/>,
    /// <paramref name="radius"/>) meets it: for each direction it finds the floor straight down at that distance
    /// (players and <paramref name="ignore"/> don't count), then sizes the circle for that floor's height (a lower
    /// floor gets a smaller circle), so the ring always lies on a real surface. A point with no floor in reach sits
    /// at the edge of the sphere's level, like before.
    /// </summary>
    public static void Drape(Vector3 centre, float radius, Vector3[] into, Transform ignore)
    {
        int n = into.Length;
        float lastY = centre.y;
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            float r = radius, y = float.NaN;
            for (int pass = 0; pass < 2; pass++)
            {
                if (!FloorBelow(centre + dir * r + Vector3.up * 0.5f, radius + 1f, ignore, out float floorY)) break;
                y = floorY;
                float dy = centre.y - y;
                r = Mathf.Sqrt(Mathf.Max(0f, radius * radius - dy * dy));
            }
            if (float.IsNaN(y)) { y = lastY; r = radius; }
            lastY = y;
            into[i] = new Vector3(centre.x + dir.x * r, y, centre.z + dir.z * r);
        }
    }

    private static bool FloorBelow(Vector3 from, float distance, Transform ignore, out float y)
    {
        y = 0f;
        int count = Physics.RaycastNonAlloc(from, Vector3.down, hits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            var h = hits[i];
            if (h.distance >= best || h.collider.GetComponentInParent<Player>() != null) continue;
            if (ignore != null && h.transform.IsChildOf(ignore)) continue;
            best = h.distance;
            y = h.point.y;
        }
        return best < float.MaxValue;
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
