using Photon.Pun;
using UnityEngine;

/// <summary>
/// TrapAbility: places an Ice Trap that freezes the ground under any player
/// who comes within range.
///
/// Unity setup:
///  - Add to player prefab, set abilityName = "IceTrap".
///  - Create prefab at Resources/Object/IceTrap with Rigidbody (Kinematic),
///    Collider, PhotonView, and IceTrapObject attached.
///  - Assign hologramMaterial, cameraHolder, and placementLayers (same as Box).
/// </summary>
public class IceTrap : TrapAbility
{
    [Header("IceTrap Settings")]
    [SerializeField] private float cooldown = 15f;

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
