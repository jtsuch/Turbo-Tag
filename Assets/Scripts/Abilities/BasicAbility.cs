using UnityEngine;

/// <summary>
/// Base for toggle-style abilities with a finite duration that drains while active and
/// regenerates while inactive (e.g. a glider or speed boost with a stamina bar).
/// Subclass this and override OnKeyDown/OnKeyUp/StopAbility for concrete behaviour.
/// Attach to: ThePlayer prefab — alongside AbilityHandler.
/// </summary>
public abstract class BasicAbility : Ability
{
    // ─── Duration ─────────────────────────────────────────────────────────────
    public float maxDuration;
    public float currentDuration;
    public bool isActive = false;

    public void Start()
    {
        currentDuration = maxDuration;
    }

    public override void TryActivate(AbilityInputEvent inputEvent)
    {
        if (PauseMenuManager.Instance.Paused) return;
        if (inputEvent == AbilityInputEvent.Down) OnKeyDown();
        else if (inputEvent == AbilityInputEvent.Up) OnKeyUp();
    }

    private void FixedUpdate()
    {
        // Drain while active; refill while inactive (capped at maxDuration)
        if (isActive)
        {
            currentDuration -= Time.deltaTime;
            if (currentDuration <= 0.01f)
                StopAbility();
        }
        else
        {
            currentDuration += Time.deltaTime;
            if (currentDuration >= maxDuration)
                currentDuration = maxDuration;
        }
    }

    // ─── Hooks for subclasses ─────────────────────────────────────────────────
    protected virtual void OnKeyDown() { }
    protected virtual void OnKeyUp() { }
    protected virtual void StopAbility() { }
}
