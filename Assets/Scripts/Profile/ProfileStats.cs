using System;
using System.Collections.Generic;
using Photon.Pun;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// The local player's lifetime profile (Justin, Nov 18): games, wins, per-mode records, combat and objective totals,
/// the abilities they bring most in each slot, and experience with a level. Shown in the Profile window
/// (<see cref="ProfilePanel"/>) on the main menu and in the lobby, and the XP a match earned on the Scoreboard.
///
/// <b>Recording:</b> each client records only itself, once per match, a moment into MatchEnd
/// (<see cref="ProfileRecorder"/>), from its own synced match stats (<see cref="MatchStats"/>, Score) and loadout. Practice
/// modes record nothing. Cheap: one JSON write per match.
///
/// <b>XP:</b> every mode pays the same base for playing and a win bonus, plus its own rate for the points it scores
/// (<see cref="GameMode.XpFromScore"/>, since a Tag point and a Free for All kill aren't worth the same), capped so no
/// single match is worth too much. The level curve is <see cref="XpToNext"/>.
///
/// <b>Storage:</b> PlayerPrefs (JSON, versioned) under the player's name, and for signed-in accounts a copy in PlayFab
/// user data ("Profile"), so it follows them to another PC; on sign-in the copy with more games wins
/// (<see cref="SyncWithCloud"/>). Guests keep theirs on this PC.
/// </summary>
public static class ProfileStats
{
    // ─── Data ─────────────────────────────────────────────────────────────────

    [Serializable] public class ModeRecord    { public string key; public int games; public int wins; public int xp; public float best; }
    [Serializable] public class AbilityRecord { public string id; public int slot; public int matches; }
    [Serializable] public class XpLine        { public string label; public int amount; }

    [Serializable]
    public class MatchSummary
    {
        public string mode;
        public bool   won;
        public int    xp;
        public int    levelBefore, levelAfter;
        public long   whenTicks;
        public List<XpLine> lines = new();
    }

    [Serializable]
    public class Data
    {
        public int   version = 1;
        public int   xp;
        public int   games, wins;
        public float seconds;
        public float kills, deaths, headshots, assists, knockOffs, damage;
        public float tags, timesTagged, longestHide, roundsSurvived;
        public float steals, catches, plants, defuses, donuts, abilitiesUsed;
        public List<ModeRecord>    modes     = new();
        public List<AbilityRecord> abilities = new();
        public MatchSummary        last;
    }

    private const string PrefsPrefix = "Profile_";
    private const string CloudKey    = "Profile";
    private const int    MaxLevel    = 100;

    private static Data data;
    private static string loadedFor;

    /// <summary>Raised after the profile changes (a match recorded, the cloud copy arrived).</summary>
    public static event Action Changed;

    /// <summary>The local player's profile (loaded on first use, for whoever is playing on this PC now).</summary>
    public static Data Current
    {
        get
        {
            string owner = Owner;
            if (data == null || loadedFor != owner) Load(owner);
            return data;
        }
    }

    // Saved per player name, so a shared PC keeps each player's profile apart
    private static string Owner => string.IsNullOrEmpty(PhotonNetwork.NickName) ? "Local" : PhotonNetwork.NickName;

    private static void Load(string owner)
    {
        loadedFor = owner;
        data = Parse(PlayerPrefs.GetString(PrefsPrefix + owner, "")) ?? new Data();
    }

