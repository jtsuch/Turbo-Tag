using System.Collections.Generic;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>The gun picker shown on every spawn in gun modes (drawn by <see cref="CombatHud"/>).</summary>
public static class GunPicker
{
    private const string LastGunPref = "LastGun";

    public static bool IsOpen { get; private set; }

    /// <summary>Frame the picker closed on, so the click that picked a gun doesn't also fire it.</summary>
    public static int ClosedFrame { get; private set; } = -1;

    public static float EndsAt   { get; private set; }
    public static int   Selected { get; set; }

    /// <summary>The gun used last (remembered between lives and matches).</summary>
    public static string LastGun
    {
        get
        {
            string id = PlayerPrefs.GetString(LastGunPref, GunCatalog.DefaultId);
            return GunCatalog.Find(id) != null ? id : GunCatalog.DefaultId;
        }
    }

    /// <summary>Local player: puts the gun away and offers the choice for <see cref="MatchRules.GunPickTime"/> seconds.</summary>
    public static void Open()
    {
        var local = Player.Local;
        if (local == null || local.IsCaught) return;
        local.Gun.Equip(null);
        Selected = Mathf.Max(0, GunCatalog.IndexOf(LastGun));
        EndsAt   = Time.time + MatchRules.GunPickTime;
        IsOpen   = true;
    }

    /// <summary>Closes the picker; <paramref name="pick"/> equips the selected gun.</summary>
    public static void Close(bool pick)
    {
        if (!IsOpen) return;
        IsOpen      = false;
        ClosedFrame = Time.frameCount;
        if (!pick || Player.Local == null) return;

        var gun = GunCatalog.All[Mathf.Clamp(Selected, 0, GunCatalog.All.Count - 1)];
        PlayerPrefs.SetString(LastGunPref, gun.Id);
        Player.Local.Gun.Equip(gun.Id);
    }
}

/// <summary>
/// The gun-mode HUD, built in code on its own overlay canvas when CombatManager starts: health and ammo,
/// hit marker, red damage flash, kill feed, the gun picker, the killcam overlay and the death notice.
/// It reads state from the local player each frame; nothing in the scene needs setting up.
/// </summary>
public class CombatHud : MonoBehaviour
{
    private static CombatHud instance;

    private static readonly Color Orange = new(1f, 0.55f, 0.1f);
    private static readonly Color Panel  = new(0.05f, 0.06f, 0.08f, 0.75f);

    private TMP_FontAsset font;

    // Health / ammo
    private GameObject    vitals;
    private TMP_Text      healthText;
    private RectTransform healthFill;
    private Image         healthFillImage;
    private TMP_Text      ammoText;
    private TMP_Text      gunText;

    // Feedback
    private Image      damageFlash;
    private GameObject hitMarker;
    private Image[]    hitMarkerBars;
    private float      hitMarkerUntil;
    private float      flashAlpha;
    private float      healFlash;

    // Kill feed
    private RectTransform feed;
    private readonly List<(GameObject line, float until)> feedLines = new();

    // Picker
    private GameObject     picker;
    private TMP_Text       pickerFooter;
    private readonly List<(Image bg, TMP_Text text)> pickerRows = new();

    // Killcam / death notice
    private GameObject    killcamOverlay;
    private TMP_Text      killcamTitle;
    private RectTransform killcamProgress;
    private TMP_Text      deathNotice;

    private Player subscribed;

    // ─── Creation ─────────────────────────────────────────────────────────────

    public static void Create()
    {
        if (instance != null) return;
        var go = new GameObject("CombatHud");
        instance = go.AddComponent<CombatHud>();
        instance.Build();
    }

    /// <summary>Green flash after picking up a health drop.</summary>
    public static void Healed()
    {
        if (instance != null) instance.healFlash = 1f;
    }

    /// <summary>Shows (or with null hides) the killcam overlay.</summary>
    public static void ShowKillcam(string title)
    {
        if (instance == null) return;
        instance.killcamOverlay.SetActive(title != null);
        if (title != null) instance.killcamTitle.text = title;
    }

    /// <summary>Killcam progress, 0–1.</summary>
    public static void SetKillcamProgress(float t)
    {
        if (instance != null) instance.killcamProgress.anchorMax = new Vector2(Mathf.Clamp01(t), 1f);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        PlayerGun.LocalHit -= HandleHit;
        if (CombatManager.Instance != null) CombatManager.Instance.Killed -= HandleKilled;
        if (subscribed != null) subscribed.Health.Damaged -= HandleDamaged;
    }

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1; // Over the scene HUD (nothing overlaps it), under the pause menu (10)
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>(); // The gun picker's buttons
        font = FindHudFont();

