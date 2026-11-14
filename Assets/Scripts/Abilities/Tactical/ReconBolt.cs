using UnityEngine;

/// <summary>Fire a bolt that sticks where it lands and scans twice: enemies it can see light up for you and your team. Can be shot down. Health modes only. Behaviour: AbilityObjects/Tactical/ReconBoltObject.</summary>
[AbilityInfo("Fire a bolt that sticks where it lands and pulses twice: enemies the pulse reaches light up for you and your team. One shot breaks it.", DisplayName = "Recon Bolt", HealthModesOnly = true)]
public class ReconBolt : ThrowAbility
{}
