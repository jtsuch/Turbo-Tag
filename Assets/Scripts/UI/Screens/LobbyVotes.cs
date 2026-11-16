using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Hashtable    = ExitGames.Client.Photon.Hashtable;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// Lobby wishes (Justin, Nov 16): players who aren't the host browse the maps and game modes too, and clicking one
/// marks it as what they'd like to play. Everyone (the host included) sees the marks: the card is outlined in the
/// colour of the first player who picked it, with a bubble per player (their initial, in their penguin's colour).
/// Clicking your pick again, or another card, takes your mark off it. Only the host actually chooses.
///
/// Each player's pick is their own player property (one map and one mode each, with the time picked, so "first" is
/// the same on every screen); the host clears them all when a match starts. <see cref="VoteBadge"/> draws it on a card.
/// </summary>
public static class LobbyVotes
{
    public enum Kind { Map, Mode }

    private static string Key(Kind kind)  => kind == Kind.Map ? PlayerKeys.VoteMap : PlayerKeys.VoteMode;
    private static string When(Kind kind) => kind == Kind.Map ? PlayerKeys.VoteMapAt : PlayerKeys.VoteModeAt;

    /// <summary>Bumped whenever anyone's picks change (or someone leaves), so badges know to redraw.</summary>
    public static int Version { get; private set; }

    private static Listener listener;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (listener != null) PhotonNetwork.RemoveCallbackTarget(listener);
        listener = new Listener();
        PhotonNetwork.AddCallbackTarget(listener);
    }

    /// <summary>The local player's pick of this kind ("" none).</summary>
    public static string Mine(Kind kind) =>
        PhotonNetwork.InRoom ? NetProps.Of(PhotonNetwork.LocalPlayer, Key(kind), "") : "";

    /// <summary>Local player: marks <paramref name="value"/>, or takes the mark off it if it's already theirs.</summary>
    public static void Toggle(Kind kind, string value)
    {
        if (!PhotonNetwork.InRoom || string.IsNullOrEmpty(value)) return;
        bool off = Mine(kind) == value;
        NetProps.SetLocal(new Hashtable { [Key(kind)] = off ? "" : value, [When(kind)] = off ? 0d : PhotonNetwork.Time });
        UISound.Play(off ? UISound.Kind.Back : UISound.Kind.Confirm);
        Version++;   // Redraw at once on this screen (the echo bumps it again)
    }

    /// <summary>Everyone who picked <paramref name="value"/>, first pick first.</summary>
    public static List<PhotonPlayer> Voters(Kind kind, string value)
    {
        var list = new List<PhotonPlayer>();
        if (!PhotonNetwork.InRoom || string.IsNullOrEmpty(value)) return list;
        foreach (var p in PhotonNetwork.PlayerList)
            if (NetProps.Of(p, Key(kind), "") == value) list.Add(p);
        list.Sort((a, b) =>
        {
            int byTime = NetProps.Of(a, When(kind), 0d).CompareTo(NetProps.Of(b, When(kind), 0d));
            return byTime != 0 ? byTime : a.ActorNumber.CompareTo(b.ActorNumber);
        });
        return list;
    }

    /// <summary>Host: everyone's picks off (a match is starting: the next lobby starts clean).</summary>
    public static void ClearAll()
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;
        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (NetProps.Of(p, PlayerKeys.VoteMap, "") == "" && NetProps.Of(p, PlayerKeys.VoteMode, "") == "") continue;
            p.SetCustomProperties(new Hashtable
            {
                [PlayerKeys.VoteMap] = "", [PlayerKeys.VoteMapAt] = 0d,
                [PlayerKeys.VoteMode] = "", [PlayerKeys.VoteModeAt] = 0d,
            });
        }
    }

    /// <summary>A player's penguin colour as UI ink: the black penguin reads as light grey on the dark UI.</summary>
    public static Color ColorOf(PhotonPlayer player)
    {
        string skin = PenguinSkins.Of(player);
        Color c = Color.grey;
        foreach (var s in PenguinSkins.All) if (s.Name == skin) { c = s.Color; break; }
        float light = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
        return light < 0.2f ? new Color(0.78f, 0.78f, 0.82f) : c;
    }

    public static string Initial(PhotonPlayer player)
    {
        string name = player != null ? player.NickName : "";
        foreach (char ch in name ?? "") if (char.IsLetterOrDigit(ch)) return char.ToUpperInvariant(ch).ToString();
        return "?";
    }

    private sealed class Listener : IInRoomCallbacks
    {
        public void OnPlayerPropertiesUpdate(PhotonPlayer target, Hashtable changed)
        {
            if (changed.ContainsKey(PlayerKeys.VoteMap) || changed.ContainsKey(PlayerKeys.VoteMode) || changed.ContainsKey(PlayerKeys.Skin)) Version++;
        }
        public void OnPlayerLeftRoom(PhotonPlayer otherPlayer) => Version++;
        public void OnPlayerEnteredRoom(PhotonPlayer newPlayer) { }
        public void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged) { }
        public void OnMasterClientSwitched(PhotonPlayer newMasterClient) { }
    }
}

