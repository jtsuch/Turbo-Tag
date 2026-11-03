using Photon.Pun;
using UnityEngine;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// Per-player match stats for the scoreboard (tags, times tagged, longest hide, rounds survived,
/// abilities used). Stored as floats in player properties (<see cref="PlayerKeys"/> "Stat_" keys), so
/// they survive the move to the Scoreboard scene. The master writes everything except abilities used,
/// which each player counts for themself (AbilityHandler). All reset when a level loads.
/// </summary>
public static class MatchStats
{
    public static readonly string[] All =
    {
        PlayerKeys.StatTags, PlayerKeys.StatTimesTagged, PlayerKeys.StatLongestHide,
        PlayerKeys.StatRoundsSurvived, PlayerKeys.StatAbilitiesUsed,
    };

    public static float Get(PhotonPlayer player, string key) => NetProps.Of(player, key, 0f);

    /// <summary>Master: adds to a player's stat.</summary>
    public static void Add(PhotonPlayer player, string key, float amount = 1f) =>
        NetProps.SetPlayer(player, key, Get(player, key) + amount);

    /// <summary>Master: keeps the larger of the stored value and <paramref name="value"/>.</summary>
    public static void Max(PhotonPlayer player, string key, float value)
    {
        if (value > Get(player, key)) NetProps.SetPlayer(player, key, value);
    }

    /// <summary>"m:ss".</summary>
    public static string FormatTime(float seconds)
    {
        int total = Mathf.Max(0, Mathf.RoundToInt(seconds));
        return $"{total / 60}:{total % 60:D2}";
    }

    public static string Count(PhotonPlayer player, string key) => Mathf.RoundToInt(Get(player, key)).ToString();
}
