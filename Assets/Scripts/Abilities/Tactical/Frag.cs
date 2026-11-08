using UnityEngine;

/// <summary>A bouncy grenade: goes off after 2 s, up to 85 damage close in. Hurts you and your team too. Health modes only. Behaviour: AbilityObjects/Tactical/FragObject.</summary>
[AbilityInfo("A bouncy grenade: goes off after 2 s, up to 85 damage close in. Hurts you and your team too.", HealthModesOnly = true)]
public class Frag : ThrowAbility
{}
