using UnityEngine;

/// <summary>Place a field that slows enemies standing in it (10 s). Health modes only. Behaviour: AbilityObjects/Tactical/SlowFieldObject.</summary>
[AbilityInfo("Place a field that slows enemies standing in it (10 s).", DisplayName = "Slow Field", HealthModesOnly = true)]
public class SlowField : TrapAbility
{
    // On floors, facing where you look (walls and the snow curtain stand across your view)
    protected override bool IsValidSurface(Vector3 normal) => normal.y > 0.7f;
    protected override Quaternion PlacementRotation(RaycastHit hit, Transform view) => Quaternion.Euler(0f, view.eulerAngles.y, 0f);
}