    private static Data Parse(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonUtility.FromJson<Data>(json); }
        catch (Exception e) { Debug.LogWarning($"[Profile] Couldn't read a saved profile: {e.Message}"); return null; }
    }

    private static void Save(bool cloud)
    {
        string json = JsonUtility.ToJson(Current);
        PlayerPrefs.SetString(PrefsPrefix + loadedFor, json);
        PlayerPrefs.Save();
        if (cloud) PushToCloud(json);
        Changed?.Invoke();
    }

    // ─── Levels ───────────────────────────────────────────────────────────────

    /// <summary>XP from level <paramref name="level"/> to the next: 300 at level 1, 75 more each level.</summary>
    public static int XpToNext(int level) => 300 + 75 * (Mathf.Max(1, level) - 1);

    /// <summary>The level for a lifetime XP total, and how far into it (XP into this level, XP this level needs).</summary>
    public static int LevelFor(int xp, out int into, out int need)
    {
        int level = 1;
        into = Mathf.Max(0, xp);
        need = XpToNext(level);
        while (into >= need && level < MaxLevel)
        {
            into -= need;
            level++;
            need = XpToNext(level);
        }
        return level;
    }

    public static int Level => LevelFor(Current.xp, out _, out _);

    // ─── Favourites ───────────────────────────────────────────────────────────

    /// <summary>The mode with the most games ("" before the first match).</summary>
    public static string FavoriteMode()
    {
        ModeRecord best = null;
        foreach (var m in Current.modes) if (best == null || m.games > best.games) best = m;
        return best != null ? best.key : "";
    }

    /// <summary>The ability brought into the most matches in a slot ("" before the first).</summary>
    public static string FavoriteAbility(AbilitySlot slot, out int matches)
    {
        AbilityRecord best = null;
        foreach (var a in Current.abilities)
            if (a.slot == (int)slot && (best == null || a.matches > best.matches)) best = a;
        matches = best != null ? best.matches : 0;
        return best != null ? best.id : "";
    }

    // ─── Recording a match ────────────────────────────────────────────────────

    public const int XpForPlaying = 50, XpForWinning = 100, MaxScoreXp = 400;

    /// <summary>
    /// The local player's result for the match that just ended (every client records itself once, at MatchEnd).
    /// Practice modes record nothing.
    /// </summary>
    public static void RecordMatch(GameMode mode, string modeKey, float secondsPlayed)
    {
        var me = PhotonNetwork.LocalPlayer;
        if (mode == null || me == null || !mode.GivesXp) return;
        var d = Current;
        int levelBefore = Level;
        int players = PhotonNetwork.PlayerList.Length;
        bool won = players >= 2 && mode.DidWin(me);

        // XP: playing, winning, and the mode's own rate for its points
        var summary = new MatchSummary { mode = modeKey, won = won, levelBefore = levelBefore, whenTicks = DateTime.UtcNow.Ticks };
        summary.lines.Add(new XpLine { label = "Match played", amount = XpForPlaying });
        if (won) summary.lines.Add(new XpLine { label = "Victory", amount = XpForWinning });
        int scoreXp = Mathf.Clamp(Mathf.RoundToInt(mode.XpFromScore(me)), 0, MaxScoreXp);
        if (scoreXp > 0) summary.lines.Add(new XpLine { label = mode.XpScoreLabel, amount = scoreXp });
        foreach (var line in summary.lines) summary.xp += line.amount;

        // Totals
        d.games++;
        if (won) d.wins++;
        d.seconds        += Mathf.Max(0f, secondsPlayed);
        d.xp             += summary.xp;
        d.kills          += Stat(me, PlayerKeys.StatKills);
        d.deaths         += Stat(me, PlayerKeys.StatDeaths);
        d.headshots      += Stat(me, PlayerKeys.StatHeadshots);
        d.assists        += Stat(me, PlayerKeys.StatAssists);
        d.knockOffs      += Stat(me, PlayerKeys.StatKnockOffs);
        d.damage         += Stat(me, PlayerKeys.StatDamage);
        d.tags           += Stat(me, PlayerKeys.StatTags);
        d.timesTagged    += Stat(me, PlayerKeys.StatTimesTagged);
        d.longestHide     = Mathf.Max(d.longestHide, Stat(me, PlayerKeys.StatLongestHide));
        d.roundsSurvived += Stat(me, PlayerKeys.StatRoundsSurvived);
        d.steals         += Stat(me, PlayerKeys.StatSteals);
        d.catches        += Stat(me, PlayerKeys.StatCatches);
        d.plants         += Stat(me, PlayerKeys.StatPlants);
        d.defuses        += Stat(me, PlayerKeys.StatDefuses);
        d.donuts         += Stat(me, PlayerKeys.StatDonuts);
        d.abilitiesUsed  += Stat(me, PlayerKeys.StatAbilitiesUsed);

        // The mode
        var record = d.modes.Find(m => m.key == modeKey);
        if (record == null) d.modes.Add(record = new ModeRecord { key = modeKey });
        record.games++;
        if (won) record.wins++;
        record.xp  += summary.xp;
        record.best = Mathf.Max(record.best, NetProps.Of(me, PlayerKeys.Score, 0f));

        // The abilities brought (one match each)
        var loadout = AbilityLoadout.Of(me);
        foreach (AbilitySlot slot in Enum.GetValues(typeof(AbilitySlot)))
        {
            string id = loadout[slot];
            if (string.IsNullOrEmpty(id)) continue;
            var a = d.abilities.Find(x => x.id == id);
            if (a == null) d.abilities.Add(a = new AbilityRecord { id = id, slot = (int)slot });
            a.matches++;
        }

        summary.levelAfter = Level;
        d.last = summary;
        Save(cloud: true);
        Debug.Log($"[Profile] {modeKey}: {(won ? "won" : "played")}, +{summary.xp} XP (level {summary.levelBefore} → {summary.levelAfter}).");
    }

    private static float Stat(PhotonPlayer p, string key) => Mathf.Max(0f, NetProps.Of(p, key, 0f));

    /// <summary>The XP summary of a match recorded in the last few minutes on this PC (the Scoreboard shows it), or null.</summary>
    public static MatchSummary RecentMatch(float withinSeconds = 180f)
    {
        var last = Current.last;
        if (last == null || last.whenTicks == 0) return null;
        return (DateTime.UtcNow - new DateTime(last.whenTicks, DateTimeKind.Utc)).TotalSeconds <= withinSeconds ? last : null;
    }

    // ─── Cloud copy (signed-in accounts) ──────────────────────────────────────

    private static bool SignedIn => !AccountService.IsGuest && PlayFabClientAPI.IsClientLoggedIn();

    private static void PushToCloud(string json)
    {
        if (!SignedIn) return;
        PlayFabClientAPI.UpdateUserData(new UpdateUserDataRequest { Data = new Dictionary<string, string> { [CloudKey] = json } },
            _ => { }, e => Debug.LogWarning($"[Profile] Couldn't save the profile online: {e.ErrorMessage}"));
    }

    /// <summary>
    /// After signing in: the online copy and this PC's are compared, and the one with more games is kept (and the other
    /// side updated). Guests have no online copy.
    /// </summary>
    public static void SyncWithCloud()
    {
        if (!SignedIn) return;
        PlayFabClientAPI.GetUserData(new GetUserDataRequest { Keys = new List<string> { CloudKey } }, result =>
        {
            var cloud = result.Data != null && result.Data.TryGetValue(CloudKey, out var record) ? Parse(record.Value) : null;
            var local = Current;
            if (cloud != null && cloud.games > local.games)
            {
                data = cloud;
                Save(cloud: false);
                Debug.Log($"[Profile] Took the online profile ({cloud.games} games).");
            }
            else if (local.games > 0 && (cloud == null || local.games > cloud.games)) PushToCloud(JsonUtility.ToJson(local));
        }, e => Debug.LogWarning($"[Profile] Couldn't read the online profile: {e.ErrorMessage}"));
    }
}
