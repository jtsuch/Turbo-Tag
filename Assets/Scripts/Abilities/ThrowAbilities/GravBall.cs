using UnityEngine;

/// <summary>
/// ThrowAbility: throws a gravity orb that continuously pulls nearby players and
/// physics objects toward it for its entire lifetime.
///
/// Unity setup:
///  - Add to player prefab, set abilityName = "GravBall".
///  - Create prefab at Resources/Object/GravBall with Rigidbody, Collider (small sphere),
///    PhotonView, and GravBallObject attached.
///  - Assign throwOrigin and trajectoryLine in the Inspector.
/// </summary>
public class GravBall : ThrowAbility
{
    [Header("GravBall Settings")]
    [SerializeField] private float cooldown      = 8f;
    [SerializeField] private float minForce      = 10f;
    [SerializeField] private float maxForce      = 45f;
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
        if (thrown.TryGetComponent(out GravBallObject gravBall))
            gravBall.IgnoreColliders(GetComponentsInChildren<Collider>());
    }
}
