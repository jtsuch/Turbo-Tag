/// <summary>Bouncy grapple: high springiness and a rope that starts slightly short, flinging the player. No reeling.</summary>
[AbilityInfo("Hold to fire a bouncy rope that flings you around.")]
public class SpringyGrapple : GrappleAbility
{
    protected override float InitialLengthFactor => 0.6f; // Was 0.8: a much stronger first pull (Nov 9)
}
