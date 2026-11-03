using UnityEngine;

/// <summary>
/// Teleports the player a set distance in the direction they are looking. Stops short of walls,
/// floors and ceilings in the way, so it never puts the player inside geometry. Momentum is kept.
/// </summary>
[AbilityInfo("Blink 15 m in the direction you're looking. Walls stop you short.")]
public class Teleport : QuickAbility
{
    [Header("References")]
    [Tooltip("The camera (look direction). Found under CameraHolder if empty.")]
    [SerializeField] private Transform cam;

    [Header("Teleport")]
    [TunableParam("Distance", 2f, 60f)]
    [SerializeField] private float distance = 15f;

    [Tooltip("What blocks the teleport (walls, floors, ceilings). Players never do.")]
    [SerializeField] private LayerMask blockingLayers = ~0;

    [SerializeField] private float fovKick = 12f;

    protected override void Awake()
    {
        base.Awake();
        if (cam == null)
        {
            var holder = transform.Find("CameraHolder");
            if (holder != null) cam = holder.GetComponentInChildren<Camera>(true)?.transform ?? holder;
        }
    }

    protected override void Activate()
    {
        if (cam == null) return;

        // Sweep the player's body along the look direction and stop just before anything solid
        var   capsule = GetComponent<CapsuleCollider>();
        float radius  = capsule != null ? capsule.radius * transform.lossyScale.x * 0.9f : 0.4f;
        float height  = capsule != null ? capsule.height * transform.lossyScale.y : Owner.Height;
        Vector3 center = Body.position + Vector3.up * (height * 0.5f);
        Vector3 bottom = center - Vector3.up * Mathf.Max(0f, height * 0.5f - radius);
        Vector3 top    = center + Vector3.up * Mathf.Max(0f, height * 0.5f - radius);
        Vector3 dir    = cam.forward;

        float range  = distance * PowerMultiplier;
        float travel = range;
        var hits = Physics.CapsuleCastAll(bottom, top, radius, dir, range, blockingLayers, QueryTriggerInteraction.Ignore);
        foreach (var hit in hits)
        {
            if (hit.collider.GetComponentInParent<Player>() != null) continue; // Not ourselves or other players
            if (hit.distance <= 0f) continue;                                    // Already touching (start overlap)
            travel = Mathf.Min(travel, hit.distance - 0.05f);
        }

        if (travel <= 0.1f) return;
        Body.position += dir * travel;
        transform.position = Body.position;

        if (PlayerCam.Local != null)
        {
            PlayerCam.Local.ChangeFov(PlayerCam.Local.DefaultFov + fovKick, 0.05f);
            PlayerCam.Local.ResetFov(0.3f);
        }
    }
}
