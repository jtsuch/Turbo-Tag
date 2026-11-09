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


    private PhotonView view;
    private Player     player;
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    private MaterialPropertyBlock block;

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
        // Armored: a soft blue glow, breathing, while it's up (on the body and on a full-body outfit over it)
        barrierAmount = Mathf.MoveTowards(barrierAmount, player.Has(PlayerFlag.Barrier) ? 1f : 0f, 4f * Time.deltaTime);
        Color barrier = new Color(0.2f, 0.5f, 1f) * (0.55f + 0.2f * Mathf.Sin(Time.time * 4f)) * barrierAmount;
        SetGlow(barrier);
        SetOutfitGlow(barrier * 1.3f);
    }

    private float barrierAmount;

    private void OnDisable()
    {
        if (block != null) { SetGlow(Color.black); SetOutfitGlow(Color.black); }
    }

    private readonly System.Collections.Generic.HashSet<Material> emissive = new();

    private void SetOutfitGlow(Color glow)
    {
        if (player == null || player.Appearance == null) return;
        foreach (var r in player.Appearance.OutfitRenderers)
        {
            if (r == null) continue;
            if (glow.maxColorComponent > 0.001f)
                foreach (var mat in r.sharedMaterials)
                    if (mat != null && emissive.Add(mat) && !mat.IsKeywordEnabled("_EMISSION")) mat.EnableKeyword("_EMISSION");
            r.GetPropertyBlock(block);
            block.SetColor(EmissionId, glow);
            r.SetPropertyBlock(block);
        }
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
