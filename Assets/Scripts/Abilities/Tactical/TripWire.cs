using UnityEngine;

/// <summary>String a wire across a gap from a wall: the first enemy to cross it is held in place and revealed. Health modes only. Behaviour: AbilityObjects/Tactical/TripWireObject.</summary>
[AbilityInfo("String a wire across a gap from a wall: the first enemy to cross it is held in place and revealed.", DisplayName = "Trip Wire", HealthModesOnly = true)]
public class TripWire : TrapAbility
{
    // On walls, facing out: the wire runs straight out from the wall to whatever is across the gap
    protected override bool IsValidSurface(Vector3 normal) => Mathf.Abs(normal.y) < 0.4f;
    protected override Quaternion PlacementRotation(RaycastHit hit, Transform view) => Quaternion.LookRotation(hit.normal, Vector3.up);
}
