using UnityEngine;

/// <summary>
/// A boost pad (the BoostPad-2x2 model): running over it throws the player the way its chevrons point (the model's
/// local −X, <see cref="launchAxis"/>) at <see cref="boostSpeed"/>, plus a hop. Each client boosts only its
/// own player (an outside force: movement doesn't brake it); every screen sees the whoosh. Adds its own trigger above
/// the pad in Awake, so the prefab's solid collider stays a floor.
/// Attach to: a BoostPad-2x2 instance (level builders add it).
/// </summary>
public class BoostPad : MonoBehaviour
{
    [Tooltip("Speed along the pad's forward it throws you at (m/s); faster players keep their speed.")]
    [SerializeField] private float boostSpeed = 28f;
    [Tooltip("Upward speed added with the boost (m/s).")]
    [SerializeField] private float hop = 5f;
    [Tooltip("The pad's local direction it throws along: the way the chevrons point (−X on the BoostPad-2x2 model).")]
    [SerializeField] private Vector3 launchAxis = Vector3.left;

    /// <summary>The flat world direction the pad throws along.</summary>
    public Vector3 LaunchDirection => Vector3.ProjectOnPlane(transform.TransformDirection(launchAxis), Vector3.up).normalized;

    private float nextFx;

    private void Awake()
    {
        // In world space (the model itself may be imported tilted or scaled), upright over the pad
        var zone = new GameObject("Boost Zone").AddComponent<BoxCollider>();
        zone.transform.SetPositionAndRotation(transform.position, Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
        zone.transform.SetParent(transform, true);
        Vector3 s = zone.transform.lossyScale;
        zone.isTrigger = true;
        zone.center = new Vector3(0f, 1.2f / s.y, 0f);
        zone.size = new Vector3(3.6f / s.x, 2f / s.y, 3.6f / s.z);
        zone.gameObject.AddComponent<BoostZone>().Pad = this;
    }

    internal void Touched(Collider other)
    {
        var player = other.GetComponentInParent<Player>();
        if (player == null) return;
        if (Time.time >= nextFx)
        {
            nextFx = Time.time + 0.3f;
            Sfx.At(SfxKind.Whoosh, transform.position, 1f, 35f, 1.2f);
            Vfx.Burst(transform.position + Vector3.up * 0.5f, new Color(1f, 0.6f, 0.15f), 14, 6f, 0.25f, 0.4f, direction: LaunchDirection, spread: 0.4f);
        }
        if (!player.IsLocal || player.Body == null) return;
        Vector3 dir = LaunchDirection;
        Vector3 v = player.Body.linearVelocity;
        float along = Vector3.Dot(v, dir);
        Vector3 across = Vector3.ProjectOnPlane(v - dir * along, Vector3.up);
        player.Body.linearVelocity = dir * Mathf.Max(along, boostSpeed) + across * 0.3f + Vector3.up * Mathf.Max(v.y, hop);
        player.NotePushed();
    }
}

/// <summary>The trigger above a <see cref="BoostPad"/> (made by it).</summary>
public class BoostZone : MonoBehaviour
{
    public BoostPad Pad;
    private void OnTriggerEnter(Collider other) { if (Pad != null) Pad.Touched(other); }
}
