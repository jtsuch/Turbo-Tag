using UnityEngine;
using System.Collections;

/// <summary>
/// Abstract base class for all temporary player effects.
/// Attach to a player's GameObject via AddComponent from EffectBlock.
/// Manages its own duration coroutine and guarantees OnEffectEnd runs exactly once.
///
/// To add a new effect:
///  1. Subclass PlayerEffect.
///  2. Implement OnEffectStart() and OnEffectEnd().
///  3. Override Update() if the effect needs per-frame behaviour (e.g. input polling).
///  4. Register the type in EffectBlock.CreateEffect().
/// </summary>
public abstract class PlayerEffect : MonoBehaviour
{
    public float Duration    { get; private set; }
    protected Player player;

    private Coroutine durationCoroutine;
    private bool      isEnded = false;

    /// <summary>True only if this effect is on the local player's client.</summary>
    protected bool IsLocalEffect => player != null && player.IsLocalPlayer;

    // -------------------------------------------------------------------------
    // Lifecycle API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called by EffectBlock after AddComponent to start the effect.
    /// </summary>
    public void Initialize(Player targetPlayer, float duration)
    {
        player   = targetPlayer;
        Duration = duration;

        OnEffectStart();

        if (duration > 0f)
            durationCoroutine = StartCoroutine(DurationRoutine());
    }

    /// <summary>
    /// Cleanly cancels the effect before its natural expiry.
    /// Safe to call multiple times — only the first call takes effect.
    /// </summary>
    public void CancelEffect()
    {
        if (isEnded) return;
        isEnded = true;

        if (durationCoroutine != null)
        {
            StopCoroutine(durationCoroutine);
            durationCoroutine = null;
        }

        OnEffectEnd();
    }

    // Ensures cleanup runs even if the component is removed externally (e.g. scene change)
    private void OnDestroy() => CancelEffect();

    // -------------------------------------------------------------------------
    // Abstract hooks
    // -------------------------------------------------------------------------

    /// <summary>Called once when the effect is first applied.  Store originals here.</summary>
    protected abstract void OnEffectStart();

    /// <summary>Called once when the effect expires or is cancelled.  Restore originals here.</summary>
    protected abstract void OnEffectEnd();

    // -------------------------------------------------------------------------
    // Internal
    // -------------------------------------------------------------------------

    private IEnumerator DurationRoutine()
    {
        yield return new WaitForSeconds(Duration);
        CancelEffect();
        Destroy(this);
    }
}
