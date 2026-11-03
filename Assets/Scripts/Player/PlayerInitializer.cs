using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Configures a freshly spawned player for its owner.
///   Local player: first person — every renderer outside the first-person camera (body, rig,
///   third-person gun, anything added to the model later) is hidden, and the viewmodel doesn't
///   cast shadows.
///   Remote copies: lose their camera and local-only scripts.
/// Ability enabling is handled separately by AbilityHandler from the owner's loadout.
/// Attach to: ThePlayer prefab root.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class PlayerInitializer : MonoBehaviour
{
    [Header("Local Player")]
    [Tooltip("Rig root (bones and third-person gun), deactivated for the owner.")]
    [SerializeField] private GameObject armature;

    [Header("Remote Players")]
    [Tooltip("Camera object destroyed on remote copies.")]
    [SerializeField] private GameObject enemyCamera;
    [Tooltip("Local-only scripts disabled on remote copies.")]
    [SerializeField] private MonoBehaviour[] scriptsToDisable;

    private void Awake()
    {
        if (GetComponent<PhotonView>().IsMine) SetupFirstPerson();
        else                                   SetupRemote();
    }

    private void SetupFirstPerson()
    {
        if (armature != null) armature.SetActive(false);

        var firstPersonCamera = GetComponentInChildren<Camera>(true);
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is LineRenderer) continue; // Rope and trajectory stay visible

            bool viewModel = firstPersonCamera != null && renderer.transform.IsChildOf(firstPersonCamera.transform);
            if (viewModel) renderer.shadowCastingMode = ShadowCastingMode.Off;
            else           renderer.enabled = false;
        }
    }

    private void SetupRemote()
    {
        if (enemyCamera != null) Destroy(enemyCamera);
        foreach (var script in scriptsToDisable)
            if (script != null) script.enabled = false;
    }
}
