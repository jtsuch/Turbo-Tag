using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Base for instant abilities with a cooldown (dash, launch, shrink). Fires on key-down only
/// and never waits for the Action key, so it can be used while aiming a throw.
/// Subclasses implement <see cref="Activate"/>.
/// </summary>
public abstract class QuickAbility : Ability
{
    [Header("Cooldown")]
    [FormerlySerializedAs("dashCooldown")]
    [FormerlySerializedAs("launchCooldown")]
    [FormerlySerializedAs("shrinkCooldown")]
    [SerializeField] protected float cooldown = 3f;

    public override AbilitySlot Slot         => AbilitySlot.Quick;
    public override float       BaseCooldown => cooldown;

    public override void HandleInput(AbilityInputEvent inputEvent)
    {
        if (inputEvent != AbilityInputEvent.Down || !IsReady) return;
        Activate();
        StartCooldown();
    }

    protected abstract void Activate();
}
