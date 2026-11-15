/// <summary>One-off: every ability's charges and meter back at once. The card just stays up a moment.</summary>
public class RefillEffect : PlayerEffect
{
    public override string DisplayName => "Refill";
    public override string Description => "Every ability ready";
    public override bool   IsChaos     => true;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect || player.Abilities == null) return;
        foreach (var ability in player.GetComponents<Ability>()) ability.RefillNow();
        Fx.Heal(player.transform.position + UnityEngine.Vector3.up, true);
    }

    protected override void OnEffectEnd() { }
}
