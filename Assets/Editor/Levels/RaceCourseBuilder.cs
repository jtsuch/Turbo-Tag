using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using static GreyboxKit;

/// <summary>
/// Builds the sample Race course, Sky Run (Assets/Scenes/Levels/Race/SkyRun.unity; Justin, Nov 18: "long and narrow
/// (like 15 m), with slopes, obstacles, walls, platforms, large gaps, wall run walls"), and the Race mode's settings
/// asset (Resources/GameModes/Race) if it's missing. Greybox, floating in the sky, ~390 m long along +Z, 15 m wide
/// (x −7.5..7.5). Every gap and height comes from the movement metrics (jump 3.2 m up, sprint jump 9.6 m, ledge grab 6.8 m):
///   start line (12 spawns) → ramp up to 6 → hop platforms (4 m gaps, alternating sides) → a 12 m gap crossed by
///   wall-running along a wall on the left → hurdles, a duck-under bar and weaving blocks → a long downhill into a
///   7 m sprint jump → a 6 m climb wall (or a step block and a ledge grab) → a zigzag of rising pillars (+2 m each)
///   → a wall-run corridor over two 9 m pits → the final descent to the finish.
/// Eight numbered gates (<see cref="RaceCheckpoint"/>), the last one the finish; a fall sends you back to the last
/// gate passed. Menu: Turbo Tag/Levels/Race/Rebuild Sky Run (editor job: BuildRaceCourse).
/// </summary>
public static class RaceCourseBuilder
{
    private const string Folder     = "Assets/Scenes/Levels/Race";
    private const string ScenePath  = Folder + "/SkyRun.unity";
    private const string RootName   = "SkyRun";
    private const string ReportPath = "Library/TurboTag/scratch/skyrun_report.txt";
    private const string ModeAsset  = "Assets/Resources/GameModes/Race.asset";

    private const float W = 7.5f;   // Half the course's width
    private static Transform course;
    private static int gate;

