using UnityEngine;
using System;

/// <summary>
/// Three-phase match timer. Runs only on the master client; fires C# events for GameModeManager to react to.
/// Phases: Countdown (counts down) → Hiding (counts down) → Active (counts up, optional cap).
///
/// DisplayTime is updated each frame and reflects the local master client's timer value.
/// All display sync to other clients is handled by GameModeManager's RPCs.
/// </summary>
public class MatchTimerController : MonoBehaviour
{
    public event Action OnCountdownComplete;
    public event Action OnHidePhaseComplete;
    public event Action OnTimeLimitReached;

    [Header("Default Durations (overridden by room settings)")]
    [SerializeField] private float countdownDuration  = 10f;
    [SerializeField] private float hidePhaseDuration  = 60f;
    [SerializeField] private float activePhaseLimit   = 0f;  // 0 = no time limit

    // Exposed so GameModeManager can apply room settings before the first round
    public float HidePhaseDuration  { get => hidePhaseDuration;  set => hidePhaseDuration  = value; }
    public float ActivePhaseLimit   { get => activePhaseLimit;   set => activePhaseLimit   = value; }

    public enum Phase { Stopped, Countdown, Hiding, Active }
    public Phase CurrentPhase { get; private set; } = Phase.Stopped;

    /// <summary>
    /// Time remaining (Countdown/Hiding) or elapsed (Active). Updated on master client each frame.
    /// GameModeManager syncs this value to all clients on phase changes.
    /// </summary>
    public float DisplayTime { get; private set; }

    private float localTimer;
    private bool eventFired;  // Prevents duplicate event invocations when timer hits zero

    // -------------------------------------------------------------------------
    // Public control API
    // -------------------------------------------------------------------------

    public void StartCountdown()
    {
        SetPhase(Phase.Countdown, countdownDuration);
    }

    public void StartHidePhase()
    {
        SetPhase(Phase.Hiding, hidePhaseDuration);
    }

    public void StartActivePhase()
    {
        SetPhase(Phase.Active, 0f);
    }

    public void Stop()
    {
        CurrentPhase = Phase.Stopped;
    }

    // -------------------------------------------------------------------------
    // Unity Lifecycle
    // -------------------------------------------------------------------------

    private void Update()
    {
        if (CurrentPhase == Phase.Stopped) return;

        switch (CurrentPhase)
        {
            case Phase.Countdown:
                localTimer  -= Time.deltaTime;
                DisplayTime  = Mathf.Max(0f, localTimer);
                if (!eventFired && localTimer <= 0f)
                {
                    eventFired    = true;
                    CurrentPhase  = Phase.Stopped;
                    OnCountdownComplete?.Invoke();
                }
                break;

            case Phase.Hiding:
                localTimer  -= Time.deltaTime;
                DisplayTime  = Mathf.Max(0f, localTimer);
                if (!eventFired && localTimer <= 0f)
                {
                    eventFired    = true;
                    CurrentPhase  = Phase.Stopped;
                    OnHidePhaseComplete?.Invoke();
                }
                break;

            case Phase.Active:
                localTimer  += Time.deltaTime;
                DisplayTime  = localTimer;
                if (!eventFired && activePhaseLimit > 0f && localTimer >= activePhaseLimit)
                {
                    eventFired    = true;
                    CurrentPhase  = Phase.Stopped;
                    OnTimeLimitReached?.Invoke();
                }
                break;
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void SetPhase(Phase phase, float startValue)
    {
        CurrentPhase = phase;
        localTimer   = startValue;
        DisplayTime  = startValue;
        eventFired   = false;
    }
}
