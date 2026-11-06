using UnityEngine;

/// <summary>
/// Base for held-key abilities with a stamina-style duration: it drains while the ability is
/// active, refills while idle, and forces the ability off when empty (e.g. grapples, glider).
/// Subclasses implement OnPress/OnRelease and <see cref="Deactivate"/>.
/// </summary>
public abstract class BasicAbility : Ability
{
    [Header("Duration")]
    [TunableParam("Max Duration", 0.5f, 30f)]
    [SerializeField] protected float maxDuration = 6f;
    [Tooltip("Meter refilled per second of rest, in seconds of use (1 = an empty meter takes as long to fill as it " +
             "lasts; 0.5 = twice as long, so strong movers can't be chained).")]
    [TunableParam("Refill Rate", 0.1f, 5f)]
    [SerializeField] protected float rechargeRate = 1f;

    public override AbilitySlot Slot => AbilitySlot.Basic;

    public float CurrentDuration { get; private set; }

    /// <summary>True while the ability is in use and draining its duration.</summary>
    protected bool IsActive { get; set; }

    public override float Readiness => MaxDuration <= 0f ? 1f : Mathf.Clamp01(CurrentDuration / MaxDuration);

    /// <summary>The full meter in seconds as set on the prefab, for descriptions.</summary>
    public float BaseDuration => maxDuration;

    /// <summary>The full meter in seconds (tunable per ability in Cheats).</summary>
    protected float MaxDuration => maxDuration;

    protected override void Awake()
    {
        base.Awake();
        CurrentDuration = maxDuration;
    }

    public override void HandleInput(AbilityInputEvent inputEvent)
    {
        if (inputEvent == AbilityInputEvent.Down && CurrentDuration > 0.01f) CountUse(); // Each press is a use
        if      (inputEvent == AbilityInputEvent.Down) OnPress();
        else if (inputEvent == AbilityInputEvent.Up)   OnRelease();
    }

    public override void Cancel() => Deactivate();

    protected virtual void FixedUpdate()
    {
        if (!IsLocal) return;

        if (IsActive)
        {
            // No Cooldowns (donut, or the cheats' cooldown at 0): the meter never runs down
            if (Draining && Owner.Modifiers.Get(Stat.Cooldown) > 0f) CurrentDuration -= Time.fixedDeltaTime;
            if (CurrentDuration <= 0.01f) Deactivate();
        }
        else
        {
            CurrentDuration = Mathf.Min(MaxDuration, CurrentDuration + Time.fixedDeltaTime * Mathf.Max(0.01f, rechargeRate));
        }
    }

    public override void RefillNow()
    {
        base.RefillNow();
        CurrentDuration = MaxDuration;
    }

    /// <summary>While active, whether the meter runs down this step (Wall Magnet rests while standing still).</summary>
    protected virtual bool Draining => true;

    protected virtual void OnPress() { }
    protected virtual void OnRelease() => Deactivate();

    /// <summary>Turns the ability off and restores anything it changed. Must be idempotent.</summary>
    protected abstract void Deactivate();
}