    [MenuItem("Turbo Tag/Levels/Race/Rebuild Sky Run")]
    public static void Build()
    {
        EnsureModeAsset();
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Scenes/Levels", "Race");
        // A level scene to start from (the template folder has no scene): the Shooting Range, its geometry cleared
        if (!System.IO.File.Exists(ScenePath)) AssetDatabase.CopyAsset("Assets/Scenes/Levels/ShootingRange.unity", ScenePath);
        var scene = Begin(ScenePath, RootName);
        course = root;
        var geometry = root.parent;
        for (int i = geometry.childCount - 1; i >= 0; i--)
            if (geometry.GetChild(i) != root) Object.DestroyImmediate(geometry.GetChild(i).gameObject);
        gate = 0;

        // 1. Start (0): the grid, low kerbs either side, a start arch
        Floor(0f, 30f, 0f);
        Kerbs(0f, 30f, 0f);
        Arch("Start Arch", 18f, 0f);

        // 2. Ramp up to 6
        Ramp(course, "Ramp Up", -W, W, 30f, 54f, 0f, 6f);

        // 3. Hop platforms at 6, 4 m gaps, alternating sides
        Floor(54f, 62f, 6f);
        Gate(58f, 6f);
        Box(course, "Hop Left",  -W, -0.5f, 5f, 6f, 66f, 72f, Kind.Ledge);
        Box(course, "Hop Right", 0.5f, W,   5f, 6f, 76f, 82f, Kind.Ledge);
        Box(course, "Hop Middle", -3f, 3f,  5f, 6f, 86f, 90f, Kind.Ledge);
        Floor(94f, 110f, 6f);
        Gate(104f, 6f);

        // 4. A 12 m gap at 6, crossed by wall-running along a tall wall on the left
        Box(course, "Wall Run Wall", -W - 1f, -W, 2f, 14f, 104f, 130f, Kind.Wall);
        Floor(122f, 170f, 6f);

        // 5. Obstacles at 6: hurdles, a bar to duck under (crouch: 2.3 m clear), weaving blocks
        Gate(128f, 6f);
        Box(course, "Hurdle 1", -W, W, 6f, 7f, 134f, 135f, Kind.Wall);
        Box(course, "Hurdle 2", -W, W, 6f, 7f, 140f, 141f, Kind.Wall);
        Box(course, "Duck Bar", -W, W, 8.3f, 9.3f, 146f, 148f, Kind.Wall);
        Box(course, "Duck Post L", -W - 0.8f, -W, 6f, 9.3f, 146f, 148f, Kind.Dark);
        Box(course, "Duck Post R", W, W + 0.8f, 6f, 9.3f, 146f, 148f, Kind.Dark);
        Box(course, "Block 1", -W, -1f, 6f, 9f, 154f, 157f, Kind.Wall);
        Box(course, "Block 2", 1f, W, 6f, 9f, 160f, 163f, Kind.Wall);
        Box(course, "Block 3", -W, -1f, 6f, 9f, 166f, 169f, Kind.Wall);

        // 6. Downhill into a 7 m sprint jump, landing at 0
        Ramp(course, "Ramp Down", -W, W, 170f, 190f, 6f, 0f);
        Floor(197f, 215f, 0f);
        Gate(201f, 0f);

        // 7. A 6 m climb wall up to the next floor, with a 3 m step block on the left (a jump, then a ledge grab)
        Box(course, "Climb Wall", -W, W, 0f, 6f, 215f, 216f, Kind.Wall);
        Box(course, "Step Block", -W, -3.5f, 0f, 3f, 211f, 215f, Kind.Ledge);
        Floor(215f, 235f, 6f);
        Gate(224f, 6f);

        // 8. A zigzag of rising pillars (+2 m each), then a floor at 12
        Pillar("Pillar 1", -3.5f, 241f, 8f);
        Pillar("Pillar 2",  3.5f, 248f, 10f);
        Pillar("Pillar 3", -3.5f, 255f, 12f);
        Pillar("Pillar 4",  3.5f, 262f, 12f);
        Floor(267f, 291f, 12f);
        Gate(273f, 12f);

        // 9. A wall-run corridor over two 9 m pits (both sides are wall-run walls)
        Box(course, "Corridor Wall L", -W - 1f, -W, 10f, 21f, 285f, 322f, Kind.Wall);
        Box(course, "Corridor Wall R", W, W + 1f, 10f, 21f, 285f, 322f, Kind.Wall);
        Floor(300f, 306f, 12f);
        Floor(315f, 334f, 12f);
        Gate(322f, 12f);

        // 10. The final descent and the finish
        Ramp(course, "Ramp Finish", -W, W, 334f, 372f, 12f, 0f);
        Floor(372f, 395f, 0f);
        Kerbs(372f, 395f, 0f);
        Gate(384f, 0f, finish: true);

        // Spawns: three rows of four on the start line, facing the course
        var spots = new List<(string, float, float, float)>();
        int n = 0;
        foreach (float z in new[] { 12f, 8f, 4f })
            foreach (float x in new[] { -5.25f, -1.75f, 1.75f, 5.25f })
                spots.Add(($"Grid {++n}", x, z, 0f));
        Spawns(spots.ToArray(), new Vector3(0f, 0f, 400f));

        Bounds(new Vector3(0f, 0f, 200f), 440f, -30f, 70f);
        PlaceSpawner(new Vector3(0f, 0.1f, 8f), -40f, 0f);
        Atmosphere.SunnyDay(course, new Vector3(1f, 0f, 0.2f), new Bounds(new Vector3(0f, 10f, 200f), new Vector3(40f, 60f, 420f)), windOn: false);

        report.Add($"gates: {gate} (the last is the finish)");
        Finish(scene, ReportPath, "Sky Run", null);
    }

