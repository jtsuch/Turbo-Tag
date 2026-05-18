using UnityEngine;

/// <summary>
/// A chargeable rock throw. Inherits the full aim → charge → release flow from ThrowAbility.
/// Tune force, charge time, and cooldown in the Inspector.
/// </summary>
public class Rock : ThrowAbility
{
    [Header("Rock Settings")]
    [TunableParam("Min Force",    5f,  60f)] [SerializeField] private float minForce    = 10f;
    [TunableParam("Max Force",   20f, 120f)] [SerializeField] private float maxForce    = 60f;
    [TunableParam("Charge Time", 0.2f, 3f)] [SerializeField] private float chargeSeconds = 1f;
    [TunableParam("Cooldown",    0.5f, 15f)] [SerializeField] private float cooldown    = 4f;

    protected override void Awake()
    {
        base.Awake();
        abilityName   = "Rock";
        cooldownTime  = cooldown;
        minThrowForce = minForce;
        maxThrowForce = maxForce;
        chargeTime    = chargeSeconds;
    }
}
