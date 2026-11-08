using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The hunter's tool on the HUD, left of the ability bar: the class colour, the tool's name, a "LMB" key chip, a
/// cooldown sweep with seconds, and the passive underneath. Shows for hunters in modes with classes once the
/// setup is over. Its own canvas, built in code; added next to the GameModeManager.
/// </summary>
public class HunterToolHud : MonoBehaviour
{
    private GameObject root;
    private TMP_Text hunterInfo;
    private bool cooling;
    private float popAt = -99f;
    private Image stripe, sweep;
    private TMP_Text toolName, className, seconds, passive, key;

    private void Start()
    {
        var theme = UITheme.Current;
        var canvas = UIKit.Canvas("Hunter Tool Canvas", 1);
        canvas.transform.SetParent(transform, false);

        var card = UIKit.Card(canvas.transform, UIKit.WithAlpha(theme.panel, 0.92f), 18f, 0.4f, "Hunter Tool");
        UIKit.Place(card, new Vector2(0.5f, 0f), new Vector2(-480f, 26f), new Vector2(250f, 116f));
        root = card.gameObject;

        stripe = UIKit.Panel(card, Color.white, 6f, "Stripe");
        stripe.rectTransform.anchorMin = new Vector2(0f, 0f);
        stripe.rectTransform.anchorMax = new Vector2(0f, 1f);
        stripe.rectTransform.pivot = new Vector2(0f, 0.5f);
        stripe.rectTransform.offsetMin = new Vector2(8f, 10f);
        stripe.rectTransform.offsetMax = new Vector2(16f, -10f);

        var chip = UIKit.Chip(card, "LMB", theme.card, theme.text, 15f);
        UIKit.Place(chip.rectTransform, new Vector2(1f, 1f), new Vector2(-10f, -10f), new Vector2(52f, 24f));
        key = chip.GetComponentInChildren<TMP_Text>();

        className = UIKit.Text(card, "", 15f, UIKit.Font.Bold, theme.textMuted, TextAlignmentOptions.TopLeft);
        UIKit.Place(className.rectTransform, new Vector2(0f, 1f), new Vector2(26f, -10f), new Vector2(160f, 22f));
        toolName = UIKit.OneLine(UIKit.Text(card, "", 26f, UIKit.Font.Display, theme.text, TextAlignmentOptions.TopLeft));
        UIKit.Place(toolName.rectTransform, new Vector2(0f, 1f), new Vector2(26f, -32f), new Vector2(210f, 34f));
        passive = UIKit.Text(card, "", 14f, UIKit.Font.Body, theme.textMuted, TextAlignmentOptions.TopLeft);
        UIKit.Place(passive.rectTransform, new Vector2(0f, 1f), new Vector2(26f, -70f), new Vector2(214f, 40f));

        // Cooldown: a dark sweep over the card and the seconds left
        sweep = UIKit.Rect("Cooldown", card).gameObject.AddComponent<Image>();
        UIKit.Stretch(sweep.rectTransform);
        sweep.sprite = UISprites.Rounded;
        sweep.type = Image.Type.Filled;
        sweep.fillMethod = Image.FillMethod.Horizontal;
        sweep.fillOrigin = 1;
        sweep.color = new Color(0f, 0f, 0f, 0.5f);
        sweep.raycastTarget = false;
        seconds = UIKit.Text(card, "", 34f, UIKit.Font.Display, theme.text, TextAlignmentOptions.Center);
        UIKit.Stretch(seconds.rectTransform);
        root.SetActive(false);

        // For hiders: which class the hunters took (under the hiders-left line)
        hunterInfo = UIKit.Text(canvas.transform, "", 20f, UIKit.Font.Bold, theme.textMuted, TextAlignmentOptions.Center);
        UIKit.Place(hunterInfo.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -208f), new Vector2(600f, 30f));
        hunterInfo.textWrappingMode = TextWrappingModes.NoWrap;
    }

    private float nextInfo;

    private void UpdateHunterInfo(GameModeManager manager, Player local)
    {
        if (Time.unscaledTime < nextInfo) return;
        nextInfo = Time.unscaledTime + 0.5f;
        bool show = manager != null && manager.Mode != null && manager.Mode.HasHunterClasses && local != null
                    && local.Role != PlayerRole.Hunter && manager.Phase == MatchPhase.Active;
        if (!show) { hunterInfo.text = ""; return; }
        var seen = new System.Collections.Generic.List<string>();
        foreach (var p in Photon.Pun.PhotonNetwork.PlayerList)
        {
            if (PlayerRoles.Get(p) != PlayerRole.Hunter) continue;
            var cls = HunterClasses.Find(NetProps.Of(p, PlayerKeys.HunterClass, ""));
            if (cls == null) continue;
            string tag = $"<color=#{ColorUtility.ToHtmlStringRGB(cls.Color)}>{cls.Name.ToUpperInvariant()}</color>";
            if (!seen.Contains(tag)) seen.Add(tag);
        }
        hunterInfo.text = seen.Count == 0 ? "" : $"HUNTER CLASS  {string.Join("  ·  ", seen)}";
    }

    private void Update()
    {
        var manager = GameModeManager.Instance;
        var mode = manager != null ? manager.Mode : null;
        var local = Player.Local;
        var kit = HunterKit.Local;
        UpdateHunterInfo(manager, local);
        bool show = mode != null && mode.HasHunterClasses && local != null && kit != null && local.Role == PlayerRole.Hunter
                    && (manager.Phase == MatchPhase.Hiding || manager.Phase == MatchPhase.Active) && !HunterClassPicker.IsOpen;
        if (root.activeSelf != show) root.SetActive(show);
        if (!show) return;

        var cls = HunterClasses.Chosen;
        var tool = HunterClasses.ChosenTool;
        stripe.color = cls.Color;
        className.text = cls.Name.ToUpperInvariant();
        className.color = cls.Color;
        toolName.text = tool.Name;
        passive.text = cls.Passive;
        key.text = Keybinds.Label(Keybinds.Get(InputAction.HunterTool));

        float left = kit.CooldownLeft;
        // Ready again: a little pop and a tick
        if (cooling && left <= 0f) { popAt = Time.unscaledTime; Sfx.Here(SfxKind.Tick, 0.35f, 1.9f); }
        cooling = left > 0f;
        float pop = Mathf.Clamp01(1f - (Time.unscaledTime - popAt) / 0.3f);
        root.transform.localScale = Vector3.one * (1f + 0.12f * pop);
        sweep.fillAmount = left > 0f ? left / Mathf.Max(0.01f, kit.CooldownTotal) : 0f;
        seconds.text = kit.Scanning ? "<size=60%>SCANNING</size>" : left > 0f ? Mathf.CeilToInt(left).ToString() : "";
    }
}