    private static void Floor(float z0, float z1, float top) => Box(course, $"Floor {z0:0}-{z1:0}", -W, W, top - 1f, top, z0, z1, Kind.Floor);

    private static void Kerbs(float z0, float z1, float top)
    {
        Box(course, $"Kerb L {z0:0}", -W - 0.6f, -W, top, top + 1f, z0, z1, Kind.Ledge);
        Box(course, $"Kerb R {z0:0}", W, W + 0.6f, top, top + 1f, z0, z1, Kind.Ledge);
    }

    private static void Pillar(string name, float x, float z, float top) =>
        Box(course, name, x - 2f, x + 2f, top - 4f, top, z - 2f, z + 2f, Kind.Ledge);

    // An arch over the course (posts outside its width), visual only
    private static void Arch(string name, float z, float floor)
    {
        Box(course, name + " Post L", -W - 1.2f, -W - 0.4f, floor, floor + 7.8f, z - 0.4f, z + 0.4f, Kind.Dark);
        Box(course, name + " Post R", W + 0.4f, W + 1.2f, floor, floor + 7.8f, z - 0.4f, z + 0.4f, Kind.Dark);
        Box(course, name + " Bar", -W - 1.2f, W + 1.2f, floor + 7f, floor + 7.8f, z - 0.4f, z + 0.4f, Kind.Ledge);
    }

    // A numbered gate: its arch, a trigger across the course and the respawn point just past it
    private static void Gate(float z, float floor, bool finish = false)
    {
        gate++;
        Arch(finish ? "Finish" : $"Gate {gate}", z, floor);
        var go = new GameObject(finish ? "Finish Gate" : $"Gate {gate} Trigger");
        go.transform.SetParent(course, false);
        go.transform.position = new Vector3(0f, floor + 4f, z);
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(W * 2f + 2f, 8f, 2f);
        var cp = go.AddComponent<RaceCheckpoint>();
        cp.index = gate;
        var respawn = new GameObject("Respawn").transform;
        respawn.SetParent(go.transform, false);
        respawn.position = new Vector3(0f, floor + 0.1f, z + 3f);
        cp.respawn = respawn;
    }

    // ─── The mode's settings asset (made once; edit it in the Inspector after) ───

    private static void EnsureModeAsset()
    {
        if (AssetDatabase.LoadAssetAtPath<GameModeDefinition>(ModeAsset) != null) return;
        var def = ScriptableObject.CreateInstance<GameModeDefinition>();
        def.gameModeName = "Race";
        def.gameModeKey = "Race";
        def.description = "Race to the finish through every gate. Fall and you're back at the last one. The first one home closes the race for everyone else.";
        def.mapCategory = "Race";
        def.settings = new[]
        {
            new RuleSetting { displayName = "Time Limit", roomPropertyKey = RuleKeys.MatchTime, fieldType = RuleSetting.FieldType.Time,
                              category = "Match", minValue = 60f, maxValue = 900f, defaultValue = 300f, wholeNumbers = true },
            new RuleSetting { displayName = "Start Countdown", roomPropertyKey = RuleKeys.CountdownTime, fieldType = RuleSetting.FieldType.Number,
                              category = "Match", minValue = 3f, maxValue = 10f, defaultValue = 5f, wholeNumbers = true, unit = "s" },
            new RuleSetting { displayName = "Collisions", roomPropertyKey = RuleKeys.RaceCollisions, fieldType = RuleSetting.FieldType.Toggle,
                              category = "Race", minValue = 0f, maxValue = 1f, defaultValue = 1f, wholeNumbers = true },
            new RuleSetting { displayName = "Ability Vote", roomPropertyKey = RuleKeys.AbilityVote, fieldType = RuleSetting.FieldType.Toggle,
                              category = "Race", minValue = 0f, maxValue = 1f, defaultValue = 1f, wholeNumbers = true },
        };
        AssetDatabase.CreateAsset(def, ModeAsset);
        AssetDatabase.SaveAssets();
    }
}
