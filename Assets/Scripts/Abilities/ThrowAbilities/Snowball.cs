using UnityEngine;

/// <summary>
/// ThrowAbility: throws a snowball that makes surfaces temporarily slick on impact.
///
/// Unity setup:
///  - Add this component to the player prefab alongside other abilities.
///  - Set abilityName = "Snowball" in the Inspector (must match the prefab at Resources/Object/Snowball).
///  - The Snowball prefab needs: Rigidbody, Collider, PhotonView, and SnowballObject script.
///  - Assign throwOrigin and trajectoryLine in the Inspector (same as BoomStick).
/// </summary>
public class Snowball : ThrowAbility
{
    [Header("Snowball Settings")]
    [SerializeField] private float cooldown      = 5f;
    [SerializeField] private float minForce      = 8f;
    [SerializeField] private float maxForce      = 35f;
    [SerializeField] private float chargeSeconds = 1.5f;

    protected override void Awake()
    {
        base.Awake();
        cooldownTime  = cooldown;
        minThrowForce = minForce;
        maxThrowForce = maxForce;
        chargeTime    = chargeSeconds;
    }

    protected override void OnThrow(GameObject thrown, Vector3 direction, float force)
    {
        if (thrown.TryGetComponent(out SnowballObject snowball))
            snowball.IgnoreColliders(GetComponentsInChildren<Collider>());
    }
}
