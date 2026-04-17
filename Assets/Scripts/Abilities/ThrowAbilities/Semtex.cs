using UnityEngine;

/// <summary>
/// ThrowAbility: throws a Semtex charge that sticks to the first surface it hits,
/// beeps three times, then detonates.
///
/// Unity setup:
///  - Add to player prefab, set abilityName = "Semtex".
///  - Create prefab at Resources/Object/Semtex with Rigidbody, Collider,
///    PhotonView, and SemtexObject attached.
///  - Assign throwOrigin and trajectoryLine in the Inspector.
/// </summary>
public class Semtex : ThrowAbility
{
    [Header("Semtex Settings")]
    [SerializeField] private float cooldown      = 12f;
    [SerializeField] private float minForce      = 8f;
    [SerializeField] private float maxForce      = 40f;
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
        if (thrown.TryGetComponent(out SemtexObject semtex))
            semtex.IgnoreColliders(GetComponentsInChildren<Collider>());
    }
}
