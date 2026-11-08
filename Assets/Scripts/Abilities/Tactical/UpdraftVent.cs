using UnityEngine;

/// <summary>Place a vent that blows anyone standing on it high into the air (20 s): reach an angle no one expects. Health modes only. Behaviour: AbilityObjects/Tactical/UpdraftVentObject.</summary>
[AbilityInfo("Place a vent that blows anyone standing on it high into the air (20 s): reach an angle no one expects.", DisplayName = "Updraft Vent", HealthModesOnly = true)]
public class UpdraftVent : TrapAbility
{
    // On floors, facing where you look (walls and the snow curtain stand across your view)
    protected override bool IsValidSurface(Vector3 normal) => normal.y > 0.7f;
    protected override Quaternion PlacementRotation(RaycastHit hit, Transform view) => Quaternion.Euler(0f, view.eulerAngles.y, 0f);
}
