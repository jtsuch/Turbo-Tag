using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Debug view of every ability's area of effect (admin console: <c>aoe</c> toggles it, local to this screen). Each
/// ability calls one of these where its effect is worked out, with the same numbers it uses, so what you see is the
/// real reach in 3D, not just a ring on the floor:
///   <see cref="Sphere"/> for distance checks in every direction (explosions, scans, the Frag, the Nuke),
///   <see cref="Cylinder"/> for "within r sideways and between these heights" (the Molly pool, the Slow Field, vents),
///   <see cref="Cone"/> for cones (Honk, Ink Blast), <see cref="Sector"/> for a flat arc (the Turret),
///   <see cref="Column"/> for a beam along a direction (the Gravity Well) and <see cref="Line"/> (the Trip Wire).
/// Pass a Transform to follow (traps, drones, clouds: drawn until it's gone) or seconds for a one-off moment. Lasting
/// objects keep the returned shape and ask again whenever it's null (<c>if (AoeDebug.On &amp;&amp; aoe == null) aoe = …</c>
/// in Update), so turning the view on shows what's already out.
/// Wireframes in the ability's colour with a label (name and size), drawn over everything.
/// Nothing is made while it's off, so the calls cost nothing in a normal game.
/// </summary>
public class AoeDebug : MonoBehaviour
{
    /// <summary>Shown on this screen (the console's <c>aoe</c>).</summary>
    public static bool On { get; private set; }

    public static void Toggle(bool? on = null)
    {
        On = on ?? !On;
        if (!On) foreach (var s in new List<AoeDebug>(live)) if (s != null) Destroy(s.gameObject);
    }

    private static readonly List<AoeDebug> live = new();
    private static Material lineMat;

    private Transform follow;
    private Vector3 offset;
    private float until;
    private TextMeshPro label;

    // ─── Shapes ───────────────────────────────────────────────────────────────

    public static AoeDebug Sphere(Vector3 centre, float radius, Color color, string name, float seconds = 3f, Transform follow = null)
    {
        var s = Make(centre, color, name, $"{radius:0.#} m radius (sphere)", seconds, follow, radius);
        if (s == null) return null;
        s.Circle(Vector3.zero, Vector3.up, radius, color);
        s.Circle(Vector3.zero, Vector3.right, radius, color);
        s.Circle(Vector3.zero, Vector3.forward, radius, color);
        s.Circle(Vector3.zero, (Vector3.right + Vector3.forward).normalized, radius, color * 0.7f);
        s.Circle(Vector3.zero, (Vector3.right - Vector3.forward).normalized, radius, color * 0.7f);
        s.Floor(radius, true, 0f, 0f, color);
        return s;
    }

