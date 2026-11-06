using UnityEngine;

/// <summary>Boon: hold the Jump key to fly upward (flames on every screen).</summary>
public class JetpackEffect : PlayerEffect
{
    public override string DisplayName => "Jetpack";
    public override string Description => "Hold Jump to fly";
    public override bool   IsGood      => true;
    public override bool   IsChaos     => false;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        player.SetFlag(PlayerFlag.Jetpack, true);
    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;
        player.SetFlag(PlayerFlag.Jetpack, false);
    }

    private void FixedUpdate()
    {
        if (!IsLocalEffect || player.IsCaught || PauseMenuManager.IsPaused) return;
        if (!Input.GetKey(Keybinds.Get(InputAction.Jump))) return;
        Vector3 v = player.Body.linearVelocity;
        if (v.y < 9f) player.Body.AddForce(Vector3.up * 75f, ForceMode.Acceleration); // Beats gravity (40) with room to climb
        player.NotePushed();
    }
}
