using UnityEngine;
using Photon.Pun;

/// <summary>
/// Faint icy emission glow on remote players while they slide on ice, so the blue tint
/// (PlayerAppearance) still reads in dark areas. Teams are no longer tinted: hunters get a red
/// outline instead (PlayerAppearance), so everyone keeps their own penguin colour.
/// Only active on remote copies (your own body is hidden from you).
///
/// Unity setup: leave highlightRenderers empty to use the 'Jimmy' child. The Jimmy material needs
/// Emission ticked (any colour) so the _EMISSION keyword is on; this script sets the colour.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class PlayerHighlight : MonoBehaviour
{
    [Header("Renderers")]
    [Tooltip("Jimmy SkinnedMeshRenderer(s). Leave empty to auto-detect 'Jimmy' child.")]
    [SerializeField] private Renderer[] highlightRenderers;

    [Header("Ice")]
    [Tooltip("Faint glow added while on ice, so the tint reads in dark areas.")]
    [SerializeField] private Color iceGlow      = new Color(0.1f, 0.3f, 0.5f);
    [SerializeField] private float iceFadeSpeed = 4f;

    private PhotonView view;
    private Player     player;
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    private MaterialPropertyBlock block;
    private float iceAmount;

    private void Awake()
    {
        view   = GetComponent<PhotonView>();
        player = GetComponent<Player>();
        block  = new MaterialPropertyBlock();

        if (highlightRenderers == null || highlightRenderers.Length == 0)
        {
            var jimmy = transform.Find("Jimmy");
            highlightRenderers = jimmy != null && jimmy.TryGetComponent<SkinnedMeshRenderer>(out var smr)
                ? new Renderer[] { smr }
                : new Renderer[0];
        }
    }

    private void Start()
    {
        if (view.IsMine) { enabled = false; return; }

        // Emission must be on in the shared material for MaterialPropertyBlock overrides to show
        foreach (var r in highlightRenderers)
            foreach (var mat in r.sharedMaterials)
                if (mat != null && !mat.IsKeywordEnabled("_EMISSION"))
                    mat.EnableKeyword("_EMISSION");
    }

    private void Update()
    {
        if (player == null) return;
        iceAmount = Mathf.MoveTowards(iceAmount, player.IsOnIce ? 1f : 0f, iceFadeSpeed * Time.deltaTime);
        SetGlow(iceGlow * iceAmount);
    }

    private void OnDisable()
    {
        if (block != null) SetGlow(Color.black);
    }

    private void SetGlow(Color glow)
    {
        foreach (var r in highlightRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetColor(EmissionId, glow);
            r.SetPropertyBlock(block);
        }
    }
}
