using UnityEngine;

public abstract class QuickAbility : Ability
{
    public float cooldownTime;
    private float lastUseTime;

    // --- Call to active child class ---
    public override void TryActivate(AbilityInputEvent inputEvent)
    {
        if (inputEvent != AbilityInputEvent.Down) return;
        if (!CanActivate()) return;
        OnKeyDown();
        lastUseTime = Time.time;
    }

    // --- Logic Gate ---
    public bool CanActivate()
    {
        return Time.time >= lastUseTime + cooldownTime;
    }

    // --- Cooldown Helper ---
    public float CooldownRemaining()
    {
        return Mathf.Max(0, (lastUseTime + cooldownTime) - Time.time);
    }

    // --- To be defined by children ---
    protected virtual void OnKeyDown() { }   
    //protected virtual void OnKeyUp() { }
}
