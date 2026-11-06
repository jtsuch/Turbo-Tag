using UnityEngine;

/// <summary>Boon (gun modes): while the fire key is held, the view locks onto the nearest visible enemy's head.</summary>
public class AimbotEffect : PlayerEffect
{
    public override string DisplayName => "Aimbot";
    public override string Description => "Hold fire: your aim snaps to heads";
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

    private void Update()
    {
        if (!IsLocalEffect || PlayerCam.Local == null || player.Gun == null || player.Gun.Current == null) return;
        if (!Input.GetKey(Keybinds.Get(InputAction.Action)) || PauseMenuManager.IsPaused) return;
        var mode = GameModeManager.Instance != null ? GameModeManager.Instance.Mode : null;
        var cam = PlayerCam.Local.Camera.transform;

        Vector3 best = Vector3.zero;
        float bestAngle = 55f;
        foreach (var p in Player.All)
        {
            if (p == null || p == player || p.IsDownAfterTag || p.Role == PlayerRole.Ghost) continue;
            if (mode != null && !mode.CanDamage(player.Role, p.Role)) continue;
            Vector3 head = new(p.transform.position.x, p.TopY - 0.35f, p.transform.position.z);
            Vector3 to = head - cam.position;
            float angle = Vector3.Angle(cam.forward, to);
            if (angle > bestAngle || to.magnitude > 120f) continue;
            if (Physics.Raycast(cam.position, to.normalized, out RaycastHit hit, to.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<Player>() != p) continue; // Hidden behind something
            bestAngle = angle;
            best = head;
        }
        if (best != Vector3.zero) PlayerCam.Local.Lure(best);
    }
}
