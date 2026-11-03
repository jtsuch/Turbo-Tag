using UnityEngine;

/// <summary>
/// Teleports the player to where the crosshair lands (up to <see cref="distance"/> m): standing on a
/// floor, beside a wall, under a ceiling, or at full range in open air. If the body doesn't fit there it
/// steps back along the aim, so it never puts the player inside geometry. Momentum is kept.
/// </summary>
[AbilityInfo("Blink to wherever your crosshair points, up to 15 m away.")]
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

        var   capsule = GetComponent<CapsuleCollider>();
        float radius  = capsule != null ? capsule.radius * transform.lossyScale.x : 0.4f;
        float height  = Owner.Height;
        float range   = distance * PowerMultiplier;
        Vector3 origin = cam.position, dir = cam.forward;

        // Where the crosshair lands: the nearest surface that isn't a player, or the end of the range
        bool       hitSomething = false;
        RaycastHit aim = default;
        aim.distance = range;
        foreach (var hit in Physics.RaycastAll(origin, dir, range, blockingLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<Player>() != null || hit.distance >= aim.distance) continue;
            aim = hit;
            hitSomething = true;
        }

        // Stand on floors, hang under ceilings, and appear beside walls (centred on the aim point)
        Vector3 point = hitSomething ? aim.point : origin + dir * range;
        Vector3 feet;
        if (!hitSomething)            feet = point - Vector3.up * (height * 0.5f);
        else if (aim.normal.y > 0.5f) feet = point + Vector3.up * 0.02f;
        else if (aim.normal.y < -0.5f) feet = point - Vector3.up * (height + 0.05f);
        else                          feet = point + aim.normal * (radius + 0.05f) - Vector3.up * (height * 0.5f);

        // Too tight there (a low gap, a corner): step back along the aim until the body fits
        float back = 0f;
        while (!Fits(feet, radius, height) && back < aim.distance)
        {
            back += 0.25f;
            feet  = origin + dir * Mathf.Max(0f, aim.distance - back) - Vector3.up * (height * 0.5f);
        }
        if (!Fits(feet, radius, height)) return;

        Body.position      = feet;
        transform.position = feet;

        if (PlayerCam.Local != null)
        {
            PlayerCam.Local.ChangeFov(PlayerCam.Local.DefaultFov + fovKick, 0.05f);
            PlayerCam.Local.ResetFov(0.3f);
        }
    }

    // Room for the body standing at `feet` (players don't count: they're pushed aside)
    private bool Fits(Vector3 feet, float radius, float height)
    {
        float   r      = radius * 0.95f;
        Vector3 bottom = feet + Vector3.up * (r + 0.02f);
        Vector3 top    = feet + Vector3.up * Mathf.Max(r + 0.02f, height - r);
        foreach (var col in Physics.OverlapCapsule(bottom, top, r, blockingLayers, QueryTriggerInteraction.Ignore))
            if (col.GetComponentInParent<Player>() == null) return false;
        return true;
    }
}
