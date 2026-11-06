using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// HUD: tag-mode feel (modes with roles):
///   - a slammed "TAGGED!" stamp when you're caught, "+1 TAG" when you catch someone;
///   - a heartbeat (faster as they get closer) and a dark red edge pulse when a hunter is within
///     <see cref="Danger"/> metres of a non-hunter.
/// Part of the HUD canvas.
/// </summary>
public class TagFeel : MonoBehaviour
{
    private const float Danger = 10f;

    private TMP_Text stamp, stampSub;
    private float    stampAt = -10f;
    private Image    edge;
    private float    nextBeat;
    private GameModeManager subscribed;

    public static TagFeel Create(RectTransform hudRoot)
    {
        var rect = UIKit.Rect("TagFeel", hudRoot);
        UIKit.Stretch(rect);
        var feel = rect.gameObject.AddComponent<TagFeel>();
        feel.Build(rect);
        return feel;
    }

    private void Build(RectTransform rect)
    {
        var theme = UITheme.Current;
        edge = new GameObject("Danger", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        edge.transform.SetParent(rect, false);
        UIKit.Stretch(edge.rectTransform);
        edge.sprite = CombatFeedback.VignetteSprite();
        edge.color = new Color(0.5f, 0f, 0.05f, 0f);
        edge.raycastTarget = false;
        edge.transform.SetAsFirstSibling();

        stamp = UIKit.OneLine(UIKit.Text(rect, "", 130f, UIKit.Font.Display, theme.danger, TextAlignmentOptions.Center, "Stamp"));
        UIKit.Place(stamp.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(1400f, 160f));
        stamp.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        stamp.rectTransform.anchoredPosition = new Vector2(0f, 60f);
        stamp.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(6f, -6f);
        stampSub = UIKit.OneLine(UIKit.Text(rect, "", 32f, UIKit.Font.Bold, theme.text, TextAlignmentOptions.Center, "StampSub"));
        UIKit.Place(stampSub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(1200f, 44f));
        stampSub.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        stampSub.rectTransform.anchoredPosition = new Vector2(0f, -40f);
        stamp.gameObject.SetActive(false);
        stampSub.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (subscribed != null) subscribed.PlayerTagged -= HandleTagged;
    }

    private void Update()
    {
        var manager = GameModeManager.Instance;
        if (manager != subscribed)
        {
            if (subscribed != null) subscribed.PlayerTagged -= HandleTagged;
            subscribed = manager;
            if (manager != null) manager.PlayerTagged += HandleTagged;
        }
        UpdateStamp();
        UpdateDanger(manager);
    }

    // ─── Stamp ────────────────────────────────────────────────────────────────

    private void HandleTagged(PhotonPlayer tagger, PhotonPlayer target)
    {
        var mode = GameModeManager.Instance != null ? GameModeManager.Instance.Mode : null;
        if (mode == null || !mode.AnnouncesTags || target == null) return;

        if (target.IsLocal)
            Stamp("TAGGED!", UITheme.Current.danger, tagger != null && !tagger.IsLocal ? $"by {tagger.NickName}" : "");
        else if (tagger != null && tagger.IsLocal)
        {
            Stamp("+1 TAG", UITheme.Current.lime, $"You caught {target.NickName}!");
            Sfx.Here(SfxKind.Kill, 0.8f);
        }
    }

    private void Stamp(string text, Color color, string sub)
    {
        stampAt = Time.unscaledTime;
        stamp.text = text;
        stamp.color = color;
        stampSub.text = sub;
        stamp.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-9f, -4f));
        Fx.Shake(0.3f);
    }

    private void UpdateStamp()
    {
        float age = Time.unscaledTime - stampAt;
        bool on = age < 1.8f;
        if (stamp.gameObject.activeSelf != on) { stamp.gameObject.SetActive(on); stampSub.gameObject.SetActive(on); }
        if (!on) return;
        float scale = age < 0.14f ? Mathf.Lerp(2.6f, 1f, age / 0.14f) : 1f;
        stamp.rectTransform.localScale = Vector3.one * scale;
        float alpha = Mathf.Clamp01((1.8f - age) / 0.4f);
        stamp.alpha = alpha;
        stampSub.alpha = alpha;
    }

    // ─── A hunter is near ─────────────────────────────────────────────────────

    private void UpdateDanger(GameModeManager manager)
    {
        float closeness = 0f; // 0 = no hunter within range, 1 = right on you
        var local = Player.Local;
        if (manager != null && manager.Mode != null && manager.Mode.UsesRoles && local != null && !local.IsCaught
            && local.Role != PlayerRole.Hunter && local.Role != PlayerRole.Ghost && manager.Phase == MatchPhase.Active)
        {
            float nearest = float.MaxValue;
            foreach (var p in Player.All)
                if (p != null && p != local && p.Role == PlayerRole.Hunter && !p.IsDownAfterTag)
                    nearest = Mathf.Min(nearest, Vector3.Distance(p.transform.position, local.transform.position));
            if (nearest < Danger) closeness = 1f - nearest / Danger;
        }

        float gap = Mathf.Lerp(1.1f, 0.42f, closeness);
        if (closeness > 0f && Time.unscaledTime >= nextBeat)
        {
            nextBeat = Time.unscaledTime + gap;
            Sfx.Here(SfxKind.Heartbeat, Mathf.Lerp(0.35f, 0.85f, closeness));
        }
        float phase = closeness > 0f ? 1f - Mathf.Clamp01((nextBeat - Time.unscaledTime) / gap) : 1f;
        float target = closeness > 0f ? Mathf.Lerp(0.15f, 0.5f, closeness) * (0.6f + 0.4f * Mathf.Exp(-phase * 5f)) : 0f;
        var c = edge.color;
        c.a = Mathf.MoveTowards(c.a, target, Time.unscaledDeltaTime * 2f);
        edge.color = c;
    }
}
