/// <summary>Bouncy grapple: high springiness and a rope that starts slightly short, flinging the player. No reeling.</summary>
[AbilityInfo("Hold to fire a bouncy rope that flings you around.")]
public class SpringyGrapple : GrappleAbility
{
    protected override float InitialLengthFactor => 0.8f;
}
