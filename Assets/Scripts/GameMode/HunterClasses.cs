using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Hunter classes (Tag, Protect the President: <see cref="GameMode.HasHunterClasses"/>): a hunter picks one class
/// (a passive) and one of its left-click tools while the hiders hide (<see cref="HunterClassPicker"/>). Speed: faster;
/// Intel: finds hiders; Trapper: lays traps. The tools themselves run in <see cref="HunterKit"/>. The pick is
/// remembered in PlayerPrefs and synced as the player's own properties (so others can see it).
/// Add a tool: an entry here and a case in HunterKit.Use.
/// </summary>
public static class HunterClasses
{
    public sealed class Tool
    {
        public string Id, Name, Description;
        public float Cooldown;
        public int MaxOut; // Placed tools: how many can be out at once (0 = not placed)
    }

    public sealed class Class
    {
        public string Id, Name, Passive, Blurb;
        public Color Color;
        public Tool[] Tools;
    }

    public static readonly Class[] All =
    {
        new()
        {
            Id = "Speed", Name = "Speed", Color = new Color(1f, 0.6f, 0.2f),
            Blurb = "Run them down.",
            Passive = "+15% movement and climbing speed",
            Tools = new[]
            {
                new Tool { Id = "Lunge",       Name = "Lunge",        Cooldown = 5f,  Description = "A short burst forward, carrying your momentum." },
                new Tool { Id = "SprintBurst", Name = "Sprint Burst", Cooldown = 12f, Description = "40% faster for 3 seconds." },
                new Tool { Id = "Pounce",      Name = "Pounce",       Cooldown = 16f, Description = "Leap to the spot you're aiming at, up to 25 m away." },
            },
        },
        new()
        {
            Id = "Intel", Name = "Intel", Color = new Color(0.3f, 0.65f, 1f),
            Blurb = "Know where they are.",
            Passive = "See hiders' footprints from the last 3 seconds",
            Tools = new[]
            {
                new Tool { Id = "XRayScan", Name = "X-Ray Scan",  Cooldown = 18f, Description = "For 3 s, see hiders through walls in a cone ahead of you (30 m). Scanned walls glow blue." },
                new Tool { Id = "Beacon",   Name = "Beacon",      Cooldown = 20f, Description = "Throw a device that sticks to walls. Hiders it can see light up for 2 s." },
                new Tool { Id = "Sonar",    Name = "Sonar Pulse", Cooldown = 25f, Description = "Every hider within 15 m lights up for 2.5 s. They hear it too." },
            },
        },
        new()
        {
            Id = "Trapper", Name = "Trapper", Color = new Color(0.45f, 0.9f, 0.4f),
            Blurb = "Make them come to you.",
            Passive = "Hiders' ability objects glow red for you",
            Tools = new[]
            {
                new Tool { Id = "Alarm",   Name = "Alarm Trap",   Cooldown = 12f, MaxOut = 3, Description = "A hidden floor trap. A hider within 10 m sets it off and shows for 6 s. Faintly visible within 15 m." },
                new Tool { Id = "Snare",   Name = "Snare",        Cooldown = 15f, MaxOut = 2, Description = "A small floor snare: a hider who steps on it is stuck for 1.5 s. Faintly visible within 8 m." },
                new Tool { Id = "Tracker", Name = "Tracker Dart", Cooldown = 20f, Description = "Shoot a dart (45 m). A hider it hits shows for 8 s." },
            },
        },
    };

    public static Class Find(string id)
    {
        foreach (var c in All) if (c.Id == id) return c;
        return null;
    }

    /// <summary>A tool by id in any class (and its class).</summary>
    public static (Class cls, Tool tool) FindAnywhere(string id)
    {
        foreach (var c in All) foreach (var t in c.Tools) if (t.Id == id) return (c, t);
        return (null, null);
    }

    public static Tool FindTool(Class c, string id)
    {
        if (c == null) return null;
        foreach (var t in c.Tools) if (t.Id == id) return t;
        return null;
    }

    // ─── The local pick ───────────────────────────────────────────────────────

    private const string ClassPrefs = "HunterClass", ToolPrefs = "HunterTool";

    /// <summary>The local hunter's class (remembered; Speed the first time).</summary>
    public static Class Chosen => Find(PlayerPrefs.GetString(ClassPrefs, "Speed")) ?? All[0];

    /// <summary>The local hunter's tool (one of <see cref="Chosen"/>'s; its first the first time).</summary>
    public static Tool ChosenTool => FindTool(Chosen, PlayerPrefs.GetString(ToolPrefs, "")) ?? Chosen.Tools[0];

    /// <summary>Local: picks a class and tool (remembered, and sent to the room).</summary>
    public static void Choose(Class c, Tool tool)
    {
        PlayerPrefs.SetString(ClassPrefs, c.Id);
        PlayerPrefs.SetString(ToolPrefs, tool.Id);
        if (PhotonNetwork.InRoom)
            NetProps.SetLocal(new ExitGames.Client.Photon.Hashtable { [PlayerKeys.HunterClass] = c.Id, [PlayerKeys.HunterTool] = tool.Id });
        Changed?.Invoke();
    }

    public static event System.Action Changed;
}

/// <summary>
/// Balancing data: every catch in a mode with hunter classes (master) appends a row to
/// {persistentDataPath}/HunterStats.csv (when, map, mode, the hunter's class and tool, seconds into the hunt) and logs it.
/// </summary>
public static class HunterStats
{
    public static void RecordCatch(Photon.Realtime.Player tagger, float secondsIntoHunt)
    {
        if (tagger == null) return;
        string cls = NetProps.Of(tagger, PlayerKeys.HunterClass, "?"), tool = NetProps.Of(tagger, PlayerKeys.HunterTool, "?");
        string row = $"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss},{MatchRules.Map},{MatchRules.GameModeKey},{cls},{tool},{secondsIntoHunt:0.0}";
        Debug.Log($"[HunterStats] catch by {cls}/{tool} at {secondsIntoHunt:0.0}s");
        try
        {
            string path = System.IO.Path.Combine(Application.persistentDataPath, "HunterStats.csv");
            if (!System.IO.File.Exists(path)) System.IO.File.WriteAllText(path, "time,map,mode,class,tool,seconds_into_hunt\n");
            System.IO.File.AppendAllText(path, row + "\n");
        }
        catch (System.Exception e) { Debug.LogWarning("[HunterStats] " + e.Message); }
    }
}

/// <summary>
/// Hiders lit up for this screen's hunters (Intel and Trapper tools): drawn through walls in red by
/// PlayerAppearance (even cloaked) until the time runs out. Local to each screen.
/// </summary>
public static class HunterReveal
{
    private static readonly Dictionary<int, float> until = new();

    public static void Reveal(int actor, float seconds)
    {
        var target = Player.Find(actor);
        if (target != null && target.Has(PlayerFlag.Jammed)) return; // The Jammer donut
        until.TryGetValue(actor, out float current);
        until[actor] = Mathf.Max(current, Time.time + seconds);
    }

    public static bool IsRevealed(int actor) => until.TryGetValue(actor, out float t) && Time.time < t;

    public static void Clear() => until.Clear();
}
