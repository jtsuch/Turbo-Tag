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
///   Silhouette — solid black, no glow, when the game mode hides who this player is from the viewer
///                (<see cref="GameModeManager.HidesIdentityFromLocal"/>: hunters during Protect the President setup).
///   Skin       — the player's penguin colour (<see cref="PenguinSkins"/>): their colour map replaces the
///                body texture on every copy, including their own (seen from a placed Camera).
///   Outline    — a sharp red rim around hunters (Resources/Cosmetics/HunterOutline, an extra material pass),
///                so they read as dangerous while keeping their penguin colour. Off while cloaked or hidden.
///   Crown      — a crown above the head while the mode says so (the President), unless hidden as above.
///                Placeholder model: Resources/Cosmetics/Crown.
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

    [Header("Silhouette")]
    [SerializeField] private Color silhouetteColor = new(0.02f, 0.02f, 0.02f);

    [Header("Crown")]
    [Tooltip("How far the crown sinks into the top of the head (m, scaled by body size). Negative lifts it.")]
    [SerializeField] private float crownSink = 0.12f;

    [Header("Ice")]
    [SerializeField] private Color iceTint      = new(0.55f, 0.8f, 1f);
    [SerializeField] private float iceFadeSpeed = 4f;

    /// <summary>Owner: nearly invisible while true.</summary>
    public bool Cloaked { get; set; }

    /// <summary>
    /// This screen sees every other player through walls (X-Ray Vision): an extra see-through pass that only draws
    /// where the body is hidden, blue for teammates and red for everyone else.
    /// </summary>
    public static bool XRay { get; set; }

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

        UpdateSkin();

        // Chameleon: the body holds perfectly still (a breathing, blinking wall gives it away)
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator != null) animator.speed = Camouflage.HasValue ? 0f : 1f;

        bool isGhost = (player.Role == PlayerRole.Ghost || player.Has(PlayerFlag.Phantom)) && !player.IsDownAfterTag;
        goldAmount = Mathf.MoveTowards(goldAmount, player.Has(PlayerFlag.Golden) ? 1f : 0f, 4f * dt);
        ghostAmount = Mathf.MoveTowards(ghostAmount, isGhost ? 1f : 0f, 2f * dt);

        // Your own body is hidden from you (PlayerInitializer); you get a screen-edge glow instead
        if (photonView.IsMine)
        {
            if (ghostAmount > CloakAmount) ScreenVignette.Set(ghostAmount * 0.7f, ghostVignette);
            else ScreenVignette.Set(CloakAmount, cloakVignette);
            return;
        }
        var  manager    = GameModeManager.Instance;
        bool silhouette = manager != null && manager.HidesIdentityFromLocal(player);
        bool crowned = manager != null && manager.ShowsCrown(player);
        UpdateCrown(crowned);
        // Cosmetics: hidden while disguised (silhouette, Chameleon), a ghost or down; cloaked, they fade with the body
        bool plain = !silhouette && !Camouflage.HasValue && ghostAmount < 0.05f && !player.IsDownAfterTag;
        UpdateCosmetics(plain, crowned);

        iceAmount   = Mathf.MoveTowards(iceAmount, player.IsOnIce ? 1f : 0f, iceFadeSpeed * dt);
        camoAmount  = Mathf.MoveTowards(camoAmount, Camouflage.HasValue ? 1f : 0f, camouflageFadeSpeed * dt);
        if (Camouflage.HasValue) camoColor = Camouflage.Value;

        bool clear   = CloakAmount > 0.001f || ghostAmount > 0.001f;
        int  kind    = !clear && !silhouette && manager != null && manager.Mode != null && Player.Local != null
                       ? manager.Mode.OutlineFor(Player.Local.Role, player.Role) : 0;
        Material outline = OutlineMaterial(kind);
        Material xray = XRayMaterial(XRayKind(manager));
        if (clear != showingClear || outline != showingOutline || xray != showingXRay) SetMaterials(clear, outline, xray);

        // Ghosts (Hunter mode) are pale and see-through on every screen
        float alpha = Mathf.Min(Mathf.Lerp(1f, cloakedAlpha, CloakAmount), Mathf.Lerp(1f, GhostAlpha, ghostAmount));
        for (int i = 0; i < bodyRenderers.Length; i++)
        {
            var r = bodyRenderers[i];
            if (r == null) continue;

            Color color = baseColors[i] * Color.Lerp(Color.white, iceTint, iceAmount);
            color = Color.Lerp(color, camoColor, camoAmount);
            color = Color.Lerp(color, GhostTint, ghostAmount * 0.65f);
            color = Color.Lerp(color, new Color(1f, 0.82f, 0.25f), goldAmount * (0.6f + 0.25f * Mathf.Sin(Time.time * 8f))); // Invulnerable
            if (silhouette) color = silhouetteColor;
            color.a = alpha;

            r.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            // Flat colour (camouflage, silhouette): drop the body texture, which would show through
            if (camoAmount > 0.5f || silhouette) block.SetTexture(BaseMapId, Texture2D.whiteTexture);
            else if (BodyMap(i) != null) block.SetTexture(BaseMapId, BodyMap(i));
            if (clear || silhouette) block.SetColor(EmissionId, Color.black);
            r.SetPropertyBlock(block);
        }
    }

    // ─── Skin ─────────────────────────────────────────────────────────────────

    private string    skinName;
    private Texture2D skinMap; // Null: the material's own map ("Black")

    private Texture BodyMap(int i) => skinMap != null ? skinMap : baseMaps[i];

    // Reads the synced pick each frame (a dictionary lookup) and swaps the map when it changes
    private void UpdateSkin()
    {
        string name = PenguinSkins.Of(photonView.Owner);
        if (name == skinName) return;
        skinName = name;
        skinMap  = PenguinSkins.Texture(name);

        // Remote copies set the map every frame below; the owner's own (normally hidden) body gets it once here
        if (!photonView.IsMine) return;
        for (int i = 0; i < bodyRenderers.Length; i++)
        {
            if (bodyRenderers[i] == null || BodyMap(i) == null) continue;
            bodyRenderers[i].GetPropertyBlock(block);
            block.SetTexture(BaseMapId, BodyMap(i));
            bodyRenderers[i].SetPropertyBlock(block);
        }
    }

    // ─── Crown ────────────────────────────────────────────────────────────────

    private static GameObject crownPrefab;
    private GameObject crown;
    private Transform  headBone;

    private void UpdateCrown(bool show)
    {
        if (!show)
        {
            if (crown != null && crown.activeSelf) crown.SetActive(false);
            return;
        }

        if (crown == null)
        {
            if (crownPrefab == null) crownPrefab = Resources.Load<GameObject>("Cosmetics/Crown");
            if (crownPrefab == null) return;
            crown = Instantiate(crownPrefab, transform);
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == "Head") { headBone = t; break; }
        }
        if (!crown.activeSelf) crown.SetActive(true);

        // On top of the head: level with the top of the body (it follows crouching and size), centred on the
        // head bone sideways, upright, facing forward
        float   size = player.Size;
        Vector3 head = headBone != null ? headBone.position : transform.position;
        Vector3 top  = new(head.x, player.TopY - crownSink * size, head.z);
        crown.transform.SetPositionAndRotation(top, Quaternion.LookRotation(transform.forward, Vector3.up));
        crown.transform.localScale = Vector3.one * size;
    }

    // ─── Cosmetics ────────────────────────────────────────────────────────────

    private CosmeticWearer wearer;

    // The owner's synced loadout, worn on this copy (children of the body, so the minimap hides them too). Hidden
    // whole while the body is disguised, hidden or see-through; head gear gives way to the crown. Everything rides
    // Jimmy's bones, so it lies down, tilts and crouches with him
    private void UpdateCosmetics(bool plain, bool crowned)
    {
        if (wearer == null)
        {
            var skin = GetComponentInChildren<SkinnedMeshRenderer>(true);
            wearer = new CosmeticWearer(transform, skin != null ? skin.gameObject.layer : gameObject.layer);
        }
        foreach (var slot in Cosmetics.Slots) wearer.Wear(slot, Cosmetics.Of(photonView.Owner, slot));
        wearer.Update(player.TopY, player.Size, slot => plain && !(slot == CosmeticSlot.Head && crowned));
        wearer.SetAlpha(Mathf.Lerp(1f, cloakedAlpha, CloakAmount));
    }

    // ─── Cloak materials ──────────────────────────────────────────────────────

    private static Material redOutline, blueOutline;
    private static Material xrayAlly, xrayEnemy;
    private Material showingXRay, showingOutline;
    private float ghostAmount, goldAmount;
    private Animator animator;

    private const float GhostAlpha = 0.32f;
    private static readonly Color GhostTint     = new(0.75f, 0.88f, 1f, 1f);
    private static readonly Color ghostVignette = new(0.6f, 0.8f, 1f, 0.45f);

    // 1: red (enemies, hunters; Resources/Cosmetics/HunterOutline), 2: blue (allies; a recoloured copy)
    private static Material OutlineMaterial(int kind)
    {
        if (kind == 0) return null;
        if (redOutline == null) redOutline = Resources.Load<Material>("Cosmetics/HunterOutline");
        if (redOutline == null) return null;
        if (kind == 1) return redOutline;
        if (blueOutline == null)
        {
            blueOutline = new Material(redOutline) { name = "AllyOutline" };
            blueOutline.SetColor("_OutlineColor", new Color(0.15f, 0.55f, 1f, 1f));
        }
        return blueOutline;
    }

    // Who this screen sees through walls: 0 none, 1 ally (blue), 2 enemy (red), 3 key ally (yellow: the President,
    // the disc carrier). The X-ray donut and Beacon show anyone; otherwise the mode decides (teammates in every mode,
    // hunters for hiders in Tag and Protect the President). A cloaked enemy stays hidden.
    private int XRayKind(GameModeManager manager)
    {
        if (player.IsDownAfterTag) return 0;
        var local = Player.Local;
        int kind = 0;
        if (XRay || player.IsBeacon) kind = IsTeammateOfLocal() ? 1 : 2;
        else if (local != null && local != player && manager != null && manager.Mode != null)
        {
            kind = manager.Mode.SeesThroughWalls(local.Role, player.Role);
            if (kind == 2 && CloakAmount > 0.5f) kind = 0;
        }
        bool key = player.Role == PlayerRole.President
                   || (KeepAwayDisc.Instance != null && KeepAwayDisc.Instance.Holder == player.ActorNumber);
        return kind == 1 && key ? 3 : kind;
    }

    private static Material xrayKey;

    // Resources/Cosmetics/XRay (TurboTag/XRay shader): one copy per colour
    private static Material XRayMaterial(int kind)
    {
        if (kind == 0) return null;
        if (xrayAlly == null || xrayEnemy == null || xrayKey == null)
        {
            var source = Resources.Load<Material>("Cosmetics/XRay");
            if (source == null) return null;
            xrayAlly  = new Material(source) { name = "XRay Ally" };
            xrayEnemy = new Material(source) { name = "XRay Enemy" };
            xrayKey   = new Material(source) { name = "XRay Key" };
            xrayAlly.SetColor("_Color",  new Color(0.25f, 0.6f, 1f, 0.75f));
            xrayEnemy.SetColor("_Color", new Color(1f, 0.25f, 0.3f, 0.75f));
            xrayKey.SetColor("_Color",   new Color(1f, 0.85f, 0.2f, 0.8f));
        }
        return kind == 1 ? xrayAlly : kind == 2 ? xrayEnemy : xrayKey;
    }

    // Same side as the local player: both hunters or both not, in modes with roles (no teams otherwise)
    private bool IsTeammateOfLocal()
    {
        var local   = Player.Local;
        var manager = GameModeManager.Instance;
        return local != null && manager != null && manager.Mode != null && manager.Mode.SameTeam(local.Role, player.Role);
    }

    // The body's materials: see-through copies while cloaked, plus the outline pass on hunters.
    // An extra material on a single-mesh renderer draws that mesh again, which is how the outline works.
    private void SetMaterials(bool clear, Material outlineMaterial, Material xray = null)
    {
        showingClear   = clear;
        showingOutline = outlineMaterial;
        showingXRay    = xray;
        bool outline   = outlineMaterial != null;
        if (clear && clearMaterials == null) BuildClearMaterials();

        for (int i = 0; i < bodyRenderers.Length; i++)
        {
            if (bodyRenderers[i] == null) continue;
            var set = clear ? clearMaterials[i] : opaqueMaterials[i];
            if (outline)
            {
                var withOutline = new Material[set.Length + 1];
                set.CopyTo(withOutline, 0);
                withOutline[set.Length] = outlineMaterial;
                set = withOutline;
            }
            if (xray != null)
            {
                var withXRay = new Material[set.Length + 1];
                set.CopyTo(withXRay, 0);
                withXRay[set.Length] = xray;
                set = withXRay;
            }
            bodyRenderers[i].sharedMaterials   = set;
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
                copies[j] = CosmeticWearer.SeeThroughCopy(source[j]);
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
