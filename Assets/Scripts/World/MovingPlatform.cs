using Photon.Pun;
using UnityEngine;

/// <summary>
/// Moves back and forth between two points. Position is a pure function of the shared network
/// clock (PhotonNetwork.Time), so every client computes the same position with no messages.
/// Players standing on it are carried along via a small follower component.
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

        var follower = player.GetComponent<PlatformFollower>() ?? player.gameObject.AddComponent<PlatformFollower>();
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
