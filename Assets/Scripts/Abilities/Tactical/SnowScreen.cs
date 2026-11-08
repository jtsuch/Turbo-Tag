using UnityEngine;

/// <summary>Raise a curtain of falling snow (14 s): it hides what's behind it and slows enemies who walk through. Health modes only. Behaviour: AbilityObjects/Tactical/SnowScreenObject.</summary>
[AbilityInfo("Raise a curtain of falling snow (14 s): it hides what's behind it and slows enemies who walk through.", DisplayName = "Snow Screen", HealthModesOnly = true)]
public class SnowScreen : TrapAbility
{
    // On floors, facing where you look (walls and the snow curtain stand across your view)
    protected override bool IsValidSurface(Vector3 normal) => normal.y > 0.7f;
    protected override Quaternion PlacementRotation(RaycastHit hit, Transform view) => Quaternion.Euler(0f, view.eulerAngles.y, 0f);
}
