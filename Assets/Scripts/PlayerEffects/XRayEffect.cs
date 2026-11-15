/// <summary>
/// Boon: every other player shows through walls on this screen (blue for teammates, red for enemies), drawn by
/// <see cref="PlayerAppearance"/> as an extra see-through pass while <see cref="PlayerAppearance.XRay"/> is on.
/// </summary>
public class XRayEffect : PlayerEffect
{
    public override string DisplayName => "X-Ray Vision";
    public override string Description => "See everyone through walls";

    protected override void OnEffectStart()
    {
        if (IsLocalEffect) PlayerAppearance.XRay = true;
    }

    protected override void OnEffectEnd()
    {
        if (IsLocalEffect) PlayerAppearance.XRay = false;
    }
}
