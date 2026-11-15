using UnityEngine;

/// <summary>Boon: middle click blinks you to where you're looking (up to 60 m), three times.</summary>
public class TeleportGunEffect : PlayerEffect
{
    public override string DisplayName => "Teleport Gun";
    public override string Description => "Middle click to blink (3 uses)";
    public override bool   IsGood      => true;
    public override bool   IsChaos     => false;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;

    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;

    }

    private int uses = 3;

    private void Update()
    {
        if (!IsLocalEffect || PlayerCam.Local == null || player.IsCaught) return;
        HUDManager.Prompt(KeyCode.Mouse2, $"Teleport ({uses} left)");
        if (PauseMenuManager.IsPaused || !Input.GetKeyDown(KeyCode.Mouse2)) return;

        var cam = PlayerCam.Local.Camera.transform;
        Vector3 to;
        if (Physics.Raycast(cam.position, cam.forward, out RaycastHit hit, 60f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            to = hit.normal.y > 0.6f ? hit.point : hit.point + hit.normal * 0.6f - Vector3.up * 0.9f;
        else to = cam.position + cam.forward * 60f - Vector3.up * 0.9f;

        Vector3 from = player.transform.position;
        player.Teleport(to, player.transform.rotation);
        if (player.TryGetComponent(out DonutNet net)) net.Blink(from, to);
        if (--uses <= 0) { CancelEffect(); Destroy(this); }
    }
}
