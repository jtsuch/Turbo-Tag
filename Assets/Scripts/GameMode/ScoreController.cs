using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// Cumulative per-player score (seconds survived as a hider in Tag).
///
/// Banked totals live in each player's "Score" property, written only by the master client
/// at discrete moments (tagged, round end). The live score adds the current phase's elapsed
/// time while the game mode says the player is scoring — a pure function of synced state, so
/// every client shows the same ticking number without per-frame network traffic.
/// Attach to: the same GameObject as GameModeManager.
/// </summary>
public class ScoreController : MonoBehaviour
{
    private GameModeManager manager;

    private void Awake() => manager = GetComponent<GameModeManager>();

    // ─── Queries ──────────────────────────────────────────────────────────────

    public float GetBanked(PhotonPlayer player) => NetProps.Of(player, PlayerKeys.Score, 0f);

    public bool IsScoring(PhotonPlayer player) =>
        manager.Mode != null
        && manager.Mode.IsScoring(PlayerRoles.Get(player), manager.Phase);

    public float GetLive(PhotonPlayer player)
    {
        float score = GetBanked(player);
        if (IsScoring(player))
        {
            var clock = manager.Clock;
            score += clock.HasLimit ? Mathf.Min(clock.Elapsed, clock.Duration) : clock.Elapsed;
        }
        return score;
    }

    public PhotonPlayer GetLeader()
    {
        PhotonPlayer leader = null;
        float best = float.MinValue;
        foreach (var player in PhotonNetwork.PlayerList)
        {
            float score = GetLive(player);
            if (score > best) { best = score; leader = player; }
        }
        return leader;
    }

    public float GetLeaderScore()
    {
        var leader = GetLeader();
        return leader != null ? GetLive(leader) : 0f;
    }

    /// <summary>All players sorted by live score, highest first.</summary>
    public List<(PhotonPlayer player, float score)> GetStandings()
    {
        var standings = new List<(PhotonPlayer, float)>();
        foreach (var player in PhotonNetwork.PlayerList)
            standings.Add((player, GetLive(player)));
        standings.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        return standings;
    }

    // ─── Master-client API ────────────────────────────────────────────────────

    /// <summary>Master: locks in the player's live score (call before their role changes).</summary>
    public void Bank(PhotonPlayer player) =>
        NetProps.SetPlayer(player, PlayerKeys.Score, GetLive(player));

    /// <summary>Master: adds a flat bonus (may be negative) to the player's banked score.</summary>
    public void Add(PhotonPlayer player, float amount) =>
        NetProps.SetPlayer(player, PlayerKeys.Score, Mathf.Max(0f, GetBanked(player) + amount));

    public void ResetAll()
    {
        foreach (var player in PhotonNetwork.PlayerList)
            NetProps.SetPlayer(player, PlayerKeys.Score, 0f);
    }
}
