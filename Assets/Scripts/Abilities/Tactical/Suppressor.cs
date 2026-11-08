using UnityEngine;

/// <summary>Place a device that stops enemies near it from using abilities (8 s). Health modes only. Behaviour: AbilityObjects/Tactical/SuppressorObject.</summary>
[AbilityInfo("Place a device that stops enemies near it from using abilities (8 s).", DisplayName = "Suppression", HealthModesOnly = true)]
public class Suppressor : TrapAbility
{
    // On floors, facing where you look (walls and the snow curtain stand across your view)
    protected override bool IsValidSurface(Vector3 normal) => normal.y > 0.7f;
    protected override Quaternion PlacementRotation(RaycastHit hit, Transform view) => Quaternion.Euler(0f, view.eulerAngles.y, 0f);
}
