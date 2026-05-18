using UnityEngine;

/// <summary>
/// A chargeable bomb throw. Uses ThrowAbility's aim → charge → release flow.
/// The BoomBomb prefab (Resources/Object/BoomBomb) handles explosion logic on contact.
/// Tune force, charge time, and cooldown in the Inspector.
/// </summary>
public class BoomBomb : ThrowAbility
{
    [Header("BoomBomb Settings")]
    [TunableParam("Min Force",    5f,  80f)]  [SerializeField] private float minForce      = 15f;
    [TunableParam("Max Force",   20f, 120f)]  [SerializeField] private float maxForce      = 80f;
    [TunableParam("Charge Time", 0.2f, 3f)]  [SerializeField] private float chargeSeconds  = 1.5f;
    [TunableParam("Cooldown",    0.5f, 15f)] [SerializeField] private float cooldown        = 6f;

    protected override void Awake()
    {
        base.Awake();
        abilityName   = "BoomBomb";
        cooldownTime  = cooldown;
        minThrowForce = minForce;
        maxThrowForce = maxForce;
        chargeTime    = chargeSeconds;
    }
}
