using UnityEngine;

public abstract class ThrowAbility : Ability
{
    public float cooldownTime;
    private float lastUseTime;

    // --- Call to active child class ---
    public override void TryActivate(bool down)
    {
        if (down && CanActivate()) // If cooldown is okay and key is pressed down
        {
            OnKeyDown();
            lastUseTime = Time.time;
        }
        else
        {
            Debug.Log($"{abilityName} is on cooldown ({CooldownRemaining():0.0}s left)");
        }
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
