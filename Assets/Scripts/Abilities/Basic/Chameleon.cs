using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hold to stick: the player sticks to the first surface they touch (floor, wall, prop) and takes on
/// its colour until they let go, the bar runs out or they fire a weapon.
///   held → first non-player contact → Player.IsAnchored (movement freezes, no gravity)
///        → PlayerAppearance.Camouflage = surface colour (synced; remote copies paint the body flat)
/// The colour is what the surface actually looks like round the contact point (Nov 14, Justin: "on the floor it just
/// turns black"): a small camera renders the patch of surface under the player, then a white card in the same spot,
/// and the patch divided by the card is the surface's own colour with the lighting taken out (the penguin's body is
/// lit again by the scene, so copying the lit colour would darken it twice). It reads whatever material, texture,
/// tile or vertex colour is really there; the old way took the mesh's first material, which on the kit's slabs is the
/// near-black edge face. Falls back to the material's colour if the render can't be read.
/// </summary>
[AbilityInfo("Hold to stick to the next surface you touch and take on its colour. Firing a weapon breaks it.")]
public class Chameleon : BasicAbility
{
    [Header("Camouflage")]
    [Tooltip("Lightens or darkens the copied colour (1 = as sampled).")]
    [TunableParam("Colour Brightness", 0.2f, 2f)]
    [SerializeField] private float brightness = 1f;

    private bool held;
    private bool stuck;
    private PlayerGun gun;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId     = Shader.PropertyToID("_Color");
    private static readonly int BaseMapId   = Shader.PropertyToID("_BaseMap");
    private static readonly Dictionary<Texture, Color> textureAverages = new();

    protected override void OnPress()
    {
        if (CurrentDuration <= 0.01f) return;
        held = true;
        // Firing breaks the disguise (Cloak does the same)
        if (gun == null && Owner != null && Owner.Gun != null) { gun = Owner.Gun; gun.Fired += OnFired; }
    }

    protected override void OnRelease() => Deactivate();

    private void OnFired()
    {
        if (held) Deactivate();
    }

    private void OnDestroy()
    {
        if (gun != null) gun.Fired -= OnFired;
    }

    protected override void Deactivate()
    {
        held     = false;
        stuck    = false;
        IsActive = false;
        if (Owner == null) return;
        Owner.IsAnchored = false;
        if (Owner.Appearance != null) Owner.Appearance.Camouflage = null;
    }

    protected override void FixedUpdate()
    {
        if (IsLocal && held && Owner.IsCaught) Deactivate();
        base.FixedUpdate();
    }

    // The Rigidbody is on this GameObject, so its contacts arrive here
    private void OnCollisionStay(Collision collision)
    {
        if (!IsLocal || !held || stuck) return;
        if (collision.collider.GetComponentInParent<Player>() != null) return;

        Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : collision.transform.position;
        stuck    = true;
        IsActive = true;
        Owner.IsAnchored = true;
        Body.linearVelocity = Vector3.zero;

        if (Owner.Appearance != null)
        {
            Color c = (SampleSurface(collision.collider, point) ?? SurfaceColor(collision.collider)) * brightness;
            Owner.Appearance.Camouflage = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
        }
    }

    // ─── Surface colour: rendered ─────────────────────────────────────────────

    private const int SampleSize = 16;
    private static Camera sampler;
    private static GameObject whiteCard;
    private static RenderTexture sampleTarget;

