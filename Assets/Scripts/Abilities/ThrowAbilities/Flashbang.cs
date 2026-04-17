using UnityEngine;

/// <summary>
/// ThrowAbility: throws a flashbang that detonates on a 2-second fuse and temporarily
/// blinds anyone with line-of-sight to the pop.  Blind intensity scales with how directly
/// the target is looking at it and how close they are.
///
/// Unity setup:
///  - Add this component to the player prefab alongside other abilities.
///  - Set abilityName = "Flashbang" (must match the prefab at Resources/Object/Flashbang).
///  - The Flashbang prefab needs: Rigidbody, Collider, PhotonView, FlashbangObject script.
///  - Assign throwOrigin and trajectoryLine in the Inspector.
/// </summary>
public class Flashbang : ThrowAbility
{
    [Header("Flashbang Settings")]
    [SerializeField] private float cooldown      = 8f;
    [SerializeField] private float minForce      = 8f;
    [SerializeField] private float maxForce      = 30f;
    [SerializeField] private float chargeSeconds = 1f;

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
        if (thrown.TryGetComponent(out FlashbangObject fb))
        {
            fb.IgnoreColliders(GetComponentsInChildren<Collider>());
            fb.StartFuse();
        }
    }
}
