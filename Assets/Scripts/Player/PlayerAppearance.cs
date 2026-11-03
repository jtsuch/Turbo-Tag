using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// How a player's body looks to everyone else, beyond animation:
///   Cloak      — nearly invisible (see-through copies of the body materials, no shadow, no name tag);
///                the cloaked player sees a glow around their screen edges (ScreenVignette).
///   Camouflage — body painted one flat colour (Chameleon copies the surface it is stuck to).
///   Ice        — tinted icy blue while standing on ice (<see cref="Player.IsOnIce"/>).
/// The owner sets <see cref="Cloaked"/> and <see cref="Camouflage"/>; they are synced, and every client
/// fades its copy toward them. Uses MaterialPropertyBlocks (_BaseColor/_BaseMap), so PlayerHighlight's
/// emission glow is kept, except while cloaked.
/// Attach to: ThePlayer prefab root (adds itself to the PhotonView's observed components).
/// </summary>
[RequireComponent(typeof(Player))]
public class PlayerAppearance : MonoBehaviourPun, IPunObservable
{
    [Header("Renderers")]
    [Tooltip("Body renderers. Leave empty to use the 'Jimmy' child's SkinnedMeshRenderer.")]
    [SerializeField] private Renderer[] bodyRenderers;

    [Header("Cloak")]
    [Tooltip("Body opacity while cloaked (0 = invisible).")]
    [SerializeField, Range(0f, 1f)] private float cloakedAlpha = 0.06f;
    [SerializeField] private float cloakFadeSpeed = 4f;
    [Tooltip("Glow around the edges of your own screen while you're cloaked.")]
    [SerializeField] private Color cloakVignette = new(0.55f, 0.75f, 1f, 0.55f);

    [Header("Camouflage")]
    [SerializeField] private float camouflageFadeSpeed = 6f;

    [Header("Ice")]
    [SerializeField] private Color iceTint      = new(0.55f, 0.8f, 1f);
    [SerializeField] private float iceFadeSpeed = 4f;

    /// <summary>Owner: nearly invisible while true.</summary>
    public bool Cloaked { get; set; }

    /// <summary>Owner: paints the body this colour while set (null = normal look).</summary>
    public Color? Camouflage { get; set; }

    /// <summary>0 = fully visible, 1 = fully cloaked (smoothed; true on every client).</summary>
    public float CloakAmount { get; private set; }

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int BaseMapId   = Shader.PropertyToID("_BaseMap");
    private static readonly int EmissionId  = Shader.PropertyToID("_EmissionColor");

    private Player                player;
    private MaterialPropertyBlock block;
    private Color[]               baseColors;
    private Texture[]             baseMaps;
    private Material[][]          opaqueMaterials;
    private Material[][]          clearMaterials; // Transparent copies, made the first time a cloak is shown
    private ShadowCastingMode[]   shadowModes;
    private bool                  showingClear;
    private float                 iceAmount;
    private float                 camoAmount;
    private Color                 camoColor = Color.white;

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        player = GetComponent<Player>();
        block  = new MaterialPropertyBlock();

        if (bodyRenderers == null || bodyRenderers.Length == 0)
        {
            var jimmy = transform.Find("Jimmy");
            if (jimmy != null && jimmy.TryGetComponent<SkinnedMeshRenderer>(out var smr))
                bodyRenderers = new Renderer[] { smr };
            else
                bodyRenderers = new Renderer[0];
        }

        int n = bodyRenderers.Length;
        baseColors      = new Color[n];
        baseMaps        = new Texture[n];
        opaqueMaterials = new Material[n][];
        shadowModes     = new ShadowCastingMode[n];
        for (int i = 0; i < n; i++)
        {
            var mat = bodyRenderers[i].sharedMaterial;
            baseColors[i]      = mat != null && mat.HasProperty(BaseColorId) ? mat.GetColor(BaseColorId) : Color.white;
            baseMaps[i]        = mat != null && mat.HasProperty(BaseMapId) ? mat.GetTexture(BaseMapId) : null;
            opaqueMaterials[i] = bodyRenderers[i].sharedMaterials;
            shadowModes[i]     = bodyRenderers[i].shadowCastingMode;
        }