    // The surface's own colour round the contact: (what the patch looks like) / (what a white card looks like there)
    private Color? SampleSurface(Collider surface, Vector3 contact)
    {
        // What's straight in front of the chest (Justin, Nov 14: it favoured the floor round the feet); nothing there
        // within reach: the surface stuck to, along the ray from the player's middle to the contact
        Vector3 middle = transform.position + Vector3.up * (Owner.Height * 0.5f);
        Vector3 chest = transform.position + Vector3.up * (Owner.Height * 0.62f);
        RaycastHit hit = default;
        bool found = false;
        foreach (var h in Physics.RaycastAll(chest, transform.forward, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (h.collider.GetComponentInParent<Player>() == null && (!found || h.distance < hit.distance)) { hit = h; found = true; }
        if (!found)
        {
            Vector3 to = contact - middle;
            if (to.sqrMagnitude < 0.0001f) to = Vector3.down;
            if (!surface.Raycast(new Ray(middle, to.normalized), out hit, to.magnitude + 1f)) return null;
        }
        Vector3 normal = hit.normal;

        if (!EnsureSampler()) return null;
        // Look straight at the patch from 1.2 m off it (no UI). The player's own body, gear and first-person gun (all
        // children of the player, on the Default layer like the world) are switched off for the two renders
        sampler.cullingMask = ~(1 << 5);
        sampler.transform.SetPositionAndRotation(hit.point + normal * 1.2f, Quaternion.LookRotation(-normal, Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up));
        var own = Owner.GetComponentsInChildren<Renderer>();
        var wasOff = new bool[own.Length];
        for (int i = 0; i < own.Length; i++) { wasOff[i] = own[i].forceRenderingOff; own[i].forceRenderingOff = true; }

        whiteCard.SetActive(false);
        Color? surfaceSeen = RenderAverage();
        whiteCard.transform.SetPositionAndRotation(hit.point + normal * 0.03f, Quaternion.LookRotation(-normal));
        whiteCard.SetActive(true);
        Color? lightSeen = RenderAverage();
        whiteCard.SetActive(false);
        for (int i = 0; i < own.Length; i++) if (own[i] != null) own[i].forceRenderingOff = wasOff[i];
        if (surfaceSeen == null || lightSeen == null) return null;

        Color s = surfaceSeen.Value, l = lightSeen.Value;
        if (l.maxColorComponent < 0.02f) return null;   // Pitch dark: nothing to go by
        // Linear colours from the render; divided per channel, then back to the gamma colour the body material takes
        var albedo = new Color(s.r / Mathf.Max(0.02f, l.r), s.g / Mathf.Max(0.02f, l.g), s.b / Mathf.Max(0.02f, l.b), 1f);
        return QualitySettings.activeColorSpace == ColorSpace.Linear ? albedo.gamma : albedo;
    }

    private static bool EnsureSampler()
    {
        if (sampler == null)
        {
            var go = new GameObject("Chameleon Sampler") { hideFlags = HideFlags.HideAndDontSave };
            sampler = go.AddComponent<Camera>();
            sampler.enabled = false;
            sampler.fieldOfView = 40f;            // A patch about 0.9 m across
            sampler.nearClipPlane = 0.05f;
            sampler.farClipPlane = 3f;
            sampler.clearFlags = CameraClearFlags.SolidColor;
            sampler.backgroundColor = Color.black;
            sampler.allowHDR = false;
            sampler.allowMSAA = false;
        }
        if (whiteCard == null)
        {
            whiteCard = GameObject.CreatePrimitive(PrimitiveType.Quad);
            whiteCard.name = "Chameleon White Card";
            whiteCard.hideFlags = HideFlags.HideAndDontSave;
            Object.Destroy(whiteCard.GetComponent<Collider>());
            whiteCard.transform.localScale = Vector3.one * 2.5f;   // Fills the view at 1.2 m
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return false;
            var mat = new Material(shader) { color = Color.white };
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0f);
            var r = whiteCard.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            whiteCard.SetActive(false);
        }
        if (sampleTarget == null)
            sampleTarget = new RenderTexture(SampleSize, SampleSize, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        return true;
    }

    private static Color? RenderAverage()
    {
        var prev = RenderTexture.active;
        try
        {
            sampler.targetTexture = sampleTarget;
            sampler.Render();
            RenderTexture.active = sampleTarget;
            var read = new Texture2D(SampleSize, SampleSize, TextureFormat.RGBA32, false, true);
            read.ReadPixels(new Rect(0, 0, SampleSize, SampleSize), 0, 0);
            read.Apply();
            Color sum = Color.black;
            var pixels = read.GetPixels();
            foreach (var p in pixels) sum += p;
            Object.Destroy(read);
            return sum / pixels.Length;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Chameleon] Couldn't sample the surface: {e.Message}");
            return null;
        }
        finally
        {
            sampler.targetTexture = null;
            RenderTexture.active = prev;
        }
    }

    // ─── Surface colour: from the material (fallback) ─────────────────────────

    private static Color SurfaceColor(Collider col)
    {
        // Unity's == (not ??): a missing component can be a "fake null" object in the editor
        var renderer = col.GetComponent<Renderer>();
        if (renderer == null) renderer = col.GetComponentInParent<Renderer>();
        if (renderer == null) renderer = col.GetComponentInChildren<Renderer>();
        var mat = renderer != null ? renderer.sharedMaterial : null;
        if (mat == null) return Color.grey;

        Color tint = mat.HasProperty(BaseColorId) ? mat.GetColor(BaseColorId)
                   : mat.HasProperty(ColorId)     ? mat.GetColor(ColorId)
                   : Color.white;
        Texture tex = mat.HasProperty(BaseMapId) ? mat.GetTexture(BaseMapId) : mat.mainTexture;
        return tex != null ? tint * AverageColor(tex) : tint;
    }

    // Shrinks the texture on the GPU and reads back a few pixels (works on non-readable textures). Cached per texture.
    private static Color AverageColor(Texture tex)
    {
        if (textureAverages.TryGetValue(tex, out Color cached)) return cached;

        const int size = 8;
        var rt   = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var prev = RenderTexture.active;
        Graphics.Blit(tex, rt);
        RenderTexture.active = rt;
        var read = new Texture2D(size, size, TextureFormat.RGBA32, false);
        read.ReadPixels(new Rect(0, 0, size, size), 0, 0);
        read.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        Color sum = Color.black;
        var pixels = read.GetPixels();
        foreach (var p in pixels) sum += p;
        Object.Destroy(read);

        Color average = sum / pixels.Length;
        average.a = 1f;
        textureAverages[tex] = average;
        return average;
    }
}
