using System.Collections.Generic;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// What's left of the old gun picker: the shop (<see cref="ShopMenu"/>) replaced it. Kept as the one place other code
/// asks "is a gun menu open" (no shooting, the mouse is free) and "did it just close" (that click doesn't fire).
/// </summary>
public static class GunPicker
{
    public static bool IsOpen => ShopMenu.IsOpen;

    /// <summary>Frame the shop closed on, so the click that closed it doesn't also fire.</summary>
    public static int ClosedFrame { get; private set; } = -1;

    public static void NoteClosed() => ClosedFrame = Time.frameCount;
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
    private TMP_Text      slotsText;   // "1 ICEPICK  2 PEBBLE  3 BLIZZARD" over the ammo card, the one in hand lit
    private GameObject    ammoCard, healthCard;

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

    // Killcam / death notice
    private GameObject    killcamOverlay;
    private TMP_Text      killcamTitle;
    private RectTransform killcamProgress;
    private TMP_Text      deathNotice;
    private GameObject    deathCard;

    private Player subscribed;

    // ─── Creation ─────────────────────────────────────────────────────────────

    /// <summary>Takes the HUD away (Freeplay's Guns switch turned off).</summary>
    public static void Remove()
    {
        if (instance != null) Destroy(instance.gameObject);
        instance = null;
    }

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
        healthCard = health.gameObject;
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
        ammoCard = ammo.gameObject;
        UIKit.Place(ammo, new Vector2(1f, 0f), new Vector2(-32f, 28f), new Vector2(360f, 108f));
        gunText = UIKit.OneLine(UIKit.Text(ammo, "", 18f, UIKit.Font.Bold, Theme.primary, TextAlignmentOptions.Right));
        UIKit.Place(gunText.rectTransform, new Vector2(1f, 1f), new Vector2(-22f, -12f), new Vector2(320f, 26f));
        ammoText = UIKit.Text(ammo, "", 56f, UIKit.Font.Display, Theme.text, TextAlignmentOptions.Right);
        UIKit.Place(ammoText.rectTransform, new Vector2(1f, 0f), new Vector2(-22f, 8f), new Vector2(320f, 66f));
        slotsText = UIKit.OneLine(UIKit.Text(vitals.transform, "", 17f, UIKit.Font.Bold, Theme.textMuted, TextAlignmentOptions.Right, "Slots"));
        UIKit.Place(slotsText.rectTransform, new Vector2(1f, 0f), new Vector2(-40f, 144f), new Vector2(600f, 26f));
        slotsText.rectTransform.pivot = new Vector2(1f, 0f);

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

