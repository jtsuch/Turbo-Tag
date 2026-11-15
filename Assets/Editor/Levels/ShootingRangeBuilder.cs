using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using static GreyboxKit;
using static KitLevel;

/// <summary>
/// Builds the Shooting Range (Assets/Scenes/Levels/ShootingRange.unity; Justin, Nov 14: "a pretty simple location to
/// test guns, climbing and ledge grabbing"), the default map (<see cref="SceneNames.DefaultMap"/>):
///   - a floating 64 × 64 m floor (4 × 4 platforms' worth, plain 32 m slabs) with its skirt, dirt and propellers;
///   - a small tower (16 × 16, roof at 10) with climb faces on every side and two doors, and a 5 m block beside it for
///     ledge grabs (its top is within reach of a ledge grab from the floor);
///   - the range: a firing line at z −4 and a <see cref="TrainingDummy"/> 18 m away (guns have no damage falloff, only a
///     maximum range: 45 m for the shotgun, 120+ for the rest), in front of a tall backstop wall.
/// For guns, play it in Freeplay with Match Rules → Combat → Guns on (B opens the shop).
/// Menu: Turbo Tag/Levels/Rebuild Shooting Range (editor job: BuildShootingRange).
/// </summary>
public static class ShootingRangeBuilder
{
    private const string ScenePath  = "Assets/Scenes/Levels/ShootingRange.unity";
    private const string RootName   = "ShootingRange";
    private const string ReportPath = "Library/TurboTag/scratch/shootingrange_report.txt";
    private const float G = 0f;

    [MenuItem("Turbo Tag/Levels/Rebuild Shooting Range")]
    public static void Build()
    {
        Reset(20261114, "Assets/Environment/Generated/ShootingRange_Island.asset", "Shooting Range Island", G);
        if (!System.IO.File.Exists(ScenePath)) AssetDatabase.CopyAsset("Assets/Scenes/Levels/Stairfall.unity", ScenePath);
        var scene = Begin(ScenePath, RootName);
        var geometry = root.parent;
        for (int i = geometry.childCount - 1; i >= 0; i--)
            if (geometry.GetChild(i) != root) Object.DestroyImmediate(geometry.GetChild(i).gameObject);

        // Ground: 64 × 64 (8 × 8 cells of 8 m)
        var ground = Group("Ground");
        var cells = new HashSet<Vector2>();
        for (float x = -28; x < 32; x += 8)
            for (float z = -28; z < 32; z += 8)
            {
                RecordGround(CellOf(x, z), G);
                cells.Add(new Vector2(x, z));
            }
        Fill(ground, cells, null, G, false, -32f, -32f);

        // The tower (roof 10: a climb from a standing jump reaches it) and the ledge block (top 5: a ledge grab)
        var climb = Group("Climbing");
        Building(climb, "Tower", 8, 24, 8, 24, G, 2, south: "CD", north: "CC", west: "DC", east: "CC");
        Building(climb, "Ledge Block", -24, -8, 16, 24, G, 1, south: "CC", north: "CC", west: "C", east: "C");

        // The range: a backstop behind the dummy, the dummy 18 m from the firing line, facing it
        var range = Group("Range");
        foreach (float x in new[] { -8f, 0f, 8f }) Wall(range, "Wall-Tall-8m", x, G, -28f, S);
        var dummy = new GameObject("Training Dummy");
        dummy.transform.SetParent(range, false);
        dummy.transform.SetPositionAndRotation(new Vector3(0f, G, -22f), Quaternion.identity);   // Faces +z: the firing line
        dummy.AddComponent<TrainingDummy>();

        BuildIsland();
        CheckOverlaps();
        Bounds(Vector3.zero, 260f, -120f, 60f);
        Spawns(new (string, float, float, float)[]
        {
            ("Firing Line", 0f, -4f, G), ("West", -24f, 0f, G), ("East", 24f, -12f, G), ("North", 0f, 28f, G),
        }, new Vector3(0f, G, -22f));
        PlaceSpawner(new Vector3(0f, 0.1f, -4f), -100f);
        Atmosphere.SunnyDay(root, new Vector3(1f, 0f, 0.35f), new Bounds(new Vector3(0, 10, 0), new Vector3(90, 40, 90)), windOn: false);

        Finish(scene, ReportPath, "Shooting Range", null);
    }
}
