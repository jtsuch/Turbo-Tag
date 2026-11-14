using UnityEngine;

/// <summary>Places a trap that turns the ground slick under anyone who comes near.</summary>
[AbilityInfo("Place a trap that ices the ground under anyone who comes near.")]
public class IceTrap : TrapAbility
{
    // While placing, its trigger reach shows round the hologram (Justin, Nov 14)
    protected override float PreviewRadius
    {
        get
        {
            var prefab = Resources.Load<GameObject>(ResourcePath);
            var trap = prefab != null ? prefab.GetComponent<IceTrapObject>() : null;
            return trap != null ? trap.TriggerRadius * Owner.AbilityPower : 3f;
        }
    }
}
