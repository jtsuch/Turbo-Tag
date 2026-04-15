using Photon.Pun;
using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(PhotonView))]
public class AbilityHandler : MonoBehaviour
{
    private Ability[] abilities;
    private Dictionary<string, Ability> abilityMap;
    private Ability activeAbility; // Tracks whichever ability is mid-flow

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

    // Returns true when two abilities should block each other.
    // Throw and Trap are mutually exclusive; Basic and Quick can overlap freely.
    private static bool Conflicts(Ability a, Ability b)
    {
        static bool isBlockingType(Ability x) =>
            x.abilityType == Ability.AbilityType.Throw ||
            x.abilityType == Ability.AbilityType.Trap;
        return isBlockingType(a) && isBlockingType(b);
    }

    public void TryUseAbility(string abilityName, AbilityInputEvent inputEvent)
    {
        if (!abilityMap.TryGetValue(abilityName, out Ability ability)) return;

        // If a conflicting ability is already active, cancel it before starting the new one
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

        // Track this as the active ability if it entered an active state
        if (ability.IsAwaitingAction)
            activeAbility = ability;
        else if (activeAbility == ability)
            activeAbility = null; // It finished or was cancelled
    }

    public void TryConfirmAction()
    {
        if (activeAbility != null && activeAbility.IsAwaitingAction)
            activeAbility.OnActionConfirm();
    }

    public void TryConfirmActionUp()
    {
        if (activeAbility != null && activeAbility.IsAwaitingAction)
        {
            activeAbility.OnActionConfirmUp();
            // Clear active if it's no longer awaiting after confirm
            if (!activeAbility.IsAwaitingAction)
                activeAbility = null;
        }
    }
}