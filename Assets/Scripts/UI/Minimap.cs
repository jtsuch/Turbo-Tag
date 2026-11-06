using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// The minimap: always shown in the corner; the Map key (default M) blows it up to a big map over most of the screen
/// (and back). A top-down orthographic camera follows the local player,
/// north up, and renders into a texture shown in a rounded HUD card. No map art is needed:
///   - the camera sits a few metres above the player's eyes and sees nothing higher, so roofs and upper floors
///     never hide the floor you're on;
///   - player bodies (and ragdolls and name tags) are hidden from it, so it can't give anyone away. Instead it
///     draws markers: you (an arrow) and whoever <see cref="GameMode.ShowsOnMap"/> allows (teammates).
/// The big map shows a wider area. Built by HUDManager.
/// </summary>
public class Minimap : MonoBehaviour
{
    private const float ViewRadius   = 45f;   // Metres from the centre to the edge (corner map)
    private const float BigRadius    = 70f;   // ...on the big map
    private const float BigSize      = 860f;  // Big map size in the 1920×1080 layout
    private const float AboveEyes    = 6f;    // How far above the eyes the camera floats (its near plane)
    private const float MapSize      = 300f;  // Pixels (in the 1920×1080 layout)
    private const int   TextureSize  = 512;

    private Camera cam;
    private RenderTexture texture;
    private RectTransform card, markers, arrow, frame;
    private bool big;
    private float radius = ViewRadius, mapSize = MapSize;
    private readonly List<Image> dots = new();
    private readonly List<Renderer> hidden = new();

    public static Minimap Create(RectTransform hudRoot)
    {
        var theme = UITheme.Current;
        var card = UIKit.Card(hudRoot, UIKit.WithAlpha(theme.panel, 0.92f), 26f, 0.45f, "Minimap");
        UIKit.Place(card, new Vector2(0f, 1f), new Vector2(32f, -156f), new Vector2(MapSize + 24f, MapSize + 56f));
        var map = card.gameObject.AddComponent<Minimap>();
        map.card = card;
        map.Build();
        return map;
    }

