using UnityEngine;

/// <summary>
/// Hitscan ray: pulls out the pistol and fires a beam where the player is looking. A player it hits
/// grows by <see cref="growMultiplier"/> for <see cref="growDuration"/> seconds.
///   shooter: raycast → beam (local) + AbilityHandler.BroadcastBeam (others see gun and beam)
///   hit player: Player.RequestTimedModifier → their owner applies the size change (size is owner-side)
/// </summary>
[AbilityInfo("Zap someone with a growth ray: they grow to double size for 8 seconds. Harder for them to hide!")]
public class GrowRay : QuickAbility
{
    [Header("References")]
    [Tooltip("The camera (aim). Copied from the grapples.")]
    [SerializeField] private Transform cam;
    [Tooltip("First-person pistol tip (beam start for the shooter).")]
    [SerializeField] private Transform gunTip;
    [Tooltip("Third-person pistol tip (beam start seen by others).")]
    [SerializeField] private Transform remoteGunTip;

    [Header("Ray")]
    [TunableParam("Range", 5f, 300f)]
    [SerializeField] private float range = 100f;
    [TunableParam("Size Multiplier", 1f, 5f)]
    [SerializeField] private float growMultiplier = 2f;
    [TunableParam("Grow Duration", 1f, 60f)]
    [SerializeField] private float growDuration = 8f;
    [Tooltip("How long the gun stays out after a shot (s).")]
    [SerializeField] private float gunShowTime = 0.6f;

    [Header("Beam")]
    [SerializeField] private Color beamColor = new(0.4f, 1f, 0.3f, 1f);
    [SerializeField] private float beamWidth = 0.08f;
    [SerializeField] private float beamTime  = 0.2f;

    private float        gunOutUntil;
    private LineRenderer beam;
    private float        beamUntil;
    private Vector3      beamEnd;

    public override bool ShowsGrappleGun => enabled && Time.time < gunOutUntil;

    protected override void Activate()
    {
        if (cam == null) return;

        // The nearest thing hit decides: a player grows, a wall just stops the beam
        Vector3 end    = cam.position + cam.forward * range;
        float nearest  = range;
        Player  target = null;
        foreach (var hit in Physics.RaycastAll(cam.position, cam.forward, range, ~0, QueryTriggerInteraction.Ignore))
        {
            var hitPlayer = hit.collider.GetComponentInParent<Player>();
            if (hitPlayer == Owner || hit.distance >= nearest) continue;
            nearest = hit.distance;
            end     = hit.point;
            target  = hitPlayer;
        }

        if (target != null && !target.IsCaught)
            target.RequestTimedModifier(Stat.Size, nameof(GrowRay), growMultiplier, growDuration);

        ShowShot(end);
        Handler.BroadcastBeam(end);
    }

    // ─── Visuals (every client) ───────────────────────────────────────────────

    /// <summary>Gun out and a short beam to <paramref name="end"/>.</summary>
    public void ShowShot(Vector3 end)
    {
        gunOutUntil = Time.time + gunShowTime;
        beamUntil   = Time.time + beamTime;
        beamEnd     = end;
        EnsureBeam();
        beam.enabled = true;
        UpdateBeam();
    }

    private void LateUpdate()
    {
        if (beam == null || !beam.enabled) return;
        if (Time.time >= beamUntil) { beam.enabled = false; return; }
        UpdateBeam();
    }

    private void UpdateBeam()
    {
        Transform tip = IsLocal ? gunTip : remoteGunTip;
        Vector3 start = tip != null ? tip.position : transform.position + Vector3.up * 1.4f;
        beam.SetPosition(0, start);
        beam.SetPosition(1, beamEnd);
        float fade = Mathf.Clamp01((beamUntil - Time.time) / beamTime);
        beam.widthMultiplier = beamWidth * fade;
    }

    private void EnsureBeam()
    {
        if (beam != null) return;
        var go = new GameObject("GrowRayBeam");
        go.transform.SetParent(transform, false);
        beam = go.AddComponent<LineRenderer>();
        beam.positionCount     = 2;
        beam.useWorldSpace     = true;
        beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        beam.receiveShadows    = false;
        beam.material          = new Material(Shader.Find("Sprites/Default")); // Always in builds
        beam.startColor = beam.endColor = beamColor;
        beam.enabled = false;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (beam != null) beam.enabled = false;
        gunOutUntil = 0f;
    }
}
