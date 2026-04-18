using Photon.Pun;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Mediates between InputHandler and the Ability components on the same GameObject.
/// Maintains a dictionary of abilities by name, routes input events, and enforces the
/// one-active-ability-at-a-time rule for conflicting ability types (Throw/Trap).
/// Attach to: ThePlayer prefab — alongside all Ability components.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class AbilityHandler : MonoBehaviour
{
    // ─── State ────────────────────────────────────────────────────────────────
    private Ability[] abilities;
    private Dictionary<string, Ability> abilityMap;
    private Ability activeAbility; // The ability currently awaiting a confirm/cancel action

    void Awake()
    {
        abilities = GetComponents<Ability>();
        abilityMap = new Dictionary<string, Ability>();
        foreach (var ability in abilities)
        {
            if (!abilityMap.ContainsKey(ability.abilityName))
                abilityMap[ability.abilityName] = ability;
        }
    }

    // ─── Conflict Detection ───────────────────────────────────────────────────

    // Throw and Trap are mutually exclusive (both require aim/placement mode).
    // Basic and Quick are fire-and-forget so they never need to block each other.
    private static bool Conflicts(Ability a, Ability b)
    {
        static bool isBlockingType(Ability x) =>
            x.abilityType == Ability.AbilityType.Throw ||
            x.abilityType == Ability.AbilityType.Trap;
        return isBlockingType(a) && isBlockingType(b);
    }

    // ─── Input Routing ────────────────────────────────────────────────────────

    public void TryUseAbility(string abilityName, AbilityInputEvent inputEvent)
    {
        if (!abilityMap.TryGetValue(abilityName, out Ability ability)) return;

        // Cancel the conflicting active ability before starting the new one
        if (inputEvent == AbilityInputEvent.Down
            && activeAbility != null
            && activeAbility != ability
            && activeAbility.IsAwaitingAction
            && Conflicts(activeAbility, ability))
        {
            activeAbility.OnActionCancel();
            activeAbility = null;
        }

        ability.TryActivate(inputEvent);

        // Update tracking: set or clear activeAbility based on the ability's new state
        if (ability.IsAwaitingAction)
            activeAbility = ability;
        else if (activeAbility == ability)
            activeAbility = null;
    }

    // Called on Action key down — forwards to whichever ability is mid-flow (e.g. charge a throw)
    public void TryConfirmAction()
    {
        if (activeAbility != null && activeAbility.IsAwaitingAction)
            activeAbility.OnActionConfirm();
    }

    // Called on Action key up — releases (e.g. executes a charged throw)
    public void TryConfirmActionUp()
    {
        if (activeAbility != null && activeAbility.IsAwaitingAction)
        {
            activeAbility.OnActionConfirmUp();
            if (!activeAbility.IsAwaitingAction)
                activeAbility = null;
        }
    }
}