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

    // The owner's own body renderers (hidden from them; shown while watching from somewhere else)
    private readonly System.Collections.Generic.List<Renderer> ownBody = new();

    private void SetupFirstPerson()
    {
        if (armature != null) armature.SetActive(false);

        var firstPersonCamera = GetComponentInChildren<Camera>(true);
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is LineRenderer) continue; // Rope and trajectory stay visible

            bool viewModel = firstPersonCamera != null && renderer.transform.IsChildOf(firstPersonCamera.transform);
            if (viewModel) { renderer.shadowCastingMode = ShadowCastingMode.Off; continue; }

            renderer.enabled = false;
            bool onRig = armature != null && renderer.transform.IsChildOf(armature.transform); // Third-person gun etc.
            if (!onRig) ownBody.Add(renderer);
        }
    }

    /// <summary>
    /// Owner: shows or hides their own body (normally hidden from them). Used while viewing from a placed
    /// Camera, so the player can see themself.
    /// </summary>
    public void SetOwnBodyVisible(bool visible)
    {
        if (!GetComponent<PhotonView>().IsMine) return;

        if (armature != null && armature.activeSelf != visible)
        {
            armature.SetActive(visible);
            // The rig was off when the Animator started: rebind so the body poses instead of T-posing
            var animator = GetComponentInChildren<Animator>(true);
            if (visible && animator != null) animator.Rebind();
        }
        foreach (var renderer in ownBody)
            if (renderer != null) renderer.enabled = visible;
        // And its look: cosmetics and the crown are otherwise only worn on other screens
        if (TryGetComponent(out PlayerAppearance look)) look.ShowOwnLook = visible;
    }

    private void SetupRemote()
    {
        if (enemyCamera != null) Destroy(enemyCamera);
        foreach (var script in scriptsToDisable)
            if (script != null) script.enabled = false;
    }
}