        // Killcam: a pink frame around the whole screen (so it's clear this is a replay) with a slim strip along
        // the very bottom edge holding the label, the skip hint and the progress (under the HUD cards, out of the view)
        var kill = UIKit.Rect("Killcam", root);
        UIKit.Stretch(kill);
        killcamOverlay = kill.gameObject;
        var tint = Img(kill, "Tint", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(1f, 0.24f, 0.5f, 0.05f));
        tint.raycastTarget = false;
        const float Edge = 8f, StripHeight = 30f;
        Color frame = Theme.pink;
        Img(kill, "Top",   new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, Edge), frame).rectTransform.pivot = new Vector2(0.5f, 1f);
        Img(kill, "Left",  new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(Edge, 0f), frame).rectTransform.pivot = new Vector2(0f, 0.5f);
        Img(kill, "Right", new Vector2(1f, 0f), new Vector2(1f, 1f), Vector2.zero, new Vector2(Edge, 0f), frame).rectTransform.pivot = new Vector2(1f, 0.5f);

        var strip = Img(kill, "Strip", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, StripHeight), new Color(0.07f, 0.04f, 0.14f, 0.94f)).rectTransform;
        strip.pivot = new Vector2(0.5f, 0f);
        strip.anchoredPosition = Vector2.zero;
        killcamProgress = Img(strip, "Progress", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, UIKit.WithAlpha(Theme.pink, 0.45f)).rectTransform;
        var line = Img(strip, "Line", new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, 3f), frame).rectTransform;
        line.pivot = new Vector2(0.5f, 1f);
        line.anchoredPosition = Vector2.zero;

        var chip = UIKit.Text(strip, "KILLCAM", 20f, UIKit.Font.Display, Theme.pink, TextAlignmentOptions.MidlineLeft);
        UIKit.Stretch(chip.rectTransform, 20f, 0f, 0f, 2f);
        killcamTitle = UIKit.OneLine(UIKit.Text(strip, "", 19f, UIKit.Font.Bold, Theme.text, TextAlignmentOptions.Midline));
        UIKit.Stretch(killcamTitle.rectTransform, 200f, 200f, 0f, 2f);
        killcamTitle.alignment = TextAlignmentOptions.Midline;
        var skip = UIKit.Text(strip, "SPACE to skip", 17f, UIKit.Font.Bold, Theme.textMuted, TextAlignmentOptions.MidlineRight);
        UIKit.Stretch(skip.rectTransform, 0f, 20f, 0f, 2f);
        foreach (var graphic in kill.GetComponentsInChildren<Graphic>()) graphic.raycastTarget = false;
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

    // ─── Update ───────────────────────────────────────────────────────────────

    private void Update()
    {
        var local = Player.Local;
        Subscribe(local);

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

    private void UpdateVitals(Player local)
    {
        bool show = local != null; // Always on (killcam and death included): the HUD never hides
        if (vitals.activeSelf != show) vitals.SetActive(show);
        if (!show) return;

        var mode = GameModeManager.Instance != null ? GameModeManager.Instance.Mode : null;
        bool unarmed = mode != null && mode.FixedGun(local.Role) == "";
        if (ammoCard.activeSelf == unarmed) ammoCard.SetActive(!unarmed);
        UpdateSlots(local, mode);
        bool ghost = local.Role == PlayerRole.Ghost;
        if (healthCard.activeSelf == ghost) healthCard.SetActive(!ghost);

        var health = local.Health;
        healthText.text = health.Armor > 0f
            ? $"{Mathf.CeilToInt(health.Current)}<size=60%><color=#22D3EE>  +{Mathf.CeilToInt(health.Armor)}</color></size>"
            : Mathf.CeilToInt(health.Current).ToString();
        healthFill.anchorMax = new Vector2(health.Fraction, 1f);
        healthFillImage.color = Color.Lerp(Theme.danger, Theme.lime, Mathf.InverseLerp(0.25f, 0.6f, health.Fraction));
        healthText.color = health.Fraction <= 0.25f ? Color.Lerp(Theme.danger, Color.white, Mathf.PingPong(Time.time * 3f, 1f)) : Theme.text;

        var gun = local.Gun.Current;
        if (gun == null)
        {
            ammoText.text = "–";
            gunText.text  = GunPicker.IsOpen ? "Choosing..." : "";
        }
        else if (gun.Melee)
        {
            ammoText.text = "–";
            gunText.text  = gun.Name.ToUpperInvariant();
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

    // The weapon slots (1 knife, 2 secondary, 3 primary): the one in hand in the accent colour, keys from the bindings
    private void UpdateSlots(Player local, GameMode mode)
    {
        var loadout = Loadout.Instance;
        bool on = loadout != null && !local.IsCaught && mode != null && mode.FixedGun(local.Role) == null;
        if (!on) { slotsText.text = ""; return; }
        var held = local.Gun.Current;
        var sb = new System.Text.StringBuilder();
        void Slot(GunSlot slot, InputAction key)
        {
            var gun = GunCatalog.Find(loadout.GunIn(slot));
            if (gun == null) return;
            bool inHand = held != null && held.Id == gun.Id;
            if (sb.Length > 0) sb.Append("     ");
            sb.Append(inHand ? $"<color=#{ColorUtility.ToHtmlStringRGB(Theme.primary)}>" : "");
            sb.Append($"{Keybinds.Label(Keybinds.Get(key))}  {gun.Name.ToUpperInvariant()}");
            if (inHand) sb.Append("</color>");
        }
        Slot(GunSlot.Knife, InputAction.WeaponKnife);
        Slot(GunSlot.Secondary, InputAction.WeaponSecondary);
        Slot(GunSlot.Primary, InputAction.WeaponPrimary);
        slotsText.text = sb.ToString();
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
        Sfx.Here(headshot ? SfxKind.Headshot : SfxKind.Hit, headshot ? 0.7f : 0.5f);
        foreach (var bar in hitMarkerBars) bar.color = headshot ? new Color(1f, 0.25f, 0.2f) : Color.white;
    }

    private void HandleDamaged(float amount, int attacker)
    {
        flashAlpha = Mathf.Clamp(flashAlpha + amount / 100f, 0.15f, 0.45f);
        Sfx.Here(SfxKind.Hurt, Mathf.Lerp(0.5f, 1f, amount / 60f));
        Fx.Shake(Mathf.Clamp(amount / 120f, 0.08f, 0.5f));
    }

    // ─── Kill feed ────────────────────────────────────────────────────────────

    // " ×3" after a killer's name once they're on a streak of three or more
    private static string StreakTag(int actor)
    {
        int n = KillStreaks.Streak(actor);
        return n >= 3 ? $" <color=#FF8A3D>×{n}</color>" : "";
    }

    /// <summary>
    /// A line in the kill feed on this screen (golden donuts, ability kills): or, in modes without the gun HUD,
    /// a banner.
    /// </summary>
    public static void Post(string line, float seconds = 6f)
    {
        if (instance == null)
        {
            var hud = HUDManager.Instance;
            if (hud != null) hud.ShowBanner(line, Mathf.Min(seconds, 3f));
            return;
        }
        instance.AddFeedLine(line, seconds);
    }

    // One feed line: text, or (with an icon) "before [icon] after"
    private void AddFeedLine(string line, float seconds, Sprite icon = null, string after = null)
    {
        var bg = UIKit.Panel(feed, Panel, 14f, "Feed");
        bg.raycastTarget = false;
        bg.gameObject.AddComponent<LayoutElement>().minHeight = 42f;
        if (icon == null)
        {
            var text = UIKit.OneLine(UIKit.Text(bg.transform, line + (after != null ? "  " + after : ""), 22f, UIKit.Font.Bold, Theme.text, TextAlignmentOptions.Right));
            UIKit.Stretch(text.rectTransform, 16f, 16f, 0f, 0f);
            text.alignment = TextAlignmentOptions.MidlineRight;
        }
        else
        {
            var row = UIKit.HStack(bg.transform, 8f, UIKit.Pad(16, 0), TextAnchor.MiddleRight, expandHeight: false);
            row.childControlWidth = true;
            row.childForceExpandWidth = false;
            var before = UIKit.OneLine(UIKit.Text(bg.transform, line, 22f, UIKit.Font.Bold, Theme.text, TextAlignmentOptions.Right));
            var img = UIKit.Rect("Icon", bg.transform).gameObject.AddComponent<Image>();
            img.sprite = icon;
            img.preserveAspect = true;
            img.raycastTarget = false;
            UIKit.Size(img, 32f, 32f);
            var rest = UIKit.OneLine(UIKit.Text(bg.transform, after ?? "", 22f, UIKit.Font.Bold, Theme.text, TextAlignmentOptions.Right));
        }
        feedLines.Add((bg.gameObject, Time.time + seconds));
        while (feedLines.Count > 6)
        {
            Destroy(feedLines[0].line);
            feedLines.RemoveAt(0);
        }
    }

    // The local player's own notices for a knock-off (what the kill-feed path below does for other kills)
    private void KnockOffNotices(KillInfo kill, string victim)
    {
        if (kill.Victim == PhotonNetwork.LocalPlayer.ActorNumber && !Killcam.IsPlaying)
        {
            string with = string.IsNullOrEmpty(kill.KnockedWith) ? "" : $" with {AbilityCatalog.DisplayName(kill.KnockedWith)}";
            deathNotice.text = $"<color=#FF4D6D>KNOCKED OFF</color>\n<size=50%>by {Name(kill.Killer)}{with}</size>";
            deathCard.SetActive(true);
        }
        if (kill.Killer == PhotonNetwork.LocalPlayer.ActorNumber)
        {
            var hud = HUDManager.Instance;
            if (hud != null) hud.ShowBanner($"Knocked off {victim}!", 1.5f);
        }
    }

    private void HandleKilled(KillInfo kill)
    {
        string victim = Name(kill.Victim);
        if (kill.IsKnockOff)
        {
            // "Bob [Honk] knocked off Sam"
            AddFeedLine($"{Name(kill.Killer)}{StreakTag(kill.Killer)}", 6f,
                        string.IsNullOrEmpty(kill.KnockedWith) ? null : AbilityIcons.Get(kill.KnockedWith),
                        $"<color=#B9B2E0>knocked off</color>  {victim}");
            KnockOffNotices(kill, victim);
            return;
        }
        string line = kill.SelfDestruct
            ? $"{victim} <color=#B9B2E0>fell off</color>"
            : $"{Name(kill.Killer)}{StreakTag(kill.Killer)}  <color=#B9B2E0>{GunCatalog.Find(kill.Weapon)?.Name ?? "?"}{(kill.Headshot ? "  <color=#FF4D6D>HEADSHOT</color>" : "")}</color>  {victim}";

        AddFeedLine(line, 6f);

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
        if (actor == PhotonNetwork.LocalPlayer.ActorNumber) return $"<color=#FFC93C>{name}</color>";
        // Team modes: names in their team's colour
        var role = p != null ? PlayerRoles.Get(p) : PlayerRole.None;
        return PlayerRoles.IsTeam(role) ? $"<color=#{ColorUtility.ToHtmlStringRGB(PlayerRoles.Color(role))}>{name}</color>" : name;
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
