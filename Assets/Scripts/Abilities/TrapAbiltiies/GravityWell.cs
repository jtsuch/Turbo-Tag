using Photon.Pun;
using UnityEngine;

/// <summary>
/// TrapAbility: places a Gravity Well that drags players and physics objects toward it
/// through a cylindrical zone extending in front of the device.
///
/// Unity setup:
///  - Add this component to the player prefab alongside other abilities.
///  - Set abilityName = "GravityWell" (must match the prefab at Resources/Object/GravityWell).
///  - The GravityWell prefab needs: Rigidbody (kinematic), Collider, PhotonView,
///    GravityWellObject script.
///  - Assign hologramMaterial, cameraHolder, and placementLayers in the Inspector
///    (same Inspector fields as Box).
/// </summary>
public class GravityWell : TrapAbility
{
    [Header("GravityWell Settings")]
    [SerializeField] private float cooldown = 20f;

    private PhotonView view;

    protected override void Awake()
    {
        base.Awake();
        cooldownTime = cooldown;
    }

    private void Start()
    {
        view          = GetComponent<PhotonView>();
        isLocalPlayer = view.IsMine;
    }
}
