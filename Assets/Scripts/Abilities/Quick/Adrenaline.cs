using UnityEngine;

/// <summary>Speeds the player up for a few seconds (a timed stat change on <see cref="Player"/>).</summary>
[AbilityInfo("A rush of adrenaline: 75% faster for 3 seconds.")]
public class Adrenaline : QuickAbility
{
    // Search & Destroy upgrade path: the value at levels 0, 1 and 2 (AbilityUpgrades reads it; Justin's table, Nov 14)
    private static readonly AbilityUpgrades.Entry Upgrades = AbilityUpgrades.Power("speed", 2f / 3f, 1f, 4f / 3f, v => $"+{v * 75f:0}%");
    public override AbilityUpgrades.Entry UpgradePath => Upgrades;
    // What every screen plays when this is used (each ability owns its own: Ability.PlayUseFx)
    public override void PlayUseFx(Player player, Vector3 feet, Vector3 chest, bool mine)
    {
        Sfx.At(SfxKind.Heartbeat, chest, 1f);
        Vfx.Burst(chest, Fx.Pink, 16, 3.5f, 0.25f, 0.5f);
        if (mine) Fx.Shake(0.1f);
    }

    [Header("Adrenaline")]
    [TunableParam("Speed Multiplier", 1f, 3f)]
    [SerializeField] private float speedMultiplier = 1.75f;
    [TunableParam("Duration", 1f, 30f)]
    [SerializeField] private float duration = 3f;

    protected override void Activate()
    {
        // S&D upgrades scale the bonus (+50% → +75% → +100%)
        float multiplier = 1f + (speedMultiplier - 1f) * UpgradePower;
        Owner.ApplyTimedModifier(Stat.Speed, nameof(Adrenaline), multiplier, duration);
        PlayerEffect.Apply(typeof(SwiftEffect), Owner, duration); // The "Swift" card at the side
    }
}

/// <summary>Boon (Adrenaline): Swift, moving faster. The card only: the speed is a timed stat change.</summary>
public class SwiftEffect : PlayerEffect
{
    public override string DisplayName => "Swift";
    public override string Description => "Moving faster";

    protected override void OnEffectStart() { }
    protected override void OnEffectEnd()   { }
}
