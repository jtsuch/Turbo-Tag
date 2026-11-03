using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hold to stick: the player sticks to the first surface they touch (floor, wall, prop) and takes on
/// its colour until they let go or the duration runs out.
///   held → first non-player contact → Player.IsAnchored (movement freezes, no gravity)
///        → PlayerAppearance.Camouflage = surface colour (synced; remote copies paint the body flat)
/// The colour is the surface material's base colour times its texture's average colour, so it's a
/// rough match: good enough to hide on a plain wall, obvious on a busy one.
/// </summary>
[AbilityInfo("Hold to stick to the next surface you touch and take on its colour. Hide on walls and props (badly).")]
public class Chameleon : BasicAbility
{
    [Header("Camouflage")]
    [Tooltip("Lightens or darkens the copied colour (1 = as sampled).")]
    [TunableParam("Colour Brightness", 0.2f, 2f)]
    [SerializeField] private float brightness = 1f;

    private bool held;
    private bool stuck;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId     = Shader.PropertyToID("_Color");
    private static readonly int BaseMapId   = Shader.PropertyToID("_BaseMap");
    private static readonly Dictionary<Texture, Color> textureAverages = new();

    protected override void OnPress()
    {
        if (CurrentDuration > 0.01f) held = true;
    }

    protected override void OnRelease() => Deactivate();

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
            Color c = SurfaceColor(collision.collider) * brightness;
            Owner.Appearance.Camouflage = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
        }
    }

    // ─── Surface colour ───────────────────────────────────────────────────────

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
