using UnityEngine;

/// <summary>Boon (escape): a big smoke cloud where you stand and a burst of speed out of it.</summary>
public class SmokeBombEffect : PlayerEffect
{
    public override string DisplayName => "Smoke Bomb";
    public override string Description => "A cloud, and a quick dash";
    public override bool   IsGood      => true;
    public override bool   IsChaos     => true;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        if (player.TryGetComponent(out DonutNet net)) net.Smoke();
        Vector3 ahead = Vector3.ProjectOnPlane(PlayerCam.Local != null ? PlayerCam.Local.Camera.transform.forward : player.transform.forward, Vector3.up).normalized;
        WorldFx.Push(player.Body, ahead * 22f + Vector3.up * 4f, ForceMode.VelocityChange);
    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;

    }
}
