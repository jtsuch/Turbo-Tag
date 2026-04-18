using UnityEngine;
using System.Collections;

/// <summary>
/// Abstract base for all temporary player effects. Added to a player's GameObject at runtime
/// by EffectBlock.ApplyEffect() via AddComponent; never placed in the scene directly.
/// Manages its own duration coroutine and guarantees OnEffectEnd runs exactly once,
/// whether the effect expires naturally, is cancelled early, or the component is destroyed.
///
/// To add a new effect:
///  1. Subclass PlayerEffect and implement OnEffectStart() / OnEffectEnd().
///  2. Override Update() if the effect needs per-frame behaviour (e.g. input polling).
///  3. Add the type to EffectBlock.EffectType and GetEffectSystemType().
/// Attach to: ThePlayer prefab (dynamically at runtime) — do not add in the Inspector.
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
