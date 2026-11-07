using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// A live 3D penguin for menus: a copy of the player model (only its mesh, skeleton and Animator; every script,
/// collider and Rigidbody stripped) idling far below the scene, filmed by its own camera into a transparent texture
/// shown in a RawImage. <see cref="SetSkin"/> recolours it (and makes it hop); <see cref="Dance"/> keeps it hopping
/// and twirling (the scoreboard podium). Each preview gets its own spot below the scene, so several can exist.
/// Used on the main menu player card and the scoreboard podium.
/// </summary>
public class PenguinPreview : MonoBehaviour, UnityEngine.EventSystems.IDragHandler
{
    private const int   PreviewLayer = 30;
    private const float Height = 2.9f; // The model's height (Player.TopY on a standing penguin)
    private static readonly Vector3 Stage = new(0f, -2000f, 0f);
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

    private GameObject holder;
    private Transform model;
    private Animator animator;
    private Renderer[] renderers;
    private RenderTexture texture;
    private MaterialPropertyBlock block;
    private string skin;
    private float spin;
    private Vector3 stage;
    private float nextMove;
    private static int created;
    private CosmeticWearer wearer;
    private float backUntil, facing, dragYaw, idleLook = 1f;
    private RawImage image;

    /// <summary>Lets the viewer spin the penguin by dragging it (the Wardrobe). Off by default.</summary>
    public bool Draggable
    {
        set { if (image != null) image.raycastTarget = value; }
    }

    // Dragging turns the penguin and stops its idle look-around, so it stays where it's put
    public void OnDrag(UnityEngine.EventSystems.PointerEventData e)
    {
        dragYaw -= e.delta.x * 0.5f;
        idleLook = 0f;
    }

    /// <summary>Turns the penguin's back to the camera for a few seconds (to show a back cosmetic).</summary>
    public void ShowBack(float seconds = 3.5f) => backUntil = Time.unscaledTime + seconds;

    /// <summary>Keeps hopping and twirling (with a little sway), for celebrations.</summary>
    public bool Dance { get; set; }

    /// <summary>Adds the preview image to <paramref name="parent"/> (a layout child of the given size).</summary>
    public static PenguinPreview Create(Transform parent, float size)
    {
        var image = UIKit.Rect("Penguin", parent).gameObject.AddComponent<RawImage>();
        UIKit.Size(image, size, size);
        image.raycastTarget = false;
        var preview = image.gameObject.AddComponent<PenguinPreview>();
        preview.image = image;
        preview.Build(image, Mathf.RoundToInt(size * 2f));
        return preview;
    }

