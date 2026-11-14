using System.Collections;
using UnityEngine;

/// <summary>
/// Shrinks the player for a while (smaller collider, body and camera height), optionally
/// changing speed. Implemented as stat modifiers, so it stacks cleanly with cheats and effects
/// and the size change is synced to other players by Player. Pressing the key again while small grows
/// back early (the cooldown still runs from when it was used).
/// </summary>
[AbilityInfo("Shrink down for 6 seconds. Smaller, harder to spot, and a little faster. Press again to grow back early.")]
public class Shrink : QuickAbility
{
    [Header("Shrink")]
    [TunableParam("Scale", 0.05f, 3f, HardMin = 0.01f)]
    [SerializeField] private float shrinkScale = 0.125f;
    [TunableParam("Duration", 1f, 60f)]
    [SerializeField] private float shrinkDuration = 6f;
    [Tooltip("Speed multiplier while shrunk (1 = unchanged). Above 1 so a shrunk player can still win a foot chase.")]
    [TunableParam("Speed Multiplier", 0.1f, 3f)]
    [SerializeField] private float speedMultiplier = 1.25f;

    private Coroutine shrinkRoutine;

    private bool IsShrunk => shrinkRoutine != null;

    // Pressing again while shrunk grows back early. The cooldown (started when shrinking) still applies
    public override void HandleInput(AbilityInputEvent inputEvent)
    {
        if (inputEvent == AbilityInputEvent.Down && IsShrunk)
        {
            StopCoroutine(shrinkRoutine);
            Revert();
            return;
        }
        base.HandleInput(inputEvent);
    }

    protected override void Activate()
    {
        if (shrinkRoutine != null) StopCoroutine(shrinkRoutine);
        shrinkRoutine = StartCoroutine(ShrinkRoutine());
    }

    private IEnumerator ShrinkRoutine()
    {
        // S&D upgrades: 50% → 40% → 30% size (UpgradePower 0.6 / 0.75 / 1)
        Owner.Modifiers.Set(Stat.Size,  this, Mathf.Min(1f, shrinkScale / Mathf.Max(0.1f, UpgradePower)));
        Owner.Modifiers.Set(Stat.Speed, this, speedMultiplier);
        yield return new WaitForSeconds(shrinkDuration);
        Revert();
    }

    // Shrink persists through pauses and aiming; it only ends early when disabled
    protected override void OnDisable()
    {
        base.OnDisable();
        if (shrinkRoutine != null) StopCoroutine(shrinkRoutine);
        Revert();
    }

    private void Revert()
    {
        shrinkRoutine = null;
        if (Owner == null) return;
        Owner.Modifiers.Remove(Stat.Size,  this);
        Owner.Modifiers.Remove(Stat.Speed, this);
    }
}
