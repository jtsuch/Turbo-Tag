using UnityEngine;
using Photon.Pun;
using TMPro;

/// <summary>
/// Displays the remote player's PlayFab display name (stored in PhotonPlayer.NickName) in
/// world space above their head. Only visible when the local camera has an unobstructed
/// line of sight AND the player is within maxRange metres. Hidden on the local player's
/// own instance. The tag always faces the local camera (billboard).
///
/// Attach to: ThePlayer prefab — no prefab wiring needed. The name tag GameObject is
/// created at runtime so it works automatically on every player that spawns.
///
/// Note: if player collider layers block the LOS raycast, exclude them by setting
/// losBlockers in the Inspector to omit the Player layer.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class PlayerNameTag : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float maxRange    = 30f;
    [SerializeField] private float tagHeight   = 2.5f;  // metres above player pivot
    [SerializeField] private float fontSize    = 3f;
    [SerializeField] private float losInterval = 0.15f; // seconds between LOS raycasts
    [SerializeField] private LayerMask losBlockers = Physics.DefaultRaycastLayers;

    private PhotonView  view;
    private TextMeshPro label;
    private Transform   tagRoot;
    private Camera      cam;
    private float       nextLos;
    private bool        losVisible;

    private void Awake()
    {
        view = GetComponent<PhotonView>();
        if (view.IsMine) return;
        CreateTag();
    }

    private void Start()
    {
        if (view.IsMine) return;
        cam = Camera.main;
    }

    private void CreateTag()
    {
        var go = new GameObject("NameTag");
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.up * tagHeight;
        tagRoot = go.transform;

        label               = go.AddComponent<TextMeshPro>();
        label.alignment     = TextAlignmentOptions.Center;
        label.fontSize      = fontSize;
        label.color         = Color.white;
        label.outlineColor  = Color.black;
        label.outlineWidth  = 0.25f;
        label.fontStyle     = FontStyles.Bold;
        label.sortingOrder  = 10;
        label.text          = ResolveName();
        label.enabled       = false;
    }

    private string ResolveName()
    {
        string nick = view.Owner?.NickName;
        return string.IsNullOrEmpty(nick) ? $"Player {view.Owner?.ActorNumber}" : nick;
    }

    private void Update()
    {
        if (view.IsMine || label == null || cam == null) return;

        // Billboard — always face the local camera
        Vector3 dir = tagRoot.position - cam.transform.position;
        if (dir != Vector3.zero)
            tagRoot.rotation = Quaternion.LookRotation(dir);

        // Throttled LOS + range check (raycasts every losInterval seconds)
        if (Time.time >= nextLos)
        {
            nextLos    = Time.time + losInterval;
            losVisible = CheckLOS();
        }

        label.enabled = losVisible;
    }

    private bool CheckLOS()
    {
        Vector3 origin = cam.transform.position;
        Vector3 target = tagRoot.position;
        float   dist   = Vector3.Distance(origin, target);

        if (dist > maxRange) return false;

        // Subtract a small margin so the cast doesn't hit the tag's own position
        Vector3 direction = (target - origin).normalized;
        return !Physics.Raycast(origin, direction, dist - 0.2f, losBlockers);
    }
}