    /// <summary>Within <paramref name="radius"/> sideways, from <paramref name="below"/> under to <paramref name="above"/> over the base point.</summary>
    public static AoeDebug Cylinder(Vector3 basePoint, float radius, float below, float above, Color color, string name, float seconds = 3f, Transform follow = null)
    {
        var s = Make(basePoint, color, name, $"{radius:0.#} m radius, {-below:0.#} to +{above:0.#} m (cylinder)", seconds, follow, above);
        if (s == null) return null;
        s.Circle(Vector3.down * below, Vector3.up, radius, color);
        s.Circle(Vector3.up * above, Vector3.up, radius, color);
        s.Circle(Vector3.zero, Vector3.up, radius, color * 0.6f);
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI / 4f;
            Vector3 side = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
            s.Segment(side + Vector3.down * below, side + Vector3.up * above, color);
        }
        s.Floor(radius, false, below, above, color);
        return s;
    }

    /// <summary>A cone from <paramref name="apex"/> along <paramref name="dir"/>, <paramref name="halfAngle"/>° either side.</summary>
    public static AoeDebug Cone(Vector3 apex, Vector3 dir, float range, float halfAngle, Color color, string name, float seconds = 3f)
    {
        var s = Make(apex, color, name, $"{range:0.#} m, {halfAngle:0}° either side (cone)", seconds, null, 1f);
        if (s == null) return null;
        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
        float rad = halfAngle * Mathf.Deg2Rad;
        Vector3 end = dir * range * Mathf.Cos(rad);
        float endRadius = range * Mathf.Sin(rad);
        s.Circle(end, dir, endRadius, color);
        Vector3 a = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized, b = Vector3.Cross(dir, a);
        for (int i = 0; i < 8; i++)
        {
            float t = i * Mathf.PI / 4f;
            s.Segment(Vector3.zero, end + (a * Mathf.Cos(t) + b * Mathf.Sin(t)) * endRadius, color);
        }
        // The far cap is a piece of sphere (the range is a distance): an arc across it
        s.Arc(Vector3.zero, dir, a, range, halfAngle, color);
        s.Arc(Vector3.zero, dir, b, range, halfAngle, color);
        return s;
    }

    /// <summary>A flat arc in front of <paramref name="follow"/> (its forward), <paramref name="halfAngle"/>° either side.</summary>
    public static AoeDebug Sector(Transform follow, float range, float halfAngle, Color color, string name)
    {
        var s = Make(follow.position, color, name, $"{range:0.#} m, {halfAngle:0}° either side (flat arc)", 0f, follow, 2f);
        if (s == null) return null;
        s.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(follow.forward, Vector3.up));
        s.Arc(Vector3.zero, Vector3.forward, Vector3.up, range, halfAngle, color, true);
        Vector3 l = Quaternion.Euler(0f, -halfAngle, 0f) * Vector3.forward, r = Quaternion.Euler(0f, halfAngle, 0f) * Vector3.forward;
        s.Segment(Vector3.zero, l * range, color);
        s.Segment(Vector3.zero, r * range, color);
        return s;
    }

    /// <summary>A beam <paramref name="radius"/> wide from <paramref name="back"/> behind to <paramref name="length"/> along <paramref name="follow"/>'s forward.</summary>
    public static AoeDebug Column(Transform follow, float length, float back, float radius, Color color, string name)
    {
        var s = Make(follow.position, color, name, $"{length:0.#} m long, {radius:0.#} m radius (beam)", 0f, follow, 1f);
        if (s == null) return null;
        s.transform.rotation = follow.rotation;
        s.Circle(Vector3.back * back, Vector3.forward, radius, color);
        s.Circle(Vector3.forward * length, Vector3.forward, radius, color);
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI / 4f;
            Vector3 side = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius;
            s.Segment(side + Vector3.back * back, side + Vector3.forward * length, color);
        }
        return s;
    }

    public static AoeDebug Line(Vector3 a, Vector3 b, float reach, Color color, string name, Transform follow = null)
    {
        var s = Make(a, color, name, $"{Vector3.Distance(a, b):0.#} m long, {reach:0.##} m reach", 0f, follow, 0.5f);
        if (s == null) return null;
        s.Segment(Vector3.zero, b - a, color, Mathf.Max(0.04f, reach * 2f));
        return s;
    }

    // ─── Building ─────────────────────────────────────────────────────────────

    private static AoeDebug Make(Vector3 at, Color color, string name, string size, float seconds, Transform follow, float labelHeight)
    {
        if (!On) return null;
        if (lineMat == null)
        {
            var shader = Shader.Find("Sprites/Default");
            lineMat = new Material(shader) { renderQueue = 4000 };
        }
        var go = new GameObject("AOE " + name);
        var s = go.AddComponent<AoeDebug>();
        go.transform.position = at;
        s.follow = follow;
        if (follow != null) s.offset = at - follow.position;
        s.until = seconds > 0f ? Time.time + seconds : float.PositiveInfinity;
        live.Add(s);

        // The label: name and size, over the shape, always facing the camera
        var tag = new GameObject("Label");
        tag.transform.SetParent(go.transform, false);
        s.label = tag.AddComponent<TextMeshPro>();
        s.label.transform.localPosition = Vector3.up * (labelHeight + 0.6f);
        s.label.font = UIKit.FontAsset(UIKit.Font.Display);
        s.label.fontSize = 4f;
        s.label.alignment = TextAlignmentOptions.Center;
        s.label.textWrappingMode = TextWrappingModes.NoWrap;
        s.label.color = Color.Lerp(color, Color.white, 0.4f);
        s.label.text = $"{name}\n<size=70%>{size}</size>";
        return s;
    }

    private void Circle(Vector3 centre, Vector3 normal, float radius, Color color)
    {
        Vector3 a = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized, b = Vector3.Cross(normal, a);
        const int n = 48;
        var pts = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            float t = i * Mathf.PI * 2f / n;
            pts[i] = centre + (a * Mathf.Cos(t) + b * Mathf.Sin(t)) * radius;
        }
        Lines(pts, color, true);
    }

    // An arc of the given radius from -angle to +angle around axis 'up' of the plane holding 'dir'
    private void Arc(Vector3 centre, Vector3 dir, Vector3 up, float radius, float halfAngle, Color color, bool flat = false)
    {
        const int n = 24;
        var pts = new Vector3[n + 1];
        for (int i = 0; i <= n; i++)
        {
            float ang = Mathf.Lerp(-halfAngle, halfAngle, i / (float)n);
            pts[i] = centre + Quaternion.AngleAxis(ang, flat ? Vector3.up : up) * dir * radius;
        }
        Lines(pts, color, false);
    }

    private void Segment(Vector3 a, Vector3 b, Color color, float width = 0.06f) => Lines(new[] { a, b }, color, false, width);

    private void Lines(Vector3[] pts, Color color, bool loop, float width = 0.06f)
    {
        var go = new GameObject("Wire");
        go.transform.SetParent(transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = loop;
        line.positionCount = pts.Length;
        line.SetPositions(pts);
        line.widthMultiplier = width;
        line.sharedMaterial = lineMat;
        color.a = 0.9f;
        line.startColor = line.endColor = color;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
    }

    // ─── The footprint on the floor ───────────────────────────────────────────

    private LineRenderer floorLine;
    private float floorRadius, floorBelow, floorAbove;
    private bool floorIsSphere;
    private readonly Vector3[] floorPoints = new Vector3[72];

    // A bright ring lying on the real floor under the shape (each point on whatever surface is below it): where a
    // sphere meets the ground, or a cylinder's edge on the ground within its height band
    private void Floor(float radius, bool sphere, float below, float above, Color color)
    {
        floorRadius = radius; floorIsSphere = sphere; floorBelow = below; floorAbove = above;
        var go = new GameObject("Floor Footprint");
        go.transform.SetParent(transform, false);
        floorLine = go.AddComponent<LineRenderer>();
        floorLine.useWorldSpace = true;
        floorLine.loop = true;
        floorLine.positionCount = floorPoints.Length;
        floorLine.widthMultiplier = 0.12f;
        floorLine.sharedMaterial = lineMat;
        color.a = 1f;
        floorLine.startColor = floorLine.endColor = Color.Lerp(color, Color.white, 0.25f);
        floorLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        UpdateFloor();
    }

    private void UpdateFloor()
    {
        if (floorLine == null) return;
        Vector3 c = transform.position;
        if (floorIsSphere) GroundRing.Drape(c, floorRadius, floorPoints, follow);
        else
        {
            for (int i = 0; i < floorPoints.Length; i++)
            {
                float a = i * Mathf.PI * 2f / floorPoints.Length;
                Vector3 top = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * floorRadius + Vector3.up * floorAbove;
                float y = c.y - floorBelow;
                foreach (var h in Physics.RaycastAll(top, Vector3.down, floorAbove + floorBelow, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    if (h.collider.GetComponentInParent<Player>() == null && (follow == null || !h.transform.IsChildOf(follow)) && h.point.y > y) y = h.point.y;
                floorPoints[i] = new Vector3(top.x, y, top.z);
            }
        }
        for (int i = 0; i < floorPoints.Length; i++) floorPoints[i].y += 0.1f;
        floorLine.SetPositions(floorPoints);
    }

    private bool hadFollow;

    private void Start() => hadFollow = follow != null;

    private void LateUpdate()
    {
        if (Time.time > until || (hadFollow && (follow == null || !follow.gameObject.activeInHierarchy))) { Destroy(gameObject); return; }
        if (follow != null)
        {
            Vector3 was = transform.position;
            transform.position = follow.position + offset;
            if ((transform.position - was).sqrMagnitude > 0.0004f) UpdateFloor();
        }
        var cam = PlayerCam.Local != null ? PlayerCam.Local.Camera : Camera.main;
        if (cam != null && label != null)
            label.transform.rotation = Quaternion.LookRotation(label.transform.position - cam.transform.position);
    }

    private void OnDestroy() => live.Remove(this);
}
