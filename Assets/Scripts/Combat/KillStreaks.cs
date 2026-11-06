using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Every client's tally of kill streaks and multi-kills, from the announced kills (CombatManager feeds it before
/// raising Killed, so the HUD can read it in the same event). A death (shot or fall) ends that player's streak.
/// Multi-kills: kills less than <see cref="MultiWindow"/> apart.
/// </summary>
public static class KillStreaks
{
    public const float MultiWindow = 4f;

    private static readonly Dictionary<int, int>   streak = new();
    private static readonly Dictionary<int, int>   multi  = new();
    private static readonly Dictionary<int, float> lastKill = new();

    /// <summary>Kills in a row without dying.</summary>
    public static int Streak(int actor) => streak.TryGetValue(actor, out int n) ? n : 0;

    /// <summary>Kills in the current quick burst (1 = a single kill).</summary>
    public static int Multi(int actor) => multi.TryGetValue(actor, out int n) ? n : 0;

    public static void Record(KillInfo kill)
    {
        streak[kill.Victim] = 0;
        multi[kill.Victim]  = 0;
        if (kill.SelfDestruct) return;

        int killer = kill.Killer;
        streak[killer] = Streak(killer) + 1;
        bool quick = lastKill.TryGetValue(killer, out float at) && Time.time - at <= MultiWindow;
        multi[killer] = quick ? Multi(killer) + 1 : 1;
        lastKill[killer] = Time.time;
    }

    public static void Clear()
    {
        streak.Clear();
        multi.Clear();
        lastKill.Clear();
    }

    /// <summary>The multi-kill call-out for a burst of <paramref name="count"/>, or null.</summary>
    public static string MultiName(int count) => count switch
    {
        2 => "DOUBLE KILL",
        3 => "TRIPLE KILL",
        4 => "QUAD KILL",
        >= 5 => "PENGUIN PANIC",
        _ => null,
    };

    /// <summary>The streak call-out when a streak reaches <paramref name="count"/>, or null.</summary>
    public static string StreakName(int count) => count switch
    {
        3  => "KILLING SPREE",
        5  => "RAMPAGE",
        7  => "UNSTOPPABLE",
        10 => "GODLIKE",
        15 => "LEGENDARY",
        _  => null,
    };
}
