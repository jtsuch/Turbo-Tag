using UnityEngine;

/// <summary>
/// Marks a Pregame Toggle with the ability it selects. The ability's slot is worked out from
/// its class, so toggles only need the ability's class name (e.g. "SpringyGrapple").
/// </summary>
public class AbilityToggle : MonoBehaviour
{
    public string abilityName;
}
