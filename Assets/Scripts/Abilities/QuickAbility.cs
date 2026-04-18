using UnityEngine;

/// <summary>
/// Base for instant-fire abilities with a simple cooldown (e.g. a dash or a stun).
/// Only reacts to key-down; never enters an awaiting state, so it never blocks other abilities.
/// Subclass this and override OnKeyDown for concrete behaviour.
/// Attach to: ThePlayer prefab — alongside AbilityHandler.
/// </summary>
public abstract class QuickAbility : Ability
{
    // ─── Cooldown ─────────────────────────────────────────────────────────────
    public float cooldownTime;
    private float lastUseTime;

    public override void TryActivate(AbilityInputEvent inputEvent)
    {
        if (inputEvent != AbilityInputEvent.Down) return;
        if (!CanActivate()) return;
        OnKeyDown();
        lastUseTime = Time.time;
    }

    public bool CanActivate() => Time.time >= lastUseTime + cooldownTime;

    /// <summary>Seconds until this ability can be used again; 0 if ready.</summary>
    public float CooldownRemaining() => Mathf.Max(0, (lastUseTime + cooldownTime) - Time.time);

    // ─── Hook for subclasses ──────────────────────────────────────────────────
    protected virtual void OnKeyDown() { }
}
