using UnityEngine;

/// <summary>Effect: multiplies movement speed for the duration.</summary>
public class AdrenalineEffect : PlayerEffect
{
    [Tooltip("Speed multiplier stacked on top of any other speed modifiers.")]
    [SerializeField] private float speedMultiplier = 1.5f;

    protected override void OnEffectStart()
    {
        if (IsLocalEffect) player.Modifiers.Set(Stat.Speed, this, speedMultiplier);
    }

    protected override void OnEffectEnd()
    {
        if (player != null) player.Modifiers.Remove(Stat.Speed, this);
    }
}
