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
    private RectTransform card, markers, arrow, frame, radarLayer, calloutLayer;
    private readonly List<TMPro.TMP_Text> calloutLabels = new();
    private TMPro.TMP_Text mapLabel;
    private Image pulse;
    private Color pulseColor;
    private float pulseAt = -99f;
    private static Minimap instance;

    /// <summary>
    /// A quiet alert: the map's border glows <paramref name="color"/> in two quick pulses (with a soft ping). Used
    /// instead of banners for things that happen often (radar sweeps, hunter tools finding you).
    /// </summary>
    public static void Pulse(Color color, bool ping = true)
    {
        if (instance == null) return;
        instance.pulseColor = color;
        instance.pulseAt = Time.unscaledTime;
        if (ping) Sfx.Here(SfxKind.Tick, 0.45f, 1.7f);
    }

    private void UpdatePulse()
    {
        float t = Time.unscaledTime - pulseAt;
        float a = t < 1.4f ? Mathf.Abs(Mathf.Sin(t / 1.4f * Mathf.PI * 2f)) * (1f - t / 1.4f) : 0f; // Two pulses, fading
        var c = pulseColor;
        c.a = a * 0.95f;
        pulse.color = c;
    }
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
        var label = mapLabel = UIKit.Text(card, "MAP", 18f, UIKit.Font.Bold, theme.textMuted);
        UIKit.Place(label.rectTransform, new Vector2(0f, 1f), new Vector2(18f, -8f), new Vector2(250f, 26f));
        var key = UIKit.Chip(card, Keybinds.Label(Keybinds.Get(InputAction.Map)), theme.card, theme.text, 16f);
        UIKit.Place(key.rectTransform, new Vector2(1f, 1f), new Vector2(-14f, -8f), new Vector2(44f, 26f));

        // A glow round the map that pulses for quiet alerts (radar sweeps, being spotted): see Pulse
        pulse = UIKit.Panel(card, new Color(1f, 0.25f, 0.3f, 0f), 22f, "Pulse");
        pulse.rectTransform.anchorMin = Vector2.zero;
        pulse.rectTransform.anchorMax = Vector2.one;
        pulse.rectTransform.offsetMin = new Vector2(4f, 4f);
        pulse.rectTransform.offsetMax = new Vector2(-4f, -36f);
        pulse.raycastTarget = false;

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

        calloutLayer = UIKit.Rect("Callouts", frame); // Place names, under everything else
        UIKit.Stretch(calloutLayer);
        radarLayer = UIKit.Rect("Radar", frame); // Under the player markers
        UIKit.Stretch(radarLayer);
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
        instance = this;
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

        DrawCallouts(local);
        DrawOthers(local);
        UpdateRadar(local);
        UpdatePulse();
    }

    // ─── Hunter radar (Tag, Protect the President) ────────────────────────────

    private const float RadarInterval = 20f; // Seconds between sweeps (every hunter's screen agrees: network time)
    private const float RadarShow     = 7f;  // How long a sweep stays up (fading out)
    private const float RadarRadius   = 14f; // Metres: the hider is somewhere inside
    // RadarRampsUp (Tag): the hunt starts with sweeps every 30 s and circles twice as wide, easing to the above
    private const float RampStartInterval = 30f, RampStartRadius = 28f, RampSeconds = 120f;
    private int   radarSweep = -1;
    private float radarSize = RadarRadius;   // Radius of the sweep on show
    private float radarShownAt = -99f;
    private readonly List<Vector3> radarPings = new();
    private readonly List<(Image fill, Image ring)> radarCircles = new();
    private readonly List<Image> radarGlows = new();

    // Every RadarInterval seconds of the hunt, a hunter's map shows a red circle around each hider (offset at
    // random so the hider can be anywhere inside it), or, for one off the map, a red glow on the rim toward them
    private void UpdateRadar(Player local)
    {
        var manager = GameModeManager.Instance;
        var mode = manager != null ? manager.Mode : null;
        bool radarMode = mode != null && mode.HunterRadar && manager.Phase == MatchPhase.Active && Photon.Pun.PhotonNetwork.InRoom;
        bool on = radarMode && local.Role == PlayerRole.Hunter && !local.IsCaught;
        bool target = radarMode && !local.IsCaught && local.Role != PlayerRole.Hunter && local.Role != PlayerRole.None && local.Role != PlayerRole.Ghost;

        // Which sweep we're in and how long until the next (every screen agrees: network time, or the hunt's clock)
        var (sweepNow, secondsLeft, sweepRadius) = radarMode ? RadarSchedule(mode, manager) : (0, 0, RadarRadius);
        string place = CalloutAt(local.transform.position);
        mapLabel.text = radarMode && (on || target) ? $"MAP  <color=#FF5A6E>RADAR {secondsLeft}s</color>"
                      : place != null ? $"MAP  <color=#FFFFFF>{place.ToUpperInvariant()}</color>" : "MAP";

        // Hiders are warned when a sweep finds them, so they can move
        if (target)
        {
            int sweep = sweepNow;
            if (radarSweep >= 0 && sweep != radarSweep) Pulse(new Color(1f, 0.25f, 0.3f)); // Spotted: a red pulse and a ping
            radarSweep = sweep;
        }
        if (on)
        {
            int sweep = sweepNow;
            if (sweep != radarSweep)
            {
                bool first = radarSweep < 0; // Not the moment the hunt starts: the first sweep is a full interval in
                radarSweep = sweep;
                radarPings.Clear();
                foreach (var p in Player.All)
                {
                    if (p == null || p == local || p.IsDownAfterTag) continue;
                    if (p.Role == PlayerRole.Hunter || p.Role == PlayerRole.None || p.Role == PlayerRole.Ghost) continue;
                    if (p.Has(PlayerFlag.Jammed)) continue; // The Jammer donut hides them from the radar
                    var rng = new System.Random(sweep * 7919 + p.ActorNumber);
                    float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float r = Mathf.Sqrt((float)rng.NextDouble()) * sweepRadius * 0.8f;
                    radarPings.Add(p.transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * r);
                }
                if (!first && radarPings.Count > 0)
                {
                    radarShownAt = Time.time;
                    radarSize = sweepRadius;
                    Pulse(new Color(1f, 0.3f, 0.35f), ping: false);
                    Sfx.Here(SfxKind.Shimmer, 0.5f, 0.7f);
                }
            }
        }
        else if (!target) radarSweep = -1;

        float age = Time.time - radarShownAt;
        float alpha = on && age < RadarShow ? Mathf.Clamp01((RadarShow - age) / 2f) : 0f; // Fades out over the last 2 s
        int circles = 0, glows = 0;
        if (alpha > 0f)
        {
            float edge = mapSize * 0.5f;
            float size = radarSize * 2f / (radius * 2f) * mapSize;
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 6f);
            foreach (var centre in radarPings)
            {
                Vector3 d = centre - local.transform.position;
                Vector2 pos = new Vector2(d.x, d.z) / (radius * 2f) * mapSize;
                if (pos.magnitude - size * 0.5f < edge)
                {
                    var (fill, ring) = RadarCircle(circles++);
                    fill.rectTransform.anchoredPosition = ring.rectTransform.anchoredPosition = pos;
                    fill.rectTransform.sizeDelta = ring.rectTransform.sizeDelta = new Vector2(size, size);
                    fill.color = new Color(1f, 0.15f, 0.2f, 0.22f * alpha);
                    ring.color = new Color(1f, 0.2f, 0.25f, 0.9f * alpha);
                }
                else
                {
                    var glow = RadarGlow(glows++);
                    glow.rectTransform.anchoredPosition = pos.normalized * (edge - 6f);
                    glow.rectTransform.sizeDelta = new Vector2(110f, 110f) * (big ? 1.4f : 1f);
                    glow.color = new Color(1f, 0.1f, 0.15f, 0.85f * alpha * pulse);
                }
            }
        }
        for (int i = circles; i < radarCircles.Count; i++) { radarCircles[i].fill.gameObject.SetActive(false); radarCircles[i].ring.gameObject.SetActive(false); }
        for (int i = glows; i < radarGlows.Count; i++) radarGlows[i].gameObject.SetActive(false);
    }

    /// <summary>
    /// The sweep we're in, the seconds until the next and how wide this one's circles are. Plain modes sweep every
    /// RadarInterval of network time. Modes that ramp up (Tag) count from the hunt's start: the first sweeps are
    /// 30 s apart with circles twice as wide, easing to 20 s / 14 m over two minutes. The last Tag hider is swept
    /// twice as often either way.
    /// </summary>
    private static (int sweep, int secondsLeft, float radius) RadarSchedule(GameMode mode, GameModeManager manager)
    {
        float speed = TagRoundSummary.HidersLeft() == 1 ? 2f : 1f;
        if (!mode.RadarRampsUp)
        {
            float interval = RadarInterval / speed;
            double now = Photon.Pun.PhotonNetwork.Time;
            return ((int)(now / interval), Mathf.CeilToInt(interval - (float)(now % interval)), RadarRadius);
        }
        float elapsed = manager.Clock.Elapsed;
        float at = 0f, previous = 0f;
        int sweep = 0;
        while (true)
        {
            float t = Mathf.Clamp01(at / RampSeconds);
            float next = at + Mathf.Lerp(RampStartInterval, RadarInterval, t) / speed;
            if (next > elapsed)
            {
                float radius = Mathf.Lerp(RampStartRadius, RadarRadius, Mathf.Clamp01(previous / RampSeconds));
                return (sweep, Mathf.Max(1, Mathf.CeilToInt(next - elapsed)), radius);
            }
            previous = at = next;
            sweep++;
        }
    }

    private (Image fill, Image ring) RadarCircle(int index)
    {
        while (radarCircles.Count <= index)
        {
            var fill = UIKit.Rect("Radar Area", radarLayer).gameObject.AddComponent<Image>();
            fill.sprite = UISprites.Circle;
            fill.raycastTarget = false;
            var ring = UIKit.Rect("Radar Ring", radarLayer).gameObject.AddComponent<Image>();
            ring.sprite = UISprites.Ring;
            ring.raycastTarget = false;
            radarCircles.Add((fill, ring));
        }
        var c = radarCircles[index];
        c.fill.gameObject.SetActive(true);
        c.ring.gameObject.SetActive(true);
        return c;
    }

    private Image RadarGlow(int index)
    {
        while (radarGlows.Count <= index)
        {
            var glow = UIKit.Rect("Radar Edge", radarLayer).gameObject.AddComponent<Image>();
            glow.sprite = UISprites.Blob;
            glow.raycastTarget = false;
            radarGlows.Add(glow);
        }
        radarGlows[index].gameObject.SetActive(true);
        return radarGlows[index];
    }

    // ─── Shots (gun modes) ────────────────────────────────────────────────────

    private const float ShotShow = 2.5f; // Seconds a shot stays on the map
    private static readonly List<(Player shooter, Vector3 at, float time)> shots = new();

    /// <summary>Every screen but the shooter's: <paramref name="shooter"/> just fired (their latest shot is kept).</summary>
    public static void NoteShot(Player shooter)
    {
        if (shooter == null) return;
        shots.RemoveAll(s => s.shooter == shooter || s.shooter == null || Time.time - s.time > ShotShow);
        shots.Add((shooter, shooter.transform.position, Time.time));
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
                if (donut != null) Mark(ref used, donut.transform.position, local, donut.Golden ? new Color(1f, 0.8f, 0.2f) : new Color(1f, 0.45f, 0.8f), pulse);
        }

        // Team pings
        foreach (var (point, color) in PingMarkers.Active) Mark(ref used, point, local, color, 14f + Mathf.PingPong(Time.unscaledTime * 16f, 6f));

        // Gun modes: enemies who just fired, as a red dot that fades (ShowsShotsOnMap)
        if (mode != null && mode.ShowsShotsOnMap)
            foreach (var (shooter, at, time) in shots)
            {
                float age = Time.time - time;
                if (age > ShotShow || shooter == null || shooter == local || shooter.IsDownAfterTag) continue;
                if (mode.SameTeam(local.Role, shooter.Role) && local.Role != PlayerRole.None) continue;
                Mark(ref used, at, local, new Color(1f, 0.15f, 0.2f, Mathf.Clamp01((ShotShow - age) / 0.8f)), 13f);
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

    // ─── Callouts: place names written over the map (MapCallout) ──────────────

    private static string CalloutAt(Vector3 point)
    {
        foreach (var c in MapCallout.All) if (c != null && c.Contains(point)) return c.label;
        return null;
    }

    private void DrawCallouts(Player local)
    {
        int used = 0;
        float edge = mapSize * 0.5f - 18f;
        foreach (var c in MapCallout.All)
        {
            if (c == null || !c.showLabel) continue;
            Vector3 d = c.transform.position - local.transform.position;
            Vector2 pos = new Vector2(d.x, d.z) / (radius * 2f) * mapSize;
            if (Mathf.Abs(pos.x) > edge || Mathf.Abs(pos.y) > edge) continue;   // Only names in view
            if (used >= calloutLabels.Count)
            {
                var t = UIKit.Text(calloutLayer, "", 14f, UIKit.Font.Bold, new Color(1f, 1f, 1f, 0.85f), TMPro.TextAlignmentOptions.Center, "Callout");
                t.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
                t.outlineWidth = 0.25f;
                t.outlineColor = new Color32(20, 22, 30, 255);
                t.rectTransform.sizeDelta = new Vector2(160f, 24f);
                calloutLabels.Add(t);
            }
            var label = calloutLabels[used++];
            label.gameObject.SetActive(true);
            label.text = c.label.ToUpperInvariant();
            label.fontSize = big ? 22f : 13f;
            label.rectTransform.anchoredPosition = pos;
        }
        for (int i = used; i < calloutLabels.Count; i++) calloutLabels[i].gameObject.SetActive(false);
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
            HideAll(p.transform);
            if (p.Corpse != null) HideAll(p.Corpse.root);
            // The name tag floats separately from the body
            if (p.TryGetComponent(out PlayerNameTag tag) && tag.TagRoot != null) HideAll(tag.TagRoot);
        }
    }

    private readonly List<Renderer> rendererBuffer = new(); // Reused: this runs every map frame

    private void HideAll(Transform root)
    {
        root.GetComponentsInChildren(rendererBuffer);
        foreach (var r in rendererBuffer) Hide(r);
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
