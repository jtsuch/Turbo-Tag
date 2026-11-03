using System.Collections;
using UnityEngine;

/// <summary>
/// Shrinks the player for a while (smaller collider, body and camera height), optionally
/// changing speed. Implemented as stat modifiers, so it stacks cleanly with cheats and effects
/// and the size change is synced to other players by Player.
/// </summary>
[AbilityInfo("Shrink down for 10 seconds. Smaller, harder to spot, and a little faster.")]
public class Shrink : QuickAbility
{
    [Header("Shrink")]
    [TunableParam("Scale", 0.05f, 3f, HardMin = 0.01f)]
    [SerializeField] private float shrinkScale = 0.125f;
    [TunableParam("Duration", 1f, 60f)]
    [SerializeField] private float shrinkDuration = 10f;
    [Tooltip("Speed multiplier while shrunk (1 = unchanged). Above 1 so a shrunk player can still win a foot chase.")]
    [TunableParam("Speed Multiplier", 0.1f, 3f)]
    [SerializeField] private float speedMultiplier = 1.25f;

    private Coroutine shrinkRoutine;

    protected override void Activate()
    {
        if (shrinkRoutine != null) StopCoroutine(shrinkRoutine);
        shrinkRoutine = StartCoroutine(ShrinkRoutine());
    }

    private IEnumerator ShrinkRoutine()
    {
        Owner.Modifiers.Set(Stat.Size,  this, shrinkScale);
        Owner.Modifiers.Set(Stat.Speed, this, speedMultiplier);
        yield return new WaitForSeconds(shrinkDuration * Owner.Modifiers.Get(Stat.AbilityDuration));
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
