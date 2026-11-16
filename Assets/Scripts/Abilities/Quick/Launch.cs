using UnityEngine;

/// <summary>
/// Launches the player straight up. Downward velocity is cancelled first so the full impulse
/// always applies.
/// </summary>
[AbilityInfo("Launches you straight up into the sky.")]
public class Launch : QuickAbility
{
    // What every screen plays when this is used (each ability owns its own: Ability.PlayUseFx)
    public override void PlayUseFx(Player player, Vector3 feet, Vector3 chest, bool mine)
    {
        Sfx.At(SfxKind.Boing, feet, 1f);
        Sfx.At(SfxKind.Whoosh, chest, 0.7f);
        Vfx.Ring(feet, Fx.Dust, 26, 8f, 0.6f, 0.6f);
        Vfx.Burst(feet, Fx.Yellow, 14, 5f, 0.22f, 0.5f, direction: Vector3.down, spread: 0.7f);
        if (mine) Fx.Shake(0.25f);
    }

    [Header("Launch")]
    [TunableParam("Launch Force", 5f, 300f)]
    [SerializeField] private float launchForce = 20f;

    protected override void Activate()
    {
        Vector3 velocity = Body.linearVelocity;
        if (velocity.y < 0f) velocity.y = 0f;
        Body.linearVelocity = velocity;

        Body.AddForce(Vector3.up * launchForce * Mathf.Sqrt(Owner.AbilityPower), ForceMode.VelocityChange);
    }
}