    private void Build()
    {
        var theme = UITheme.Current;
        var label = UIKit.Text(card, "MAP", 18f, UIKit.Font.Bold, theme.textMuted);
        UIKit.Place(label.rectTransform, new Vector2(0f, 1f), new Vector2(18f, -8f), new Vector2(200f, 26f));
        var key = UIKit.Chip(card, Keybinds.Label(Keybinds.Get(InputAction.Map)), theme.card, theme.text, 16f);
        UIKit.Place(key.rectTransform, new Vector2(1f, 1f), new Vector2(-14f, -8f), new Vector2(44f, 26f));

        // The view: the rendered texture inside a rounded mask
        var frameImage = UIKit.Panel(card, theme.field, 18f, "Frame");
        frame = frameImage.rectTransform;
        frame.anchorMin = new Vector2(0f, 0f);
        frame.anchorMax = new Vector2(1f, 1f);
        frame.offsetMin = new Vector2(12f, 12f);
        frame.offsetMax = new Vector2(-12f, -44f);
        frameImage.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var view = UIKit.Rect("View", frame).gameObject.AddComponent<RawImage>();
        UIKit.Stretch(view.rectTransform);
        view.raycastTarget = false;

        texture = new RenderTexture(TextureSize, TextureSize, 24) { name = "Minimap" };
        view.texture = texture;

        markers = UIKit.Rect("Markers", frame);
        UIKit.Stretch(markers);

        // You: a dot with a nose pointing where you look
        arrow = UIKit.Rect("You", markers);
        arrow.sizeDelta = new Vector2(24f, 24f);
        var body = UIKit.Rect("Dot", arrow).gameObject.AddComponent<Image>();
        body.sprite = UISprites.Circle;
        body.color = theme.primary;
        body.rectTransform.sizeDelta = new Vector2(18f, 18f);
        var outline = body.gameObject.AddComponent<Outline>();
        outline.effectColor = theme.textDark;
        outline.effectDistance = new Vector2(2f, -2f);
        var nose = UIKit.Panel(arrow, theme.primary, 3f, "Nose");
        nose.rectTransform.sizeDelta = new Vector2(6f, 14f);
        nose.rectTransform.anchoredPosition = new Vector2(0f, 14f);

        var go = new GameObject("Minimap Camera");
        go.transform.SetParent(transform, false);
        cam = go.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = ViewRadius;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 400f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = theme.field;
        cam.cullingMask = ~(1 << 5); // Everything but UI
        cam.targetTexture = texture;
        cam.depth = -10;
        var data = go.AddComponent<UniversalAdditionalCameraData>();
        data.renderShadows = false;
        data.renderPostProcessing = false;
        data.antialiasing = AntialiasingMode.None;
        data.requiresDepthTexture = false;
        data.requiresColorTexture = false;

        SetBig(false);
    }

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += BeforeRender;
        RenderPipelineManager.endCameraRendering   += AfterRender;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeforeRender;
        RenderPipelineManager.endCameraRendering   -= AfterRender;
        AfterRender(default, cam);
    }

    private void OnDestroy()
    {
        if (texture != null) texture.Release();
    }

    // Corner map, or a big map in the middle of the screen showing a wider area
    private void SetBig(bool value)
    {
        big = value;
        radius  = big ? BigRadius : ViewRadius;
        mapSize = big ? BigSize : MapSize;
        if (cam != null) cam.orthographicSize = radius;
        var anchor = big ? new Vector2(0.5f, 0.5f) : new Vector2(0f, 1f);
        UIKit.Place(card, anchor, big ? Vector2.zero : new Vector2(32f, -156f), new Vector2(mapSize + 24f, mapSize + 56f));
        card.pivot = big ? new Vector2(0.5f, 0.5f) : new Vector2(0f, 1f);
        card.SetAsLastSibling();
    }

    // ─── Update ───────────────────────────────────────────────────────────────

    private void Update()
    {
        if (!PauseMenuManager.IsPaused && !GunPicker.IsOpen && Input.GetKeyDown(Keybinds.Get(InputAction.Map)))
        {
            SetBig(!big);
            UISound.Play(big ? UISound.Kind.Click : UISound.Kind.Back);
        }
    }

    /// <summary>Every player gets a marker, whatever the mode allows (the Radar donut).</summary>
    public static bool ShowEveryone { get; set; }

    private void LateUpdate()
    {
        var local = Player.Local;
        if (local == null) return;

        // The map is a second render of the whole scene: the corner map only needs ~20 updates a second
        cam.enabled = big || Time.frameCount % 3 == 0;

        // Follow the player, north up; the near plane sits just above head height
        Vector3 eye = local.transform.position + Vector3.up * local.EyeHeight;
        cam.transform.SetPositionAndRotation(eye + Vector3.up * AboveEyes, Quaternion.Euler(90f, 0f, 0f));

        var cameraForward = PlayerCam.Local != null ? PlayerCam.Local.transform.forward : local.transform.forward;
        float yaw = Mathf.Atan2(cameraForward.x, cameraForward.z) * Mathf.Rad2Deg;
        arrow.anchoredPosition = Vector2.zero;
        arrow.localRotation = Quaternion.Euler(0f, 0f, -yaw);

        DrawOthers(local);
    }

    private void DrawOthers(Player local)
    {
        var manager = GameModeManager.Instance;
        var mode = manager != null ? manager.Mode : null;
        int used = 0;

        // Donuts: pink, pulsing, pinned to the rim when out of view (so players can go and find them)
        if (DonutManager.Instance != null)
        {
            float pulse = 12f + Mathf.PingPong(Time.unscaledTime * 10f, 4f);
            foreach (var donut in DonutManager.Instance.Donuts)
                if (donut != null) Mark(ref used, donut.transform.position, local, new Color(1f, 0.45f, 0.8f), pulse);
        }

        // Keep Away: the disc, always (it's the whole game)
        if (KeepAwayDisc.Instance != null)
            Mark(ref used, KeepAwayDisc.Instance.transform.position, local, new Color(1f, 0.85f, 0.3f), 16f + Mathf.PingPong(Time.unscaledTime * 12f, 5f));

        foreach (var p in Player.All)
        {
            if (p == null || p == local || p.IsDownAfterTag) continue;
            if (!ShowEveryone && (mode == null || !mode.ShowsOnMap(local.Role, p.Role))) continue;
            if (manager.HidesIdentityFromLocal(p)) continue;

            Color color = PlayerRoles.Color(p.Role);
            Mark(ref used, p.transform.position, local, color, 14f);
        }
        for (int i = used; i < dots.Count; i++) dots[i].gameObject.SetActive(false);
        arrow.SetAsLastSibling();
    }

    // One round marker at a world position (reusing the pool of dots)
    private void Mark(ref int used, Vector3 world, Player local, Color color, float size)
    {
        Vector3 d = world - local.transform.position;
        Vector2 pos = new Vector2(d.x, d.z) / (radius * 2f) * mapSize;
        float edge = mapSize * 0.5f - 10f;
        if (pos.magnitude > edge) pos = pos.normalized * edge; // Pinned to the rim when out of view

        if (used >= dots.Count)
        {
            var dot = UIKit.Rect("Mark", markers).gameObject.AddComponent<Image>();
            dot.sprite = UISprites.Circle;
            var o = dot.gameObject.AddComponent<Outline>();
            o.effectColor = UITheme.Current.textDark;
            o.effectDistance = new Vector2(1.5f, -1.5f);
            dots.Add(dot);
        }
        var img = dots[used++];
        img.gameObject.SetActive(true);
        img.rectTransform.sizeDelta = new Vector2(size, size);
        img.rectTransform.anchoredPosition = pos;
        img.color = color;
    }

    // ─── Hide bodies from the map camera ──────────────────────────────────────

    private void BeforeRender(ScriptableRenderContext context, Camera rendering)
    {
        if (rendering != cam) return;
        hidden.Clear();
        foreach (var p in Player.All)
        {
            if (p == null) continue;
            foreach (var r in p.GetComponentsInChildren<Renderer>()) Hide(r);
            if (p.Corpse != null) foreach (var r in p.Corpse.root.GetComponentsInChildren<Renderer>()) Hide(r);
        }
        foreach (var tag in FindObjectsByType<PlayerNameTag>(FindObjectsSortMode.None))
            if (tag.TagRoot != null) foreach (var r in tag.TagRoot.GetComponentsInChildren<Renderer>()) Hide(r);
    }

    private void Hide(Renderer r)
    {
        if (r.forceRenderingOff) return;
        r.forceRenderingOff = true;
        hidden.Add(r);
    }

    private void AfterRender(ScriptableRenderContext context, Camera rendering)
    {
        if (rendering != cam) return;
        foreach (var r in hidden) if (r != null) r.forceRenderingOff = false;
        hidden.Clear();
    }
}
