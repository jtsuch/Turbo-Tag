using UnityEngine;

public abstract class BasicAbility : Ability
{
    public float maxDuration;
    public float currentDuration;
    public bool isActive = false;

    public void Start()
    {
        currentDuration = maxDuration;
    }

    // --- Call to active child class ---
    public override void TryActivate(AbilityInputEvent inputEvent)
    {
        if (PauseMenuManager.Instance.Paused) return;
        if (inputEvent == AbilityInputEvent.Down) OnKeyDown();
        else if (inputEvent == AbilityInputEvent.Up) OnKeyUp();
    }

    private void FixedUpdate()
    {
        // Decrement duration while gliding, increment otherwise
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

    // --- To be defined by children ---
    protected virtual void OnKeyDown() { }   
    protected virtual void OnKeyUp() { }
    protected virtual void StopAbility() { }
}
