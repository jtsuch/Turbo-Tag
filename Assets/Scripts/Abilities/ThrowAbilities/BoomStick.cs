using UnityEngine;

/// <summary>
/// Throws a stick of dynamite that explodes on impact.
/// Hold LMB longer to throw harder. The Dynamite prefab handles its own explosion.
///
/// Unity setup:
///  - Add this component to the player prefab alongside your other abilities.
///  - Set abilityName = "BoomStick" in the Inspector (must match the Photon prefab name).
///  - Create a prefab at Resources/Object/BoomStick with a Rigidbody, Collider,
///    PhotonView, and the Dynamite script attached.
///  - Assign throwOrigin (camera or hand Transform) and trajectoryLine in the Inspector.
/// </summary>
public class BoomStick : ThrowAbility
{
    [Header("BoomStick Settings")]
    [SerializeField] private float cooldown = 3f;
    [SerializeField] private float minForce = 10f;
    [SerializeField] private float maxForce = 80f;
    [SerializeField] private float chargeSeconds = 1f;

    protected override void Awake()
    {
        base.Awake();
        cooldownTime   = cooldown;
        minThrowForce  = minForce;
        maxThrowForce  = maxForce;
        chargeTime     = chargeSeconds;
    }

    protected override void OnThrow(GameObject thrown, Vector3 direction, float force)
    {
        thrown.GetComponent<BoomStickObject>()?.IgnoreColliders(GetComponentsInChildren<Collider>());
    }
}
