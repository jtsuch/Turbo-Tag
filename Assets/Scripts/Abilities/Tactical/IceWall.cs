using UnityEngine;

/// <summary>Raise a thick wall of ice to block a lane (20 s). Shoot it down to get through. Health modes only. Behaviour: AbilityObjects/Tactical/IceWallObject.</summary>
[AbilityInfo("Raise a thick wall of ice to block a lane (20 s). Shoot it down to get through.", DisplayName = "Ice Wall", HealthModesOnly = true)]
public class IceWall : TrapAbility
{
    // Search & Destroy upgrade path: the value at levels 0, 1 and 2 (AbilityUpgrades reads it; Justin's table, Nov 14)
    private static readonly AbilityUpgrades.Entry Upgrades = AbilityUpgrades.Power("wall health", 0.6f, 1f, 1.4f, v => $"{v * 250f:0}");
    public override AbilityUpgrades.Entry UpgradePath => Upgrades;

    // On floors, facing where you look (walls and the snow curtain stand across your view)
    protected override bool IsValidSurface(Vector3 normal) => normal.y > 0.7f;
    protected override Quaternion PlacementRotation(RaycastHit hit, Transform view) => Quaternion.Euler(0f, view.eulerAngles.y, 0f);
    // Only its centre has to be on a floor: it may run into side walls, and under players (it lifts them up)
    protected override bool NeedsFreeSpace => false;
}
