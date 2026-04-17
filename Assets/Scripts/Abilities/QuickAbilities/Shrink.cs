using UnityEngine;
using System.Collections;

/// <summary>
/// QuickAbility: shrinks the player to 1/8 their normal size for a configurable duration.
/// Stores original scale and height before shrinking and restores them on revert or disable.
/// An optional SpeedMultiplier is applied while shrunk (defaults to 1 — no change).
///
/// Unity setup:
///  - Add this component to the player prefab alongside other abilities.
///  - Tune shrinkScale, shrinkDuration, shrinkCooldown, and speedMultiplier in the Inspector.
/// </summary>
[RequireComponent(typeof(Player))]
public class Shrink : QuickAbility
{
    [Header("Shrink Settings")]
    [SerializeField] private float shrinkScale      = 0.125f;  // 1/8 size
    [SerializeField] private float shrinkDuration   = 10f;
    [SerializeField] private float shrinkCooldown   = 15f;
    [SerializeField] private float speedMultiplier  = 1f;      // SpeedMultiplier while shrunk; 1 = no change

    private Player     player;
    private Coroutine  shrinkCoroutine;
    private bool       isShrunk = false;

    // Originals captured at shrink time
    private float origX;
    private float origY;
    private float origZ;
    private float origHeight;
    private float origSpeedMultiplier;

    protected override void Awake()
    {
        base.Awake();
        cooldownTime = shrinkCooldown;
        player       = GetComponent<Player>();
    }

    private void OnDisable()
    {
        if (shrinkCoroutine != null)
        {
            StopCoroutine(shrinkCoroutine);
            shrinkCoroutine = null;
        }
        if (isShrunk) RevertShrink();
    }

    // -------------------------------------------------------------------------
    // QuickAbility
    // -------------------------------------------------------------------------

    protected override void OnKeyDown()
    {
        if (player == null || isShrunk) return;

        if (shrinkCoroutine != null) StopCoroutine(shrinkCoroutine);
        shrinkCoroutine = StartCoroutine(ShrinkRoutine());
    }

    // -------------------------------------------------------------------------
    // Coroutine
    // -------------------------------------------------------------------------

    private IEnumerator ShrinkRoutine()
    {
        ApplyShrink();
        yield return new WaitForSeconds(shrinkDuration);
        RevertShrink();
    }

    private void ApplyShrink()
    {
        isShrunk = true;

        // Capture originals
        origX               = player.currentXScale;
        origY               = player.currentYScale;
        origZ               = player.currentZScale;
        origHeight          = player.height;
        origSpeedMultiplier = player.SpeedMultiplier;

        // Apply shrink
        player.currentXScale = origX * shrinkScale;
        player.currentYScale = origY * shrinkScale;
        player.currentZScale = origZ * shrinkScale;
        player.SetPlayerScale();

        player.height          = origHeight * shrinkScale;
        player.SpeedMultiplier = speedMultiplier;
    }

    private void RevertShrink()
    {
        if (!isShrunk) return;
        isShrunk        = false;
        shrinkCoroutine = null;

        player.currentXScale   = origX;
        player.currentYScale   = origY;
        player.currentZScale   = origZ;
        player.SetPlayerScale();

        player.height          = origHeight;
        player.SpeedMultiplier = origSpeedMultiplier;
    }
}