    private void Build(RawImage image, int pixels)
    {
        texture = new RenderTexture(pixels, pixels, 24, RenderTextureFormat.ARGB32) { name = "PenguinPreview", antiAliasing = 4 };
        image.texture = texture;
        block = new MaterialPropertyBlock();

        holder = new GameObject("Penguin Preview Stage");
        holder.SetActive(false); // Nothing on the copy wakes up while it's stripped
        stage = Stage + Vector3.right * (40f * (created++ % 16)); // Apart, so cameras never film each other's penguin
        holder.transform.position = stage;

        var prefab = Resources.Load<GameObject>("Player/ThePlayer");
        if (prefab == null) return;
        var copy = Instantiate(prefab, holder.transform);
        copy.transform.localPosition = Vector3.zero;
        Strip(copy);
        model = copy.transform;
        foreach (var t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = PreviewLayer;

        animator = copy.GetComponentInChildren<Animator>();
        if (animator != null) animator.applyRootMotion = false;
        renderers = copy.GetComponentsInChildren<Renderer>(true);
        wearer = new CosmeticWearer(copy.transform, PreviewLayer);

        // Its own camera and light
        var camGo = new GameObject("Camera");
        camGo.transform.SetParent(holder.transform, false);
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.cullingMask = 1 << PreviewLayer;
        cam.fieldOfView = 28f; // Room above the head for tall hats (a wizard hat reaches ~3.7 m)
        cam.targetTexture = texture;
        cam.depth = -20;
        camGo.transform.localPosition = new Vector3(0f, Height * 0.62f, 9.5f);
        camGo.transform.LookAt(stage + Vector3.up * Height * 0.58f);
        var data = camGo.AddComponent<UniversalAdditionalCameraData>();
        data.renderShadows = false;
        data.renderPostProcessing = false;

        EnsureKeyLight();

        // A point light by the camera as well, a little extra shape on the front
        var fillGo = new GameObject("Fill");
        fillGo.transform.SetParent(holder.transform, false);
        fillGo.transform.localPosition = new Vector3(1.5f, Height * 0.9f, 5f);
        var fill = fillGo.AddComponent<Light>();
        fill.type = LightType.Point;
        fill.range = 20f;
        fill.intensity = 1.4f; // Softer: brighter glared on the white belly in the big Wardrobe view
        fill.cullingMask = 1 << PreviewLayer;
        fill.shadows = LightShadows.None;
        fill.renderMode = LightRenderMode.ForcePixel;

        holder.SetActive(true);
        if (animator != null)
        {
            SetBool("onGround", true);
            SetFloat("MovementSpeed", 0f);
            SetFloat("VerticalDirectionLooking", 0.5f);
            for (int i = 1; i < animator.layerCount; i++) animator.SetLayerWeight(i, i == 4 ? 1f : 0f); // Head layer on
        }
    }

    // Removes everything but visuals and animation: scripts first (in dependency order), then physics, then the
    // first-person camera rig. Done while inactive, so none of it ever runs.
    private static void Strip(GameObject copy)
    {
        var camera = copy.transform.Find("CameraHolder");
        if (camera != null) DestroyImmediate(camera.gameObject);

        for (int pass = 0; pass < 8; pass++)
        {
            var behaviours = copy.GetComponentsInChildren<MonoBehaviour>(true);
            if (behaviours.Length == 0) break;
            var required = new HashSet<Type>();
            foreach (var b in behaviours)
                if (b != null)
                    foreach (RequireComponent r in b.GetType().GetCustomAttributes(typeof(RequireComponent), true))
                    {
                        if (r.m_Type0 != null) required.Add(r.m_Type0);
                        if (r.m_Type1 != null) required.Add(r.m_Type1);
                        if (r.m_Type2 != null) required.Add(r.m_Type2);
                    }
            foreach (var b in behaviours)
            {
                if (b == null) continue;
                bool needed = false;
                foreach (var t in required) if (t.IsInstanceOfType(b)) { needed = true; break; }
                if (!needed || pass >= 6) DestroyImmediate(b);
            }
        }
        foreach (var j in copy.GetComponentsInChildren<Joint>(true)) DestroyImmediate(j);
        foreach (var c in copy.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
        foreach (var rb in copy.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(rb);
        foreach (var a in copy.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(a);
        foreach (var l in copy.GetComponentsInChildren<LineRenderer>(true)) l.enabled = false;
        foreach (var r in copy.GetComponentsInChildren<MeshRenderer>(true)) r.gameObject.SetActive(false); // Held props (pistols, leaf blower)
    }

    private void SetBool(string name, bool value)
    {
        foreach (var p in animator.parameters) if (p.name == name) { animator.SetBool(name, value); return; }
    }

    private void SetFloat(string name, float value)
    {
        foreach (var p in animator.parameters) if (p.name == name) { animator.SetFloat(name, value); return; }
    }

    /// <summary>Wears <paramref name="skinName"/> (a <see cref="PenguinSkins"/> name); a change makes it hop.</summary>
    public void SetSkin(string skinName)
    {
        if (renderers == null) return;
        bool changed = skin != null && skin != skinName;
        skin = skinName;
        var map = PenguinSkins.Texture(skinName);
        foreach (var r in renderers)
        {
            if (r == null || r is MeshRenderer) continue;
            r.GetPropertyBlock(block);
            if (map != null) block.SetTexture(BaseMapId, map);
            else block.Clear(); // "Black": the material's own map
            r.SetPropertyBlock(block);
        }
        if (changed && animator != null)
            foreach (var p in animator.parameters)
                if (p.name == "Jump" && p.type == AnimatorControllerParameterType.Trigger) animator.SetTrigger("Jump");
        if (changed) spin = 360f;
    }

    /// <summary>Wears a cosmetic (<see cref="Cosmetics"/> id, "" for nothing) in a slot.</summary>
    public void SetCosmetic(CosmeticSlot slot, string id)
    {
        if (wearer == null) return;
        id ??= Cosmetics.None;
        bool had = wearer.Wearing(slot);
        string before = had ? currentIds.TryGetValue(slot, out var b) ? b : "" : "";
        if (before == id) return;
        currentIds[slot] = id;
        wearer.Wear(slot, id);
        if (id != Cosmetics.None) spin = 360f; // A twirl to show it off
    }

    private readonly System.Collections.Generic.Dictionary<CosmeticSlot, string> currentIds = new();

    /// <summary>Wears <paramref name="player"/>'s picks (or the local saved ones when null).</summary>
    public void SetCosmetics(Photon.Realtime.Player player)
    {
        foreach (var slot in Cosmetics.Slots)
            SetCosmetic(slot, player != null ? Cosmetics.Of(player, slot) : Cosmetics.Saved(slot));
    }

    private void LateUpdate()
    {
        if (model == null || wearer == null) return;
        wearer.Update(model.position.y + Height, 1f);
    }

    private void Update()
    {
        if (model == null) return;
        // A slow look around, and a quick twirl after a colour change
        spin = Mathf.MoveTowards(spin, 0f, Time.unscaledDeltaTime * 720f);
        float yaw = Mathf.Sin(Time.unscaledTime * 0.6f) * 25f * idleLook + spin + dragYaw;
        float sway = 0f;
        if (Dance)
        {
            sway = Mathf.Sin(Time.unscaledTime * 6f) * 10f;
            if (Time.unscaledTime >= nextMove)
            {
                nextMove = Time.unscaledTime + UnityEngine.Random.Range(0.9f, 1.6f);
                if (animator != null)
                    foreach (var p in animator.parameters)
                        if (p.name == "Jump" && p.type == AnimatorControllerParameterType.Trigger) animator.SetTrigger("Jump");
                if (UnityEngine.Random.value < 0.45f) spin = UnityEngine.Random.value < 0.5f ? 360f : -360f;
            }
        }
        facing = Mathf.MoveTowardsAngle(facing, Time.unscaledTime < backUntil ? 180f : 0f, 420f * Time.unscaledDeltaTime);
        model.localRotation = Quaternion.Euler(0f, yaw + facing, sway);
    }

    // One directional light for every preview (they all share the preview layer). Each preview used to bring its
    // own: with the podium's three, Unity had to choose between three directional lights per penguin and one
    // penguin could come out unlit (black). Rebuilt when a new scene has none.
    private static Light keyLight;

    private static void EnsureKeyLight()
    {
        if (keyLight != null) return;
        var go = new GameObject("Penguin Preview Light");
        go.transform.rotation = Quaternion.Euler(35f, 160f, 0f);
        keyLight = go.AddComponent<Light>();
        keyLight.type = LightType.Directional;
        keyLight.intensity = 1.0f;
        keyLight.cullingMask = 1 << PreviewLayer;
        keyLight.shadows = LightShadows.None;
        keyLight.renderMode = LightRenderMode.ForcePixel;
    }

    private void OnDestroy()
    {
        if (holder != null) Destroy(holder);
        if (texture != null) texture.Release();
    }
}
