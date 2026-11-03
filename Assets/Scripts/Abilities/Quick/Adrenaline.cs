using UnityEngine;

/// <summary>Speeds the player up for a few seconds (a timed stat change on <see cref="Player"/>).</summary>
[AbilityInfo("A rush of adrenaline: 75% faster for 5 seconds.")]
public class Adrenaline : QuickAbility
{
    [Header("Adrenaline")]
    [TunableParam("Speed Multiplier", 1f, 3f)]
    [SerializeField] private float speedMultiplier = 1.75f;
    [TunableParam("Duration", 1f, 30f)]
    [SerializeField] private float duration = 5f;

    protected override void Activate() =>
        Owner.ApplyTimedModifier(Stat.Speed, nameof(Adrenaline), speedMultiplier,
                                 duration * Owner.Modifiers.Get(Stat.AbilityDuration));
}
