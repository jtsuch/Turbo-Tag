using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gun-mode game feel on its own overlay (created with the CombatHud):
///   - kill confirm: a big red X around the crosshair, "ELIMINATED name" under it, a crunch and a short hit-stop;
///   - multi-kills and streaks: "DOUBLE KILL", "RAMPAGE"… popping in the middle (others hear about big streaks);
///   - low health (≤ 30%): a pulsing red vignette, a heartbeat and muffled hearing;
///   - hit direction: red arcs round the crosshair pointing where the shots came from.
/// </summary>
public class CombatFeedback : MonoBehaviour
{
    private const float LowHealth = 0.3f;

    private static CombatFeedback instance;
    private static UITheme Theme => UITheme.Current;

    private RectTransform root;

    // Kill confirm
    private RectTransform killMarker;
    private TMP_Text      killText;
    private float         killAt = -10f;

    // Call-outs
    private TMP_Text callout, calloutSub;
    private float    calloutAt = -10f;

    // Low health
    private Image             vignette;
    private float             nextBeat;
    private AudioLowPassFilter muffle;

    // Hit direction
    private sealed class Arc { public RectTransform Rect; public Image Image; public Vector3 From; public float At; }
    private readonly List<Arc> arcs = new();

    private Player subscribed;
    private bool   hitStopping;

    public static void Create()
    {
        if (instance != null) return;
        instance = new GameObject("CombatFeedback").AddComponent<CombatFeedback>();
        instance.Build();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        if (CombatManager.Instance != null) CombatManager.Instance.Killed -= HandleKilled;
        if (subscribed != null) subscribed.Health.Damaged -= HandleDamaged;
        if (muffle != null) muffle.cutoffFrequency = 22000f;
        if (hitStopping) Time.timeScale = 1f;
        KillStreaks.Clear();
    }

    // ─── Build ────────────────────────────────────────────────────────────────

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 2; // Over the combat HUD, under the score overlay and pause menu
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        gameObject.AddComponent<UIScaler>();
        root = (RectTransform)transform;

        // Low-health vignette: clear in the middle, red at the edges
        vignette = new GameObject("LowHealth", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        vignette.transform.SetParent(root, false);
        UIKit.Stretch(vignette.rectTransform);
        vignette.sprite = VignetteSprite();
        vignette.color = new Color(0.8f, 0f, 0.05f, 0f);
        vignette.raycastTarget = false;

        // Kill X: four long red bars
        killMarker = UIKit.Rect("KillMarker", root);
        killMarker.anchorMin = killMarker.anchorMax = killMarker.pivot = new Vector2(0.5f, 0.5f);
        for (int i = 0; i < 4; i++)
        {
            float angle = 45f + 90f * i;
            Vector2 dir = new(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            var bar = UIKit.Panel(killMarker, new Color(1f, 0.2f, 0.25f), 3f, "Bar");
            bar.raycastTarget = false;
            bar.rectTransform.anchorMin = bar.rectTransform.anchorMax = bar.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            bar.rectTransform.sizeDelta = new Vector2(30f, 7f);
            bar.rectTransform.anchoredPosition = dir * 28f;
            bar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
        }
        killMarker.gameObject.SetActive(false);

        killText = UIKit.OneLine(UIKit.Text(root, "", 30f, UIKit.Font.Display, Theme.text, TextAlignmentOptions.Center, "KillText"));
        UIKit.Place(killText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -96f), new Vector2(900f, 44f));
        killText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        killText.rectTransform.anchoredPosition = new Vector2(0f, -96f);
        killText.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);