/// <summary>
/// On a map or mode card: the outline in the first voter's colour and a row of voter bubbles in its top-right corner
/// (<see cref="LobbyVotes"/>). Redraws itself whenever anyone's picks change.
/// </summary>
public class VoteBadge : MonoBehaviour
{
    private LobbyVotes.Kind kind;
    private string value;
    private Outline outline;
    private RectTransform bubbles;
    private int drawn = -1;

    public static VoteBadge Add(Image card, LobbyVotes.Kind kind, string value)
    {
        var badge = card.gameObject.AddComponent<VoteBadge>();
        badge.kind = kind;
        badge.value = value;
        badge.outline = card.gameObject.AddComponent<Outline>();
        badge.outline.effectDistance = new Vector2(5f, -5f);
        badge.outline.useGraphicAlpha = false;
        badge.outline.enabled = false;   // Off rather than zero-sized: a zero-sized Outline still tints the card

        badge.bubbles = UIKit.Rect("Votes", card.transform);
        badge.bubbles.anchorMin = badge.bubbles.anchorMax = new Vector2(1f, 1f);
        badge.bubbles.pivot = new Vector2(1f, 1f);
        badge.bubbles.anchoredPosition = new Vector2(-10f, -10f);
        badge.bubbles.sizeDelta = new Vector2(300f, 40f);
        var row = badge.bubbles.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.childAlignment = TextAnchor.UpperRight;
        row.spacing = -8f;   // Overlapping a little, like stacked avatars
        row.childControlWidth = row.childControlHeight = false;
        row.childForceExpandWidth = row.childForceExpandHeight = false;
        row.reverseArrangement = false;
        var ignore = badge.bubbles.gameObject.AddComponent<LayoutElement>();
        ignore.ignoreLayout = true;   // The card's own layout leaves it alone
        return badge;
    }

    private void OnEnable() => drawn = -1;

    private void Update()
    {
        if (drawn == LobbyVotes.Version) return;
        drawn = LobbyVotes.Version;
        Redraw();
    }

    private void Redraw()
    {
        var voters = LobbyVotes.Voters(kind, value);
        outline.enabled = voters.Count > 0;
        if (voters.Count > 0) outline.effectColor = LobbyVotes.ColorOf(voters[0]);

        for (int i = bubbles.childCount - 1; i >= 0; i--) Destroy(bubbles.GetChild(i).gameObject);
        foreach (var p in voters)
        {
            Color c = LobbyVotes.ColorOf(p);
            var bubble = UIKit.Rect("Vote", bubbles).gameObject.AddComponent<Image>();
            bubble.sprite = UISprites.Circle;
            bubble.color = c;
            bubble.raycastTarget = false;
            bubble.rectTransform.sizeDelta = new Vector2(40f, 40f);
            var edge = bubble.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(0.08f, 0.05f, 0.2f, 0.9f);
            edge.effectDistance = new Vector2(2f, -2f);
            float light = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
            var letter = UIKit.Text(bubble.transform, LobbyVotes.Initial(p), 24f, UIKit.Font.Display,
                light > 0.6f ? UITheme.Current.textDark : Color.white, TextAlignmentOptions.Center);
            UIKit.Stretch(letter.rectTransform);
            letter.raycastTarget = false;
        }
    }
}
