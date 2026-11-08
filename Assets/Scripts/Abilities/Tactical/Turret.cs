using UnityEngine;

/// <summary>Place a small turret that shoots enemies in front of it (30 s). Can be shot down. Health modes only. Behaviour: AbilityObjects/Tactical/TurretObject.</summary>
[AbilityInfo("Place a small turret that shoots enemies in front of it (30 s). Can be shot down.", HealthModesOnly = true)]
public class Turret : TrapAbility
{
    // On floors, facing where you look (walls and the snow curtain stand across your view)
    protected override bool IsValidSurface(Vector3 normal) => normal.y > 0.7f;
    protected override Quaternion PlacementRotation(RaycastHit hit, Transform view) => Quaternion.Euler(0f, view.eulerAngles.y, 0f);
}
