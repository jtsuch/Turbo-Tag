using Photon.Pun;
using TMPro;
using UnityEngine;

/// <summary>
/// The player's name in plain white text just above their head, seen by other players within
/// <see cref="readRange"/> (30 ft). Who can read whose name is the game mode's call
/// (<see cref="GameMode.ShowsName"/>): teammates always, hiders also see hunters, hunters never see
/// hiders. Hidden while the player is cloaked, camouflaged, or their identity is hidden (hunters during
/// Protect the President setup). Never shown on your own player. Always faces the camera.
/// The tag is its own object (not a child of the player), so nothing that rearranges or strips the
/// player's children can destroy it; it is removed with the player and rebuilt if it ever goes missing.
/// Attach to: ThePlayer prefab (the tag is created at runtime).
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class PlayerNameTag : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Farthest a name can be read from (m). 30 ft ≈ 9.1 m.")]
    [SerializeField] private float readRange    = 9.14f;
    [Tooltip("Gap between the top of the player and the name (m, scaled by body size).")]
    [SerializeField] private float headroom    = 0.35f;
    [SerializeField] private float fontSize    = 2.2f;

    private PhotonView       view;
    private Player           player;
    private PlayerAppearance appearance;
    private TextMeshPro      label;
    private Transform        tagRoot;
    private Camera           cam;

    private void Awake()
    {
        view = GetComponent<PhotonView>();
        if (view.IsMine) return;
        player     = GetComponent<Player>();
        appearance = GetComponent<PlayerAppearance>();
        CreateTag();
    }

    private void OnDestroy()
    {
        if (tagRoot != null) Destroy(tagRoot.gameObject);
    }

    // The tag isn't a child, so it has to be hidden by hand when the player is switched off
    private void OnDisable()
    {
        if (label != null) label.enabled = false;
    }

    private void CreateTag()
    {
        var go = new GameObject($"NameTag ({ResolveName()})");

        // Read the transform after adding the text: TextMeshPro swaps the Transform for a RectTransform,
        // destroying the original (holding on to it was the "Transform has been destroyed" error)
        label              = go.AddComponent<TextMeshPro>();
        tagRoot            = label.transform;
        label.alignment    = TextAlignmentOptions.Center;
        label.fontSize     = fontSize;
        label.color        = Color.white;
        label.outlineWidth = 0f;                 // Plain white words, no outline or glow
        label.fontStyle    = FontStyles.Normal;
        label.sortingOrder = 10;
        label.text         = ResolveName();
        label.enabled      = false;
    }

    private string ResolveName()
    {
        string nick = view.Owner?.NickName;
        return string.IsNullOrEmpty(nick) ? $"Player {view.Owner?.ActorNumber}" : nick;
    }

    private void LateUpdate()
    {
        if (view.IsMine) return;
        if (tagRoot == null || label == null) CreateTag(); // Rebuilt if anything destroyed it

        // The local camera may spawn after this player, and is replaced when the local player respawns
        if (cam == null || !cam.isActiveAndEnabled)
        {
            cam = PlayerCam.Local != null ? PlayerCam.Local.Camera : Camera.main;
            if (cam == null) return;
        }

        // Just above the top of the body, whatever the body size or stance
        float size = player != null ? player.Size : 1f;
        float top  = player != null ? player.TopY : transform.position.y + 1.8f;
        tagRoot.position = new Vector3(transform.position.x, top + headroom * size, transform.position.z);
        Vector3 dir = tagRoot.position - cam.transform.position;
        if (dir != Vector3.zero) tagRoot.rotation = Quaternion.LookRotation(dir);

        label.enabled = dir.magnitude <= readRange && Readable();
    }

    private bool Readable()
    {
        if (player == null) return true;
        if (player.IsDownAfterTag) return false; // Tagged: the ragdoll has no name over it

        // Hidden identity (hunters during Protect the President setup) shows no details at all
        var manager = GameModeManager.Instance;
        if (manager != null && manager.HidesIdentityFromLocal(player)) return false;

        // Cloak and camouflage would be given away by a name
        if (appearance != null && (appearance.CloakAmount > 0.5f || appearance.Camouflage.HasValue)) return false;

        if (manager == null || manager.Mode == null || Player.Local == null) return true;
        return manager.Mode.ShowsName(Player.Local.Role, player.Role);
    }
}
