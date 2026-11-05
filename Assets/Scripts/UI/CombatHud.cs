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

    private static UITheme Theme => UITheme.Current;
    private static Color Panel => UIKit.WithAlpha(UITheme.Current.panel, 0.9f);

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
    private readonly List<Button> pickerRows = new();

    // Killcam / death notice
    private GameObject    killcamOverlay;
    private TMP_Text      killcamTitle;
    private RectTransform killcamProgress;
    private TMP_Text      deathNotice;
    private GameObject    deathCard;

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
        // Same fitting as every kit canvas; over the HUD (0), under the score overlay (5) and pause menu (20)
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.Expand;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        gameObject.AddComponent<UIScaler>();
        gameObject.AddComponent<GraphicRaycaster>(); // The gun picker's buttons
        UIKit.EnsureEventSystem();
        var root = (RectTransform)transform;

        // Damage / heal flash (under everything else)
        damageFlash = Img(transform, "Damage", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0.8f, 0f, 0f, 0f));

        // Health (bottom left) and ammo (bottom right)
        vitals = UIKit.Rect("Vitals", root).gameObject;
        UIKit.Stretch((RectTransform)vitals.transform);

        var health = UIKit.Card(vitals.transform, Panel, 22f, 0.4f, "Health");
        UIKit.Place(health, new Vector2(0f, 0f), new Vector2(32f, 28f), new Vector2(400f, 108f));
        var hLabel = UIKit.Text(health, "HEALTH", 18f, UIKit.Font.Bold, Theme.textMuted);
        UIKit.Place(hLabel.rectTransform, new Vector2(0f, 1f), new Vector2(24f, -12f), new Vector2(200f, 26f));
        healthText = UIKit.Text(health, "100", 50f, UIKit.Font.Display, Theme.text, TextAlignmentOptions.Right);
        UIKit.Place(healthText.rectTransform, new Vector2(1f, 1f), new Vector2(-22f, -2f), new Vector2(160f, 60f));
        var barBack = UIKit.Panel(health, Theme.field, 9f, "Bar");
        barBack.rectTransform.anchorMin = new Vector2(0f, 0f);
        barBack.rectTransform.anchorMax = new Vector2(1f, 0f);
        barBack.rectTransform.pivot = new Vector2(0.5f, 0f);
        barBack.rectTransform.sizeDelta = new Vector2(-48f, 18f);
        barBack.rectTransform.anchoredPosition = new Vector2(0f, 18f);
        healthFillImage = UIKit.Panel(barBack.transform, Theme.lime, 9f, "Fill");
        healthFill = healthFillImage.rectTransform;
        healthFill.anchorMin = Vector2.zero;
        healthFill.anchorMax = Vector2.one;
        healthFill.sizeDelta = Vector2.zero;

        var ammo = UIKit.Card(vitals.transform, Panel, 22f, 0.4f, "Ammo");
        UIKit.Place(ammo, new Vector2(1f, 0f), new Vector2(-32f, 28f), new Vector2(360f, 108f));
        gunText = UIKit.OneLine(UIKit.Text(ammo, "", 18f, UIKit.Font.Bold, Theme.primary, TextAlignmentOptions.Right));
        UIKit.Place(gunText.rectTransform, new Vector2(1f, 1f), new Vector2(-22f, -12f), new Vector2(320f, 26f));
        ammoText = UIKit.Text(ammo, "", 56f, UIKit.Font.Display, Theme.text, TextAlignmentOptions.Right);
        UIKit.Place(ammoText.rectTransform, new Vector2(1f, 0f), new Vector2(-22f, 8f), new Vector2(320f, 66f));

        // Hit marker: four short diagonal bars around the crosshair
        hitMarker = UIKit.Rect("HitMarker", root).gameObject;
        hitMarkerBars = new Image[4];
        for (int i = 0; i < 4; i++)
        {
            float angle = 45f + 90f * i;
            Vector2 dir = new(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            var bar = Img(hitMarker.transform, "Bar", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dir * 17f, new Vector2(15f, 4f), Color.white);
            UIKit.Round(bar, 2f);
            bar.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            bar.rectTransform.anchoredPosition = dir * 17f;
            bar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
            hitMarkerBars[i] = bar;
        }
        hitMarker.SetActive(false);

        // Kill feed (right, under the score)
        feed = UIKit.Rect("KillFeed", root);
        feed.anchorMin = feed.anchorMax = feed.pivot = new Vector2(1f, 1f);
        feed.anchoredPosition = new Vector2(-32f, -180f);
        feed.sizeDelta = new Vector2(560f, 320f);
        var layout = UIKit.VStack(feed, 6f, null, TextAnchor.UpperRight);
        layout.childForceExpandWidth = true;

        BuildPicker(root);

        // Killcam: a card under the clock (the rest of the HUD stays visible)
        var kill = UIKit.Card(root, Panel, 24f, 0.45f, "Killcam");
        UIKit.Place(kill, new Vector2(0.5f, 1f), new Vector2(0f, -176f), new Vector2(900f, 118f));
        killcamOverlay = kill.gameObject;
        var chip = UIKit.Chip(kill, "KILLCAM", Theme.pink, Theme.text, 20f);
        UIKit.Place(chip.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, 16f), new Vector2(150f, 34f));
        killcamTitle = UIKit.OneLine(UIKit.Text(kill, "", 28f, UIKit.Font.Display, Theme.text, TextAlignmentOptions.Center));
        UIKit.Place(killcamTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(860f, 40f));
        var skip = UIKit.Text(kill, "Press SPACE to skip", 18f, UIKit.Font.Bold, Theme.textMuted, TextAlignmentOptions.Center);
        UIKit.Place(skip.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(860f, 26f));
        var progressBack = UIKit.Panel(kill, Theme.field, 3f, "Progress");
        progressBack.rectTransform.anchorMin = new Vector2(0f, 0f);
        progressBack.rectTransform.anchorMax = new Vector2(1f, 0f);
        progressBack.rectTransform.pivot = new Vector2(0.5f, 0f);
        progressBack.rectTransform.sizeDelta = new Vector2(-48f, 6f);
        progressBack.rectTransform.anchoredPosition = new Vector2(0f, 8f);
        killcamProgress = UIKit.Panel(progressBack.transform, Theme.pink, 3f, "Fill").rectTransform;
        killcamProgress.anchorMin = Vector2.zero;
        killcamProgress.anchorMax = Vector2.one;
        killcamProgress.sizeDelta = Vector2.zero;
        killcamOverlay.SetActive(false);

        // Death notice (falls, or no killcam)
        var death = UIKit.Card(root, Panel, 28f, 0.5f, "Death");
        UIKit.Place(death, new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(760f, 150f));
        death.pivot = new Vector2(0.5f, 0.5f);
        deathNotice = UIKit.Text(death, "", 56f, UIKit.Font.Display, Theme.text, TextAlignmentOptions.Center);
        UIKit.Stretch(deathNotice.rectTransform, 20f, 20f, 10f, 10f);
        deathCard = death.gameObject;
        deathCard.SetActive(false);

        PlayerGun.LocalHit += HandleHit;
    }

    private void BuildPicker(RectTransform root)
    {
        var card = UIKit.Card(root, Theme.panel, 28f, 0.55f, "GunPicker");
        float height = 120f + GunCatalog.All.Count * 86f + 60f;
        UIKit.Place(card, new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), new Vector2(760f, height));
        card.pivot = new Vector2(0.5f, 0.5f);
        picker = card.gameObject;
        UIKit.VStack(card, 12f, UIKit.Pad(32, 28), TextAnchor.UpperCenter);

        UIKit.Title(card, "Choose Your Gun", 48f, TextAlignmentOptions.Center);
        UIKit.Spacer(card, 4f);

        for (int i = 0; i < GunCatalog.All.Count; i++)
        {
            var gun = GunCatalog.All[i];
            int index = i;
            var button = UIKit.Button(card, "", UIKit.Style.Ghost, () => { GunPicker.Selected = index; GunPicker.Close(pick: true); }, 74f, 28f);
            var label = UIKit.Label(button);
            label.enableAutoSizing = false;
            label.fontSize = 28f;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.richText = true;
            label.text = $"<color=#FFC93C>{i + 1}</color>   {gun.Name}   <size=65%><color=#B9B2E0>{gun.Stats}</color></size>";
            pickerRows.Add(button);
        }

        pickerFooter = UIKit.Text(card, "", 20f, UIKit.Font.Bold, Theme.textMuted, TextAlignmentOptions.Center);
        UIKit.Size(pickerFooter, height: 30f);
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
            pickerRows[i].GetComponent<UIButtonFx>().FaceColor = i == last ? Theme.secondary : Theme.card;

        float left = Mathf.Max(0f, GunPicker.EndsAt - Time.time);
        pickerFooter.text = $"Click a gun or press 1–{GunCatalog.All.Count}   ·   Space / Enter: {GunCatalog.All[last].Name}  ({Mathf.CeilToInt(left)})";
    }

    private void UpdateVitals(Player local)
    {
        bool show = local != null; // Always on (killcam and death included): the HUD never hides
        if (vitals.activeSelf != show) vitals.SetActive(show);
        if (!show) return;

        var health = local.Health;
        healthText.text = Mathf.CeilToInt(health.Current).ToString();
        healthFill.anchorMax = new Vector2(health.Fraction, 1f);
        healthFillImage.color = Color.Lerp(Theme.danger, Theme.lime, Mathf.InverseLerp(0.25f, 0.6f, health.Fraction));
        healthText.color = health.Fraction <= 0.25f ? Color.Lerp(Theme.danger, Color.white, Mathf.PingPong(Time.time * 3f, 1f)) : Theme.text;

        var gun = local.Gun.Current;
        if (gun == null)
        {
            ammoText.text = "–";
            gunText.text  = GunPicker.IsOpen ? "Choosing..." : "";
        }
        else if (local.Gun.IsReloading)
        {
            ammoText.text = $"<alpha=#55>{local.Gun.Ammo}<size=60%> / {gun.Magazine}</size>"; // The gun animates the reload
            gunText.text  = gun.Name.ToUpperInvariant() + "   ·   RELOADING";
        }
        else
        {
            ammoText.text = $"{local.Gun.Ammo}<size=60%> / {gun.Magazine}</size>";
            gunText.text  = $"{gun.Name.ToUpperInvariant()}   ·   {Keybinds.Label(Keybinds.Get(InputAction.Reload))} to reload";
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
            ? $"{victim} <color=#B9B2E0>fell off</color>"
            : $"{Name(kill.Killer)}  <color=#B9B2E0>{GunCatalog.Find(kill.Weapon)?.Name ?? "?"}{(kill.Headshot ? "  <color=#FF4D6D>HEADSHOT</color>" : "")}</color>  {victim}";

        var bg = UIKit.Panel(feed, Panel, 14f, "Kill");
        bg.raycastTarget = false;
        bg.gameObject.AddComponent<LayoutElement>().minHeight = 42f;
        var text = UIKit.OneLine(UIKit.Text(bg.transform, line, 22f, UIKit.Font.Bold, Theme.text, TextAlignmentOptions.Right));
        UIKit.Stretch(text.rectTransform, 16f, 16f, 0f, 0f);
        text.alignment = TextAlignmentOptions.MidlineRight;
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
                ? $"<color=#FFC93C>SELF DESTRUCTION</color>\n<size=50%>You fell off{(penalty > 0f ? $"  ·  -{penalty:0} point{(penalty == 1f ? "" : "s")}" : "")}</size>"
                : $"<color=#FF4D6D>ELIMINATED</color>\n<size=50%>by {Name(kill.Killer)}</size>";
            deathCard.SetActive(true);
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
        if (deathCard.activeSelf && (local == null || !local.IsCaught))
            deathCard.SetActive(false);
    }

    private static string Name(int actor)
    {
        var p = PhotonNetwork.CurrentRoom?.GetPlayer(actor);
        string name = p == null ? "Someone" : string.IsNullOrEmpty(p.NickName) ? $"Player {actor}" : p.NickName;
        return actor == PhotonNetwork.LocalPlayer.ActorNumber ? $"<color=#FFC93C>{name}</color>" : name;
    }

    // ─── Building helpers ─────────────────────────────────────────────────────

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
}
