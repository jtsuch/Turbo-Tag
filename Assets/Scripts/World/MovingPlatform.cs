using Photon.Pun;
using UnityEngine;

/// <summary>
/// Moves back and forth between two points. Position is a pure function of the shared network
/// clock (PhotonNetwork.Time), so every client computes the same position with no messages.
/// Players standing on it are carried along via a small follower component.
/// **Landing spots are marked** (Justin, Nov 14: standing under a platform that comes down pushes you through the
/// floor, so show where it lands): if its lowest point comes within a body's height of the floor under it, that
/// footprint is painted on the floor (a yellow outline and a fill that brightens as the platform comes down).
/// Apply this to every mover that comes down near a floor.
/// Attach to: a platform with a kinematic Rigidbody (recommended) and assign pointA/pointB.
/// </summary>
public class MovingPlatform : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private Transform pointA;
    [SerializeField] private Transform pointB;
    [SerializeField] private float     speed      = 4f;
    [SerializeField] private bool      startAtA   = true;
    [SerializeField] private bool      waitAtEnds;
    [SerializeField] private float     waitTime   = 1f;

    private Vector3   a, b;
    private Rigidbody body;

    private void Start()
    {
        body = GetComponent<Rigidbody>();
        a = startAtA ? pointA.position : pointB.position;
        b = startAtA ? pointB.position : pointA.position;
        BuildLandingMark();
    }

    // ─── Landing spot ─────────────────────────────────────────────────────────

    private const float Clearance = 3.2f;   // A standing penguin is ~2.9 m: anything lower than this squashes
    private Material markFill, markEdge;
    private float markFloorY, markHighY;
    private GameObject mark;

    private void BuildLandingMark()
    {
        if (!FindLandingSpot(a, b, out Bounds bounds, out Vector3 centre, out float bottom, out float floorY)) return;

        markFloorY = floorY;
        markHighY = bottom + Mathf.Abs(a.y - b.y);   // Its underside at the top of the path
        mark = new GameObject($"{name} Landing Mark");
        mark.transform.position = new Vector3(centre.x, floorY + 0.03f, centre.z);
        markFill = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 0.82f, 0.1f, 0.12f) };
        markEdge = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 0.82f, 0.1f, 0.85f) };
        float w = bounds.size.x, d = bounds.size.z, t = 0.25f;
        MarkQuad(Vector3.zero, new Vector2(w, d), markFill);
        MarkQuad(new Vector3(0f, 0.01f, d / 2f - t / 2f), new Vector2(w, t), markEdge);
        MarkQuad(new Vector3(0f, 0.01f, -d / 2f + t / 2f), new Vector2(w, t), markEdge);
        MarkQuad(new Vector3(w / 2f - t / 2f, 0.01f, 0f), new Vector2(t, d), markEdge);
        MarkQuad(new Vector3(-w / 2f + t / 2f, 0.01f, 0f), new Vector2(t, d), markEdge);
    }

    /// <summary>
    /// Where this mover comes down near a floor: its footprint at the lowest end of its path (<paramref name="centre"/>,
    /// <paramref name="bounds"/> as it is now), its underside there and the floor under it. False for movers that
    /// don't go up and down, or whose lowest point stays more than a body's height above the floor. The floor is looked
    /// for from the platform's top down (one that comes right down to the floor sits in it, so its underside is no
    /// place to start), taking the highest surface that isn't the platform and isn't above its underside + 1 m.
    /// Public so the editor can list every map's marks (LandingMarkAudit).
    /// </summary>
    public bool FindLandingSpot(Vector3 from, Vector3 to, out Bounds bounds, out Vector3 centre, out float bottom, out float floorY)
    {
        bounds = default; centre = default; bottom = 0f; floorY = float.NegativeInfinity;
        var cols = GetComponentsInChildren<Collider>();
        if (cols.Length == 0 || Mathf.Abs(from.y - to.y) < 0.5f) return false;   // Only movers that go up and down
        bounds = cols[0].bounds;
        foreach (var c in cols) bounds.Encapsulate(c.bounds);
        Vector3 low = from.y < to.y ? from : to;
        Vector3 shift = low - transform.position;
        centre = bounds.center + shift;
        bottom = bounds.min.y + shift.y;
        float top = bounds.max.y + shift.y;
        float best = float.NegativeInfinity;
        // Several rays over the footprint (a hole under its middle shouldn't hide the floor round it)
        Vector3 e = bounds.extents * 0.7f;
        foreach (var o in new[] { Vector3.zero, new Vector3(e.x, 0f, e.z), new Vector3(-e.x, 0f, e.z), new Vector3(e.x, 0f, -e.z), new Vector3(-e.x, 0f, -e.z) })
            foreach (var hit in Physics.RaycastAll(new Vector3(centre.x + o.x, top + 0.1f, centre.z + o.z), Vector3.down, top - bottom + Clearance + 1f,
                                                   Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(transform) || hit.collider.isTrigger || hit.collider.GetComponentInParent<Player>() != null) continue;
                if (hit.point.y > bottom + 1f) continue;   // Something beside/above it, not the floor it lands on
                if (hit.point.y > best) best = hit.point.y;
            }
        floorY = best;
        return !float.IsNegativeInfinity(best) && bottom - best <= Clearance;
    }

    private void MarkQuad(Vector3 local, Vector2 size, Material mat)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(q.GetComponent<Collider>());
        q.transform.SetParent(mark.transform, false);
        q.transform.localPosition = local;
        q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // Facing up
        q.transform.localScale = new Vector3(size.x, size.y, 1f);
        var r = q.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    // The fill brightens as the platform comes down onto it
    private void Update()
    {
        if (markFill == null) return;
        var cols = GetComponentsInChildren<Collider>();
        if (cols.Length == 0) return;
        float bottomNow = cols[0].bounds.min.y;
        foreach (var c in cols) bottomNow = Mathf.Min(bottomNow, c.bounds.min.y);
        float near = 1f - Mathf.InverseLerp(markFloorY + Clearance, Mathf.Max(markFloorY + Clearance + 0.01f, markHighY), bottomNow);
        if (bottomNow < markFloorY + Clearance) near = 1f;
        float pulse = near >= 1f ? 0.5f + 0.5f * Mathf.Sin(Time.time * 10f) : 0f;
        markFill.color = new Color(1f, 0.82f, 0.1f, 0.08f + 0.22f * near + 0.1f * pulse);
    }

    private void OnDestroy()
    {
        if (mark != null) Destroy(mark);
        if (markFill != null) Destroy(markFill);
        if (markEdge != null) Destroy(markEdge);
    }

    private void FixedUpdate()
    {
        Vector3 target = PositionAt(PhotonNetwork.Time);
        if (body != null && body.isKinematic) body.MovePosition(target);
        else                                  transform.position = target;
    }

    private Vector3 PositionAt(double time)
    {
        float distance = Vector3.Distance(a, b);
        if (distance < 0.001f || speed <= 0f) return a;

        float travel = distance / speed;
        float wait   = waitAtEnds ? waitTime : 0f;
        float cycle  = 2f * (travel + wait);
        float t      = (float)(time % cycle);

        // Out: travel → wait → back: travel → wait
        if (t < travel)                return Vector3.Lerp(a, b, t / travel);
        if (t < travel + wait)         return b;
        if (t < 2f * travel + wait)    return Vector3.Lerp(b, a, (t - travel - wait) / travel);
        return a;
    }

    private void OnCollisionEnter(Collision collision)
    {
        var player = collision.collider.GetComponentInParent<Player>();
        if (player == null || !player.IsLocal) return;

        // TryGetComponent, not GetComponent ?? Add: in the editor a missing component is a "fake null" object
        // that ?? doesn't catch, so the follower was never added and riders slid off
        if (!player.TryGetComponent(out PlatformFollower follower)) follower = player.gameObject.AddComponent<PlatformFollower>();
        follower.Attach(transform);
    }

    private void OnCollisionExit(Collision collision)
    {
        var player = collision.collider.GetComponentInParent<Player>();
        if (player != null && player.TryGetComponent(out PlatformFollower follower))
            follower.Detach(transform);
    }

    private void OnDrawGizmos()
    {
        if (pointA == null || pointB == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(pointA.position, pointB.position);
        Gizmos.DrawSphere(pointA.position, 0.5f);
        Gizmos.DrawSphere(pointB.position, 0.5f);
    }
}

/// <summary>Carries a local player's rigidbody along with the platform they stand on.</summary>
public class PlatformFollower : MonoBehaviour
{
    private Transform platform;
    private Vector3   lastPosition;
    private Rigidbody body;

    private void Awake() => body = GetComponent<Rigidbody>();

    public void Attach(Transform newPlatform)
    {
        platform     = newPlatform;
        lastPosition = platform.position;
    }

    public void Detach(Transform fromPlatform)
    {
        if (platform == fromPlatform) platform = null;
    }

    private void FixedUpdate()
    {
        if (platform == null) return;

        Vector3 delta = platform.position - lastPosition;
        lastPosition  = platform.position;
        if (delta == Vector3.zero) return;

        if (body != null) body.MovePosition(body.position + delta);
        else              transform.position += delta;
    }
}