        if (!photonView.ObservedComponents.Contains(this))
            photonView.ObservedComponents.Add(this);
    }

    private void OnDestroy()
    {
        if (photonView != null && photonView.IsMine) ScreenVignette.Set(0f, cloakVignette);
        if (clearMaterials == null) return;
        foreach (var set in clearMaterials)
            if (set != null) foreach (var m in set) if (m != null) Destroy(m);
    }

    // After PlayerHighlight's Update, so the cloak can switch its glow off
    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        CloakAmount = Mathf.MoveTowards(CloakAmount, Cloaked ? 1f : 0f, cloakFadeSpeed * dt);

        // Your own body is hidden from you (PlayerInitializer); you get a screen-edge glow instead
        if (photonView.IsMine)
        {
            ScreenVignette.Set(CloakAmount, cloakVignette);
            return;
        }
        iceAmount   = Mathf.MoveTowards(iceAmount, player.IsOnIce ? 1f : 0f, iceFadeSpeed * dt);
        camoAmount  = Mathf.MoveTowards(camoAmount, Camouflage.HasValue ? 1f : 0f, camouflageFadeSpeed * dt);
        if (Camouflage.HasValue) camoColor = Camouflage.Value;

        bool clear = CloakAmount > 0.001f;
        if (clear != showingClear) SetClearMaterials(clear);

        float alpha = Mathf.Lerp(1f, cloakedAlpha, CloakAmount);
        for (int i = 0; i < bodyRenderers.Length; i++)
        {
            var r = bodyRenderers[i];
            if (r == null) continue;

            Color color = baseColors[i] * Color.Lerp(Color.white, iceTint, iceAmount);
            color = Color.Lerp(color, camoColor, camoAmount);
            color.a = alpha;

            r.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            // Fully camouflaged: drop the body texture so the colour is flat (the texture would show through)
            if (camoAmount > 0.5f) block.SetTexture(BaseMapId, Texture2D.whiteTexture);
            else if (baseMaps[i] != null) block.SetTexture(BaseMapId, baseMaps[i]);
            if (clear) block.SetColor(EmissionId, Color.black);
            r.SetPropertyBlock(block);
        }
    }

    // ─── Cloak materials ──────────────────────────────────────────────────────

    private void SetClearMaterials(bool clear)
    {
        showingClear = clear;
        if (clear && clearMaterials == null) BuildClearMaterials();

        for (int i = 0; i < bodyRenderers.Length; i++)
        {
            if (bodyRenderers[i] == null) continue;
            bodyRenderers[i].sharedMaterials   = clear ? clearMaterials[i] : opaqueMaterials[i];
            bodyRenderers[i].shadowCastingMode = clear ? ShadowCastingMode.Off : shadowModes[i];
        }
    }

    // URP Lit switched to Transparent (alpha blended), the same setup the hologram material uses
    private void BuildClearMaterials()
    {
        clearMaterials = new Material[bodyRenderers.Length][];
        for (int i = 0; i < bodyRenderers.Length; i++)
        {
            var source = opaqueMaterials[i];
            var copies = new Material[source.Length];
            for (int j = 0; j < source.Length; j++)
            {
                if (source[j] == null) continue;
                var m = new Material(source[j]) { name = source[j].name + " (Cloak)" };
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.DisableKeyword("_ALPHATEST_ON");
                m.renderQueue = (int)RenderQueue.Transparent;
                copies[j] = m;
            }
            clearMaterials[i] = copies;
        }
    }

    // ─── Network Sync ─────────────────────────────────────────────────────────

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(Cloaked);
            stream.SendNext(Camouflage.HasValue);
            Color c = Camouflage ?? Color.white;
            stream.SendNext(new Vector3(c.r, c.g, c.b));
        }
        else
        {
            Cloaked = (bool)stream.ReceiveNext();
            bool camo = (bool)stream.ReceiveNext();
            var  c    = (Vector3)stream.ReceiveNext();
            Camouflage = camo ? new Color(c.x, c.y, c.z) : (Color?)null;
        }
    }
}
