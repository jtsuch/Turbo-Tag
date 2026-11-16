using UnityEngine;

/// <summary>Place a device that stops enemies near it from using abilities (8 s). Health modes only. Behaviour: AbilityObjects/Tactical/SuppressorObject.</summary>
[AbilityInfo("Place a device that stops enemies near it from using abilities (8 s).", DisplayName = "Suppression", HealthModesOnly = true)]
public class Suppressor : TrapAbility
{
    // Search & Destroy upgrade path: the value at levels 0, 1 and 2 (AbilityUpgrades reads it; Justin's table, Nov 14)
    private static readonly AbilityUpgrades.Entry Upgrades = AbilityUpgrades.Power("lasts", 0.75f, 1f, 1.25f, v => $"{v * 8f:0.#} s");
    public override AbilityUpgrades.Entry UpgradePath => Upgrades;

    // On floors, facing where you look (walls and the snow curtain stand across your view)
    protected override bool IsValidSurface(Vector3 normal) => normal.y > 0.7f;
    protected override Quaternion PlacementRotation(RaycastHit hit, Transform view) => Quaternion.Euler(0f, view.eulerAngles.y, 0f);
}
