using UnityEngine;
using Photon.Pun;

/// <summary>
/// Applies a coloured emission glow to remote player meshes to communicate team membership.
/// Only active on remote player instances — the local player's own mesh is never tinted.
///
/// Glow rules from the local player's perspective:
///   Hider  sees Hunter  → Red   (threat)
///   Hider  sees Hider   → Blue  (ally)
///   Hunter sees Hunter  → Red   (shared team colour)
///   Hunter sees Hider   → None  (hunters search naturally; no visual advantage)
///
/// Unity setup:
///   1. Assign the Jimmy SkinnedMeshRenderer to highlightRenderers in the Inspector
///      (or leave it empty — the component auto-detects a child named "Jimmy").
///   2. Open the Jimmy material and tick the Emission checkbox (any colour to initialise).
///      The script overrides the emission colour at runtime; the checkbox just activates
///      the _EMISSION keyword so MaterialPropertyBlock overrides take effect.
///   3. Add a Bloom override to your URP Volume for the best glow look.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class PlayerHighlight : MonoBehaviour
{
    [Header("Renderers")]
    [Tooltip("Jimmy SkinnedMeshRenderer(s). Leave empty to auto-detect 'Jimmy' child.")]
    [SerializeField] private Renderer[] highlightRenderers;

    [Header("Colours")]
    [SerializeField] private Color threatColor = new Color(1.0f, 0.15f, 0.1f);
    [SerializeField] private Color allyColor   = new Color(0.1f, 0.35f, 1.0f);
    [SerializeField] [Range(0f, 4f)] private float intensity = 1.5f;

    private PhotonView view;
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    private MaterialPropertyBlock block;

    private void Awake()
    {
        view  = GetComponent<PhotonView>();
        block = new MaterialPropertyBlock();

        if (highlightRenderers == null || highlightRenderers.Length == 0)
        {
            var jimmy = transform.Find("Jimmy");
            if (jimmy != null && jimmy.TryGetComponent<SkinnedMeshRenderer>(out var smr))
                highlightRenderers = new Renderer[] { smr };
        }
    }

    private void Start()
    {
        if (view.IsMine) { enabled = false; return; }

        // Enable emission on shared materials so MaterialPropertyBlock overrides take effect.
        // Using sharedMaterials avoids creating per-instance copies, which can corrupt URP state.
        foreach (var r in highlightRenderers)
            foreach (var mat in r.sharedMaterials)
                if (mat != null && !mat.IsKeywordEnabled("_EMISSION"))
                    mat.EnableKeyword("_EMISSION");
    }

    private void Update()
    {
        if (Player.Instance == null) return;
        if (GameModeManager.Instance == null) return;
        if (view.Owner == null) return;

        bool localIsHunter  = Player.Instance.isHunter;
        bool remoteIsHunter = GameModeManager.Instance.IsHunter(view.Owner.ActorNumber);

        Color target = ResolveColor(localIsHunter, remoteIsHunter);

        foreach (var r in highlightRenderers)
        {
            r.GetPropertyBlock(block);
            block.SetColor(EmissionId, target * intensity);
            r.SetPropertyBlock(block);
        }
    }

    private Color ResolveColor(bool localIsHunter, bool remoteIsHunter)
    {
        if (localIsHunter && !remoteIsHunter) return Color.black; // hunter vs hider   → none
        if (!localIsHunter && remoteIsHunter) return threatColor; // hider  vs hunter  → red
        if (!localIsHunter && !remoteIsHunter) return allyColor;  // hider  vs hider   → blue
        return threatColor;                                        // hunter vs hunter  → red
    }

    private void OnDisable()
    {
        if (block == null) return;
        foreach (var r in highlightRenderers)
        {
            r.GetPropertyBlock(block);
            block.SetColor(EmissionId, Color.black);
            r.SetPropertyBlock(block);
        }
    }
}
