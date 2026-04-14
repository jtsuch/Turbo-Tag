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

    public void TryUseAbility(string abilityName, AbilityInputEvent inputEvent)
    {
        if (!abilityMap.TryGetValue(abilityName, out Ability ability)) return;

        // If a different ability is already active and this is a fresh activation,
        // cancel the current one before proceeding
        if (inputEvent == AbilityInputEvent.Down
            && activeAbility != null
            && activeAbility != ability
            && activeAbility.IsAwaitingAction)
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