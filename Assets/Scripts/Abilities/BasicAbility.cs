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
    public override void TryActivate(bool down)
    {
        if (PauseMenuManager.Instance.Paused) return;
        if (down)
        {
            HUDManager.Instance.timer.SetActive(true);
            OnKeyDown();    
        }
        else
        {
            OnKeyUp();
        }
    }

    private void FixedUpdate()
    {
        // Decrement duration while gliding, increment otherwise
        if (isActive)
        {
            currentDuration -= Time.deltaTime;
            if (currentDuration <= 0.01f)
            {
                HUDManager.Instance.yellowCircle.fillAmount = 0;
                StopAbility();
            }
            else
                HUDManager.Instance.yellowCircle.fillAmount = currentDuration / maxDuration;
        }
        else
        {
            currentDuration += Time.deltaTime;
            if (currentDuration >= maxDuration) 
            {
                currentDuration = maxDuration;
                HUDManager.Instance.timer.SetActive(false);
            }
            else
            {
                HUDManager.Instance.yellowCircle.fillAmount = currentDuration / maxDuration;
            }
        }
    }

    // --- To be defined by children ---
    protected virtual void OnKeyDown() { }   
    protected virtual void OnKeyUp() { }
    protected virtual void StopAbility() { }
}
