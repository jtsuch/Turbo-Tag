using UnityEngine;

/// <summary>
/// A chargeable frisbee throw. Inherits the full aim → charge → release flow from ThrowAbility.
/// Tune force, charge time, and cooldown in the Inspector.
/// Requires a Frisbee prefab at Resources/Object/Frisbee with a Rigidbody and PhotonView.
/// </summary>
public class Frisbee : ThrowAbility
{
    [Header("Frisbee Settings")]
    [TunableParam("Min Force",    5f,  60f)] [SerializeField] private float minForce      = 12f;
    [TunableParam("Max Force",   20f, 120f)] [SerializeField] private float maxForce      = 50f;
    [TunableParam("Charge Time", 0.1f, 2f)] [SerializeField] private float chargeSeconds = 0.8f;
    [TunableParam("Cooldown",    0.5f, 15f)] [SerializeField] private float cooldown      = 5f;

    protected override void Awake()
    {
        base.Awake();
        abilityName   = "Frisbee";
        cooldownTime  = cooldown;
        minThrowForce = minForce;
        maxThrowForce = maxForce;
        chargeTime    = chargeSeconds;
    }
}
