using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base for temporary player effects (donut pickups, pickup blocks). Added to a player's GameObject at runtime
/// by <see cref="Apply"/>; never placed in the scene directly. Runs its own timer and guarantees OnEffectEnd runs
/// exactly once, whether it expires, is cancelled early (picked up again: the timer restarts) or is destroyed.
/// The local player's running effects are listed in <see cref="Active"/>, which the HUD's effect tray shows on the
/// right side of the screen with <see cref="DisplayName"/>, <see cref="IsGood"/> and the time left.
///
/// To add a new effect:
///  1. Subclass PlayerEffect: OnEffectStart() / OnEffectEnd(), a DisplayName, IsGood and Description.
///  2. Override Update() if it needs per-frame behaviour (input polling).
///  3. Add it to a pool: <see cref="Donut"/>'s list (with a duration and weight) or EffectBlock.
/// </summary>
public abstract class PlayerEffect : MonoBehaviour
{
    /// <summary>The local player's effects that are running now (for the HUD tray).</summary>
    public static readonly List<PlayerEffect> Active = new();

    /// <summary>A local effect started (the tray pops a card in).</summary>
    public static event Action<PlayerEffect> Started;

    public float Duration  { get; private set; }
    public float StartTime { get; private set; }

    /// <summary>Seconds left (0 when timeless or over).</summary>
    public float Remaining => Duration > 0f ? Mathf.Max(0f, Duration - (Time.time - StartTime)) : 0f;

    /// <summary>Name on the HUD card ("Double Jump").</summary>
    public virtual string DisplayName => GetType().Name.Replace("Effect", "");

    /// <summary>One line under the name ("Jump again in mid-air").</summary>
    public virtual string Description => "";

    /// <summary>Green card (a boon) or red card (a curse).</summary>
    public virtual bool IsGood => true;

    /// <summary>Yellow card: a chaos effect that hits everyone, or a one-off (swap, refill).</summary>
    public virtual bool IsChaos => false;

    protected Player player;

    private Coroutine durationCoroutine;
    private bool      isEnded;

    /// <summary>True only if this effect is on the local player's client.</summary>
    protected bool IsLocalEffect => player != null && player.IsLocal;

    // ─── Applying ────────────────────────────────────────────────────────────

    /// <summary>
    /// Gives <paramref name="player"/> the effect <paramref name="effectType"/> for <paramref name="duration"/> seconds.
    /// Already has it: the old one ends and a fresh one starts (the timer restarts).
    /// </summary>
    public static PlayerEffect Apply(Type effectType, Player player, float duration)
    {
        if (player == null || effectType == null || !typeof(PlayerEffect).IsAssignableFrom(effectType)) return null;
        if (player.GetComponent(effectType) is PlayerEffect existing)
        {
            existing.CancelEffect();
            Destroy(existing);
        }
        var effect = (PlayerEffect)player.gameObject.AddComponent(effectType);
        effect.Initialize(player, duration);
        return effect;
    }

    /// <summary>Ends every effect on <paramref name="player"/> (e.g. leaving a level).</summary>
    public static void ClearAll(Player player)
    {
        if (player == null) return;
        foreach (var effect in player.GetComponents<PlayerEffect>())
        {
            effect.CancelEffect();
            Destroy(effect);
        }
    }

    // ─── Lifecycle API ───────────────────────────────────────────────────────

    /// <summary>Starts the effect (called by <see cref="Apply"/>, or EffectBlock after AddComponent).</summary>
    public void Initialize(Player targetPlayer, float duration)
    {
        player    = targetPlayer;
        Duration  = duration;
        StartTime = Time.time;

        OnEffectStart();
        if (IsLocalEffect)
        {
            Active.Add(this);
            Started?.Invoke(this);
        }

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
        Active.Remove(this);

        if (durationCoroutine != null)
        {
            StopCoroutine(durationCoroutine);
            durationCoroutine = null;
        }

        OnEffectEnd();
    }

    // Ensures cleanup runs even if the component is removed externally (e.g. scene change)
    private void OnDestroy() => CancelEffect();

    // ─── Abstract hooks ──────────────────────────────────────────────────────

    /// <summary>Called once when the effect is first applied.  Store originals here.</summary>
    protected abstract void OnEffectStart();

    /// <summary>Called once when the effect expires or is cancelled.  Restore originals here.</summary>
    protected abstract void OnEffectEnd();

    // ─── Internal ────────────────────────────────────────────────────────────

    private IEnumerator DurationRoutine()
    {
        yield return new WaitForSeconds(Duration);
        CancelEffect();
        Destroy(this);
    }
}
