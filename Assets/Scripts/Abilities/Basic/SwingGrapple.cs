using UnityEngine;

/// <summary>
/// Web Shooter (class kept as SwingGrapple: loadouts store the id): a plain tether in white, like a web. The rope
/// stays the length it had when it attached, so the player swings from the anchor. No reeling in or out.
/// </summary>
[AbilityInfo("Hold to shoot a web and swing from anything you hit.", DisplayName = "Web Shooter")]
public class SwingGrapple : GrappleAbility
{
    protected override Color? RopeColor => Color.white;
}
