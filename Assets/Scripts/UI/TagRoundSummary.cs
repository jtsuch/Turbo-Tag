using Photon.Pun;
using TMPro;
using UnityEngine;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// Tag's round feedback, on every screen (its own canvas, built in code):
///   - "N HIDERS LEFT" under the clock during the hunt;
///   - the last-hider moment: when one hider remains, a banner with their name, a horn and a shake (and the hunters'
///     radar speeds up, <see cref="Minimap"/>);
///   - between rounds, a summary card: who lasted longest, who tagged most, and the closest call (the nearest a
///     hunter got without catching someone).
/// Reads only synced properties. Added next to the GameModeManager.
/// </summary>
public class TagRoundSummary : MonoBehaviour
{
    private TMP_Text counter, summaryText;
    private RectTransform card;
    private int lastCount = -1;
    private MatchPhase lastPhase;
    private float showSummaryAt = -1f;

    /// <summary>Tag hiders still free (role Hider). 0 outside Tag.</summary>
    public static int HidersLeft()
    {
        var manager = GameModeManager.Instance;
        if (manager == null || manager.Mode is not TagMode) return 0;
        int n = 0;
        foreach (var p in PhotonNetwork.PlayerList) if (PlayerRoles.Get(p) == PlayerRole.Hider) n++;
        return n;
    }

    private void Start()
    {
        var theme = UITheme.Current;
        var canvas = UIKit.Canvas("Tag Round Canvas", 3);
        canvas.transform.SetParent(transform, false);

        counter = UIKit.Text(canvas.transform, "", 24f, UIKit.Font.Display, theme.text, TextAlignmentOptions.Center);
        UIKit.Place(counter.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -176f), new Vector2(420f, 34f));
        counter.textWrappingMode = TextWrappingModes.NoWrap;

        card = UIKit.Card(canvas.transform, UIKit.WithAlpha(theme.panel, 0.94f), 26f, 0.5f, "Round Summary");
        UIKit.Place(card, new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(620f, 300f));
        summaryText = UIKit.Text(card, "", 26f, UIKit.Font.Body, theme.text, TextAlignmentOptions.Center);
        UIKit.Stretch(summaryText.rectTransform, 28f, 28f, 24f, 24f);
        summaryText.richText = true;
        card.gameObject.SetActive(false);
    }

    private void Update()
    {
        var manager = GameModeManager.Instance;
        bool tag = manager != null && manager.Mode is TagMode;
        var phase = tag ? manager.Phase : MatchPhase.Waiting;

        // Hiders left, and the moment only one remains
        int left = tag && phase == MatchPhase.Active ? HidersLeft() : -1;
        counter.text = left > 0 ? (left == 1 ? "<color=#FFD23F>LAST HIDER</color>" : $"{left} HIDERS LEFT") : "";
        if (left == 1 && lastCount > 1) LastHider();
        lastCount = left;

        // Between rounds: the summary card (a moment in, so the master's last writes have arrived)
        if (phase != lastPhase)
        {
            card.gameObject.SetActive(false);
            if (phase == MatchPhase.RoundEnd) showSummaryAt = Time.unscaledTime + 0.6f;
            lastPhase = phase;
        }
        if (showSummaryAt > 0f && Time.unscaledTime >= showSummaryAt)
        {
            showSummaryAt = -1f;
            if (phase == MatchPhase.RoundEnd) ShowSummary(manager.Round);
        }
    }

    private void LastHider()
    {
        string name = "";
        foreach (var p in PhotonNetwork.PlayerList) if (PlayerRoles.Get(p) == PlayerRole.Hider) name = p.NickName;
        bool me = PlayerRoles.Get(PhotonNetwork.LocalPlayer) == PlayerRole.Hider;
        if (HUDManager.Instance != null)
            HUDManager.Instance.ShowBanner(me ? "<size=70%>YOU'RE THE</size>\nLAST HIDER!" : $"<size=70%>LAST HIDER</size>\n{name}", 3f);
        Sfx.Here(SfxKind.Horn, 0.7f, 0.8f);
        if (PlayerCam.Local != null) PlayerCam.Local.AddShake(0.35f);
    }

    private void ShowSummary(int round)
    {
        PhotonPlayer longest = null, tagger = null, closest = null;
        float longestTime = 0f, closestDistance = float.MaxValue;
        int mostTags = 0;
        foreach (var p in PhotonNetwork.PlayerList)
        {
            float survived = NetProps.Of(p, PlayerKeys.RoundSurvived, 0f);
            if (survived > longestTime) { longestTime = survived; longest = p; }
            int tags = NetProps.Of(p, PlayerKeys.RoundTags, 0);
            if (tags > mostTags) { mostTags = tags; tagger = p; }
            float close = NetProps.Of(p, PlayerKeys.RoundClose, 0f);
            if (close > 0f && close < closestDistance) { closestDistance = close; closest = p; }
        }

        string muted = "#" + ColorUtility.ToHtmlStringRGB(UITheme.Current.textMuted);
        var lines = new System.Text.StringBuilder();
        lines.AppendLine($"<font-weight=700><size=120%>ROUND {round} OVER</size></font-weight>\n");
        if (longest != null) lines.AppendLine($"<color={muted}>Lasted longest</color>  <b>{longest.NickName}</b>  {MatchStats.FormatTime(longestTime)}");
        if (tagger != null)  lines.AppendLine($"<color={muted}>Most tags</color>  <b>{tagger.NickName}</b>  {mostTags}");
        if (closest != null) lines.AppendLine($"<color={muted}>Closest call</color>  <b>{closest.NickName}</b>  {closestDistance:0.0} m");
        if (longest == null && tagger == null && closest == null) lines.AppendLine($"<color={muted}>A quiet round.</color>");
        summaryText.text = lines.ToString();
        card.gameObject.SetActive(true);
    }
}