        callout = UIKit.OneLine(UIKit.Text(root, "", 84f, UIKit.Font.Display, Theme.primary, TextAlignmentOptions.Center, "Callout"));
        UIKit.Place(callout.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 210f), new Vector2(1400f, 110f));
        callout.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        callout.rectTransform.anchoredPosition = new Vector2(0f, 210f);
        callout.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(4f, -4f);
        calloutSub = UIKit.OneLine(UIKit.Text(root, "", 30f, UIKit.Font.Bold, Theme.text, TextAlignmentOptions.Center, "CalloutSub"));
        UIKit.Place(calloutSub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 148f), new Vector2(1200f, 40f));
        calloutSub.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        calloutSub.rectTransform.anchoredPosition = new Vector2(0f, 148f);

        foreach (var g in GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
    }

    private static Sprite vignetteSprite;

    // White with alpha rising toward the edges (tinted by the Image colour)
    public static Sprite VignetteSprite()
    {
        if (vignetteSprite != null) return vignetteSprite;
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Vignette" };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx * 0.8f + dy * dy * 0.8f);
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.15f, d));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(pixels);
        tex.Apply();
        return vignetteSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    // ─── Update ───────────────────────────────────────────────────────────────

    private void Update()
    {
        if (CombatManager.Instance != null && !subscribedKills)
        {
            CombatManager.Instance.Killed += HandleKilled;
            subscribedKills = true;
        }
        var local = Player.Local;
        if (local != subscribed)
        {
            if (subscribed != null) subscribed.Health.Damaged -= HandleDamaged;
            subscribed = local;
            if (local != null) local.Health.Damaged += HandleDamaged;
        }

        UpdateKill();
        UpdateCallout();
        UpdateLowHealth(local);
        UpdateArcs(local);
    }

    private bool subscribedKills;

    // ─── Kills ────────────────────────────────────────────────────────────────

    private void HandleKilled(KillInfo kill)
    {
        int me = PhotonNetwork.LocalPlayer.ActorNumber;
        string killerName = Name(kill.Killer);

        if (!kill.SelfDestruct && kill.Killer == me && kill.Victim != me)
        {
            killAt = Time.unscaledTime;
            killMarker.gameObject.SetActive(true);
            killText.text = $"<color=#FF4D6D>ELIMINATED</color>  {Name(kill.Victim)}{(kill.Headshot ? "  <size=70%><color=#FFC93C>HEADSHOT</color></size>" : "")}";
            Sfx.Here(SfxKind.Snap, 0.8f, 0.7f);
            StartCoroutine(HitStop());

            // Multi-kill beats a streak call-out of the same moment
            string multiName  = KillStreaks.MultiName(KillStreaks.Multi(me));
            string streakName = KillStreaks.StreakName(KillStreaks.Streak(me));
            if (multiName != null) Callout(multiName, streakName != null ? $"{streakName} · {KillStreaks.Streak(me)} in a row" : null);
            else if (streakName != null) Callout(streakName, $"{KillStreaks.Streak(me)} kills in a row");
            return;
        }

        // Everyone hears about big streaks (and about someone ending one)
        if (!kill.SelfDestruct)
        {
            string streakName = KillStreaks.StreakName(KillStreaks.Streak(kill.Killer));
            if (streakName != null && KillStreaks.Streak(kill.Killer) >= 5 && HUDManager.Instance != null)
                HUDManager.Instance.ShowBanner($"{killerName} is on a {Title(streakName)}!", 2.5f);
        }
    }

    private static string Title(string upper) =>
        System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(upper.ToLowerInvariant());

    private static string Name(int actor)
    {
        var p = PhotonNetwork.CurrentRoom?.GetPlayer(actor);
        return p != null && !string.IsNullOrEmpty(p.NickName) ? p.NickName : $"Player {actor}";
    }

    // A brief freeze on the killer's screen: the shot lands with weight
    private IEnumerator HitStop()
    {
        if (hitStopping || PauseMenuManager.IsPaused) yield break;
        hitStopping = true;
        Time.timeScale = 0.1f;
        yield return new WaitForSecondsRealtime(0.06f);
        Time.timeScale = 1f;
        hitStopping = false;
    }

    private void UpdateKill()
    {
        float age = Time.unscaledTime - killAt;
        bool on = age < 1.4f;
        if (killMarker.gameObject.activeSelf != (age < 0.45f)) killMarker.gameObject.SetActive(age < 0.45f);
        if (age < 0.45f)
        {
            float pop = age < 0.08f ? Mathf.Lerp(1.8f, 1f, age / 0.08f) : 1f;
            killMarker.localScale = Vector3.one * pop;
            killMarker.localRotation = Quaternion.Euler(0f, 0f, age * 40f);
        }
        killText.gameObject.SetActive(on);
        if (on)
        {
            killText.alpha = Mathf.Clamp01((1.4f - age) / 0.3f);
            float pop = age < 0.12f ? 1f + (0.12f - age) * 2.5f : 1f;
            killText.rectTransform.localScale = Vector3.one * pop;
        }
    }

    private void Callout(string text, string sub)
    {
        calloutAt = Time.unscaledTime;
        callout.text = text;
        calloutSub.text = sub ?? "";
        Sfx.Here(SfxKind.Fanfare, 0.6f, 1.15f);
    }

    private void UpdateCallout()
    {
        float age = Time.unscaledTime - calloutAt;
        bool on = age < 2.2f;
        callout.gameObject.SetActive(on);
        calloutSub.gameObject.SetActive(on);
        if (!on) return;
        float scale = age < 0.15f ? Mathf.Lerp(2.2f, 1f, age / 0.15f) : 1f + Mathf.Sin(age * 8f) * 0.02f;
        callout.rectTransform.localScale = Vector3.one * scale;
        float alpha = Mathf.Clamp01((2.2f - age) / 0.4f);
        callout.alpha = alpha;
        calloutSub.alpha = alpha;
        callout.color = Color.Lerp(Theme.primary, Theme.pink, Mathf.PingPong(Time.unscaledTime * 3f, 1f));
        callout.alpha = alpha;
    }

    // ─── Low health ───────────────────────────────────────────────────────────

    private void UpdateLowHealth(Player local)
    {
        bool low = local != null && !local.IsCaught && local.Health.Fraction > 0f && local.Health.Fraction <= LowHealth;
        float severity = low ? 1f - local.Health.Fraction / LowHealth : 0f; // 0 at 30%, 1 near 0

        // Heartbeat, faster as health drops; the vignette pulses with it
        float beatGap = Mathf.Lerp(1.0f, 0.55f, severity);
        if (low && Time.unscaledTime >= nextBeat)
        {
            nextBeat = Time.unscaledTime + beatGap;
            Sfx.Here(SfxKind.Heartbeat, Mathf.Lerp(0.45f, 0.8f, severity));
        }
        float phase = low ? 1f - Mathf.Clamp01((nextBeat - Time.unscaledTime) / beatGap) : 0f;
        float pulse = low ? Mathf.Lerp(0.35f, 0.6f, severity) * (0.7f + 0.3f * Mathf.Exp(-phase * 6f)) : 0f;
        var c = vignette.color;
        c.a = Mathf.MoveTowards(c.a, pulse, Time.unscaledDeltaTime * 2f);
        vignette.color = c;

        // Muffled hearing (a low-pass on the listener)
        if (muffle == null)
        {
            var listener = FindAnyObjectByType<AudioListener>();
            if (listener != null && !listener.TryGetComponent(out muffle)) muffle = listener.gameObject.AddComponent<AudioLowPassFilter>();
        }
        if (muffle != null)
        {
            float target = low ? Mathf.Lerp(3500f, 1200f, severity) : 22000f;
            muffle.cutoffFrequency = Mathf.Lerp(muffle.cutoffFrequency, target, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
        }
    }

    // ─── Hit direction ────────────────────────────────────────────────────────

    private void HandleDamaged(float amount, int attacker)
    {
        var local = Player.Local;
        if (local == null) return;
        Vector3 shot = local.Health.LastHitDirection;
        if (shot.sqrMagnitude < 0.0001f) return;

        // Where the shooter is: back along the shot. One arc per direction (a new hit from near the same way refreshes it)
        Vector3 from = -shot.normalized;
        foreach (var existing in arcs)
            if (Time.unscaledTime - existing.At < 1.4f && Vector3.Angle(existing.From, from) < 25f)
            {
                existing.From = from;
                existing.At = Time.unscaledTime;
                return;
            }

        Arc arc = arcs.Find(a => Time.unscaledTime - a.At >= 1.4f);
        if (arc == null)
        {
            var holder = UIKit.Rect("HitArc", root);
            holder.anchorMin = holder.anchorMax = holder.pivot = new Vector2(0.5f, 0.5f);
            var img = UIKit.Panel(holder, new Color(1f, 0.15f, 0.2f), 8f, "Bar");
            img.raycastTarget = false;
            img.rectTransform.anchorMin = img.rectTransform.anchorMax = img.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            img.rectTransform.sizeDelta = new Vector2(150f, 16f);
            img.rectTransform.anchoredPosition = new Vector2(0f, 170f);
            arc = new Arc { Rect = holder, Image = img };
            arcs.Add(arc);
        }
        arc.From = from;
        arc.At = Time.unscaledTime;
    }

    private void UpdateArcs(Player local)
    {
        var cam = PlayerCam.Local;
        foreach (var arc in arcs)
        {
            float age = Time.unscaledTime - arc.At;
            bool on = age < 1.4f && local != null && cam != null;
            if (arc.Rect.gameObject.activeSelf != on) arc.Rect.gameObject.SetActive(on);
            if (!on) continue;

            // The source's direction in the view's flat frame: 0° = ahead (arc at the top), 90° = right
            Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            Vector3 to  = Vector3.ProjectOnPlane(arc.From, Vector3.up).normalized;
            float angle = Vector3.SignedAngle(fwd, to, Vector3.up);
            arc.Rect.localRotation = Quaternion.Euler(0f, 0f, -angle);
            var c = arc.Image.color;
            c.a = Mathf.Clamp01((1.4f - age) / 0.5f) * 0.85f;
            arc.Image.color = c;
        }
    }
}