        // Damage flash (under everything else)
        damageFlash = Img(transform, "Damage", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0.8f, 0f, 0f, 0f));
        damageFlash.raycastTarget = false;

        // Health (bottom left) and ammo (bottom right)
        vitals = new GameObject("Vitals", typeof(RectTransform));
        vitals.transform.SetParent(transform, false);
        Stretch((RectTransform)vitals.transform);

        var healthBg = Img(vitals.transform, "Health", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 40f), new Vector2(360f, 70f), Panel);
        healthText = Text(healthBg.transform, "Value", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 6f), new Vector2(140f, 60f), 46f, TextAlignmentOptions.Left);
        var barBack = Img(healthBg.transform, "Bar", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(150f, 0f), new Vector2(190f, 16f), new Color(1f, 1f, 1f, 0.15f));
        healthFillImage = Img(barBack.transform, "Fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Color.white);
        healthFill = healthFillImage.rectTransform;
        healthFill.anchorMin = Vector2.zero;

        var ammoBg = Img(vitals.transform, "Ammo", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 40f), new Vector2(300f, 90f), Panel);
        ammoText = Text(ammoBg.transform, "Rounds", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -4f), new Vector2(270f, 56f), 46f, TextAlignmentOptions.Right);
        gunText  = Text(ammoBg.transform, "Gun", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-16f, 6f), new Vector2(270f, 30f), 22f, TextAlignmentOptions.Right);
        gunText.color = Orange;

        // Hit marker: four short diagonal bars around the crosshair
        hitMarker = new GameObject("HitMarker", typeof(RectTransform));
        hitMarker.transform.SetParent(transform, false);
        hitMarkerBars = new Image[4];
        for (int i = 0; i < 4; i++)
        {
            float angle = 45f + 90f * i;
            Vector2 dir = new(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            var bar = Img(hitMarker.transform, "Bar", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dir * 16f, new Vector2(14f, 3f), Color.white);
            bar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
            hitMarkerBars[i] = bar;
        }
        hitMarker.SetActive(false);

        // Kill feed (top right)
        feed = new GameObject("KillFeed", typeof(RectTransform)).GetComponent<RectTransform>();
        feed.SetParent(transform, false);
        feed.anchorMin = feed.anchorMax = feed.pivot = new Vector2(1f, 1f);
        feed.anchoredPosition = new Vector2(-30f, -240f); // Below the clock and score
        feed.sizeDelta = new Vector2(560f, 300f);
        var layout = feed.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperRight;
        layout.spacing = 4f;
        layout.childControlHeight = layout.childControlWidth = true;
        layout.childForceExpandHeight = false;

        BuildPicker();

        // Killcam: a compact panel under the phase text (the HUD stays visible)
        var panel = Img(transform, "Killcam", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(900f, 110f), Panel);
        killcamOverlay = panel.gameObject;
        var label = Text(panel.transform, "Label", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(860f, 40f), 32f, TextAlignmentOptions.Center);
        label.text  = "KILLCAM";
        label.color = Orange;
        killcamTitle = Text(panel.transform, "Title", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -42f), new Vector2(860f, 36f), 26f, TextAlignmentOptions.Center);
        var skip = Text(panel.transform, "Skip", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(860f, 26f), 20f, TextAlignmentOptions.Center);
        skip.text  = "Press SPACE to skip";
        skip.color = new Color(1f, 1f, 1f, 0.7f);
        var progressBack = Img(panel.transform, "Progress", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 4f), new Color(1f, 1f, 1f, 0.15f));
        progressBack.rectTransform.pivot = new Vector2(0.5f, 0f);
        killcamProgress = Img(progressBack.transform, "Fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Orange).rectTransform;
        killcamOverlay.SetActive(false);

        deathNotice = Text(transform, "DeathNotice", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(1200f, 120f), 40f, TextAlignmentOptions.Center);
        deathNotice.gameObject.SetActive(false);

        PlayerGun.LocalHit += HandleHit;
    }

    private void BuildPicker()
    {
        picker = Img(transform, "GunPicker", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -170f), new Vector2(720f, 0f), Panel).gameObject;
        var rect = (RectTransform)picker.transform;
        float height = 70f + GunCatalog.All.Count * 64f + 50f;
        rect.sizeDelta = new Vector2(720f, height);

        var title = Text(picker.transform, "Title", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(680f, 48f), 34f, TextAlignmentOptions.Center);
        title.text  = "CHOOSE YOUR GUN";
        title.color = Orange;

        for (int i = 0; i < GunCatalog.All.Count; i++)
        {
            var gun = GunCatalog.All[i];
            var row = Img(picker.transform, gun.Id, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -70f - i * 64f), new Vector2(680f, 58f), new Color(1f, 1f, 1f, 0.06f));
            row.raycastTarget = true;
            var button = row.gameObject.AddComponent<Button>();
            button.targetGraphic = row;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.6f, 1.3f, 1f, 1f);
            colors.pressedColor     = new Color(1.2f, 1f, 0.8f, 1f);
            colors.fadeDuration     = 0.05f;
            button.colors = colors;
            int index = i;
            button.onClick.AddListener(() => { GunPicker.Selected = index; GunPicker.Close(pick: true); });
            var text = Text(row.transform, "Text", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 26f, TextAlignmentOptions.Left);
            text.rectTransform.offsetMin = new Vector2(16f, 0f);
            text.text = $"<b>[{i + 1}]  {gun.Name}</b>   <size=70%><color=#BBBBBB>{gun.Stats}</color></size>";
            pickerRows.Add((row, text));
        }

        pickerFooter = Text(picker.transform, "Footer", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(680f, 36f), 22f, TextAlignmentOptions.Center);
        picker.SetActive(false);
    }

    // ─── Update ───────────────────────────────────────────────────────────────

    private void Update()
    {
        var local = Player.Local;
        Subscribe(local);

        UpdatePicker(local);
        UpdateVitals(local);
        UpdateFeedback();
        UpdateFeed();
        UpdateDeathNotice(local);
    }

    private void Subscribe(Player local)
    {
        if (CombatManager.Instance != null && !killedHooked)
        {
            CombatManager.Instance.Killed += HandleKilled;
            killedHooked = true;
        }
        if (local == subscribed) return;
        if (subscribed != null) subscribed.Health.Damaged -= HandleDamaged;
        subscribed = local;
        if (local != null) local.Health.Damaged += HandleDamaged;
    }

    private bool killedHooked;

    private void UpdatePicker(Player local)
    {
        bool open = GunPicker.IsOpen && local != null && !local.IsCaught;
        if (GunPicker.IsOpen && !open) GunPicker.Close(pick: false);
        picker.SetActive(open && !PauseMenuManager.IsPaused);
        if (!open) return;

        // Click a gun or press its number; Space or Enter (or waiting) takes the gun used last
        for (int i = 0; i < GunCatalog.All.Count && i < 9; i++)
        {
            if (!Input.GetKeyDown(KeyCode.Alpha1 + i) && !Input.GetKeyDown(KeyCode.Keypad1 + i)) continue;
            GunPicker.Selected = i;
            GunPicker.Close(pick: true);
            return;
        }

        int last = Mathf.Max(0, GunCatalog.IndexOf(GunPicker.LastGun));
        bool quick = !PauseMenuManager.IsPaused
            && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter));
        if (quick || Time.time >= GunPicker.EndsAt)
        {
            GunPicker.Selected = last;
            GunPicker.Close(pick: true);
            return;
        }

        for (int i = 0; i < pickerRows.Count; i++)
            pickerRows[i].bg.color = i == last ? new Color(Orange.r, Orange.g, Orange.b, 0.35f) : new Color(1f, 1f, 1f, 0.06f);

        float left = Mathf.Max(0f, GunPicker.EndsAt - Time.time);
        pickerFooter.text = $"Click a gun or press 1–{GunCatalog.All.Count}  ·  Space/Enter: {GunCatalog.All[last].Name} ({Mathf.CeilToInt(left)}s)";
    }

    private void UpdateVitals(Player local)
    {
        bool show = local != null; // Always on (killcam and death included): the HUD never hides
        if (vitals.activeSelf != show) vitals.SetActive(show);
        if (!show) return;

        var health = local.Health;
        healthText.text = Mathf.CeilToInt(health.Current).ToString();
        healthFill.anchorMax = new Vector2(health.Fraction, 1f);
        healthFillImage.color = Color.Lerp(new Color(0.95f, 0.2f, 0.15f), Color.white, Mathf.InverseLerp(0.25f, 0.6f, health.Fraction));

        var gun = local.Gun.Current;
        if (gun == null)
        {
            ammoText.text = "-";
            gunText.text  = GunPicker.IsOpen ? "Choosing..." : "";
        }
        else if (local.Gun.IsReloading)
        {
            ammoText.text = $"<alpha=#55>{local.Gun.Ammo}<size=60%> / {gun.Magazine}</size>"; // The gun animates the reload
            gunText.text  = gun.Name;
        }
        else
        {
            ammoText.text = $"{local.Gun.Ammo}<size=60%> / {gun.Magazine}</size>";
            gunText.text  = $"{gun.Name}  ·  {Keybinds.Get(InputAction.Reload)} reload";
        }
    }

    private void UpdateFeedback()
    {
        flashAlpha = Mathf.MoveTowards(flashAlpha, 0f, Time.deltaTime * 1.2f);
        healFlash  = Mathf.MoveTowards(healFlash, 0f, Time.deltaTime * 2f);
        damageFlash.color = healFlash > flashAlpha
            ? new Color(0.2f, 0.9f, 0.3f, healFlash * 0.25f)
            : new Color(0.8f, 0f, 0f, flashAlpha);

        bool marker = Time.time < hitMarkerUntil;
        if (hitMarker.activeSelf != marker) hitMarker.SetActive(marker);
    }

    private void HandleHit(bool headshot)
    {
        hitMarkerUntil = Time.time + 0.15f;
        foreach (var bar in hitMarkerBars) bar.color = headshot ? new Color(1f, 0.25f, 0.2f) : Color.white;
    }

    private void HandleDamaged(float amount, int attacker) =>
        flashAlpha = Mathf.Clamp(flashAlpha + amount / 100f, 0.15f, 0.45f);

    // ─── Kill feed ────────────────────────────────────────────────────────────

    private void HandleKilled(KillInfo kill)
    {
        string victim = Name(kill.Victim);
        string line = kill.SelfDestruct
            ? $"{victim} <color=#BBBBBB>fell off</color>"
            : $"{Name(kill.Killer)} <color=#BBBBBB>[{GunCatalog.Find(kill.Weapon)?.Name ?? "?"}{(kill.Headshot ? " · headshot" : "")}]</color> {victim}";

        var bg = Img(feed, "Kill", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(0f, 40f), Panel);
        bg.gameObject.AddComponent<LayoutElement>().minHeight = 40f;
        var text = Text(bg.transform, "Text", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 24f, TextAlignmentOptions.Right);
        text.rectTransform.offsetMax = new Vector2(-12f, 0f);
        text.text = line;
        feedLines.Add((bg.gameObject, Time.time + 6f));

        while (feedLines.Count > 6)
        {
            Destroy(feedLines[0].line);
            feedLines.RemoveAt(0);
        }

        // Local death without a killcam: say what happened until the respawn
        bool mine = kill.Victim == PhotonNetwork.LocalPlayer.ActorNumber;
        if (mine && (kill.SelfDestruct || !Killcam.IsPlaying))
        {
            float penalty = MatchRules.FallPenalty;
            deathNotice.text = kill.SelfDestruct
                ? $"<color=#FF8C1A>SELF DESTRUCTION</color>\n<size=60%>You fell off{(penalty > 0f ? $"  ·  -{penalty:0} point{(penalty == 1f ? "" : "s")}" : "")}</size>"
                : $"<color=#FF8C1A>ELIMINATED</color>\n<size=60%>by {Name(kill.Killer)}</size>";
            deathNotice.gameObject.SetActive(true);
        }
        if (kill.Killer == PhotonNetwork.LocalPlayer.ActorNumber && !kill.SelfDestruct)
        {
            var hud = HUDManager.Instance;
            if (hud != null) hud.ShowBanner($"Eliminated {victim}{(kill.Headshot ? " (headshot)" : "")}", 1.5f);
        }
    }

    private void UpdateFeed()
    {
        for (int i = feedLines.Count - 1; i >= 0; i--)
        {
            if (Time.time < feedLines[i].until) continue;
            Destroy(feedLines[i].line);
            feedLines.RemoveAt(i);
        }
    }

    private void UpdateDeathNotice(Player local)
    {
        if (deathNotice.gameObject.activeSelf && (local == null || !local.IsCaught))
            deathNotice.gameObject.SetActive(false);
    }

    private static string Name(int actor)
    {
        var p = PhotonNetwork.CurrentRoom?.GetPlayer(actor);
        string name = p == null ? "Someone" : string.IsNullOrEmpty(p.NickName) ? $"Player {actor}" : p.NickName;
        return actor == PhotonNetwork.LocalPlayer.ActorNumber ? $"<color=#FF8C1A>{name}</color>" : name;
    }

    // ─── Building helpers ─────────────────────────────────────────────────────

    private TMP_FontAsset FindHudFont()
    {
        var hud = HUDManager.Instance;
        if (hud == null) return null;
        var text = hud.GetComponentInChildren<TMP_Text>(true);
        return text != null ? text.font : null;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static Image Img(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot     = anchorMin == anchorMax ? anchorMin : new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private TMP_Text Text(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, float fontSize, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        var rect = text.rectTransform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot     = anchorMin == anchorMax ? anchorMin : new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        if (font != null) text.font = font;
        text.fontSize  = fontSize;
        text.alignment = align;
        text.color     = Color.white;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }
}
