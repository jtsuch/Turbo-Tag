using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ProBuilder;

/// <summary>
/// Builds the Pinnacle level (Assets/Scenes/Levels/Pinnacle.unity) from code, as greybox: plain blocks in three
/// tones (light walkable tops, mid walls, dark supports), orange tops on ledges meant for climbing, Justin's kit
/// props for life. Re-running rebuilds everything under Geometry/Pinnacle. Plan: Assets/Environment/LevelSpecs/Pinnacle.md.
///
/// Height tiers (from the movement numbers): low 2.5 m (a jump), medium 6 m (a crouch jump or a ledge grab),
/// high 10–14 m (wall-run lanes, ledge chains, lifts). Clearances come from the player's capsule: running
/// tunnels and doors ≥ 3.5 m (4 m here), crouch tunnels 2.4 m, crawl tunnels 1.7 m, all ≥ 2.5 m wide.
///
/// After building it checks itself (clearances in every tunnel, spawn points free, gap lengths) and writes
/// Library/TurboTag/scratch/pinnacle_report.txt plus overview pictures (pinnacle_top.png, pinnacle_*.png).
/// Menu: Turbo Tag/Levels/Rebuild Pinnacle (editor job: BuildPinnacle).
/// </summary>
public static class PinnacleBuilder
{
    private const string ScenePath    = "Assets/Scenes/Levels/Pinnacle.unity";
    private const string TemplatePath = "Assets/Scenes/Levels/_LevelTemplate.unity";
    private const string RootName     = "Pinnacle";
    private const string ReportPath   = "Library/TurboTag/scratch/pinnacle_report.txt";
    private const int    Structure    = 6;

    // Tiers (top heights above the main floor)
    private const float Low = 2.5f, Mid = 6f;

    // Player capsule (standing / crouched / prone) for the clearance checks
    private const float Radius = 0.7f, Stand = 2.9f, Crouch = 2.03f;

    private static Material light, mid, dark, ledge;
    private static Transform root;
    private static readonly List<string> report = new();
    private static readonly List<(string name, Vector3 from, Vector3 to, string kind)> tunnels = new();
    private static readonly List<(string name, Vector3 a, Vector3 b)> gaps = new();

    [MenuItem("Turbo Tag/Levels/Rebuild Pinnacle")]
    public static void Build()
    {
        report.Clear();
        tunnels.Clear();
        gaps.Clear();

        if (!File.Exists(ScenePath)) AssetDatabase.CopyAsset(TemplatePath, ScenePath);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var geometry = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Geometry");
        if (geometry == null) geometry = new GameObject("Geometry");
        var old = geometry.transform.Find(RootName);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        LoadMaterials();
        root = new GameObject(RootName).transform;
        root.SetParent(geometry.transform, false);

        BuildMain();
        BuildPlateau();
        BuildTunnelBlock();
        BuildPlaza();
        BuildNorthWall();
        BuildSouthWest();
        BuildLowIsle();
        BuildHighIsle();
        BuildPitMaze();
        BuildSouthWalkway();
        BuildBounds();
        BuildSpawns();

        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.gameObject.layer == 0 && t.GetComponent<Light>() == null && !t.name.StartsWith("Invisible") && !t.name.StartsWith("Kill"))
                t.gameObject.layer = Structure;

        PlaceSpawner();
        Physics.SyncTransforms();
        Review();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        LevelTools_Sync();
        RenderOverviews();

        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllLines(ReportPath, report);
        Debug.Log($"[Pinnacle] Built. {report.Count(r => r.StartsWith("!"))} problem(s). Report: {ReportPath}");
    }

    // ─── Main platform ────────────────────────────────────────────────────────

    // 110 × 110 m slab, floor at 0, 4 m thick, hanging over the pit
    private static void BuildMain()
    {
        var g = Group("Main Platform");
        Box(g, "Main Slab", -55, 55, -4, 0, -55, 55, Kind.Floor);
        // A skirt under the edges so the slab reads as solid ground from below
        Box(g, "Main Underside", -50, 50, -10, -4, -50, 50, Kind.Dark);
    }

    // East third raised to the medium tier (6 m), with tunnels through its base
    private static void BuildPlateau()
    {
        var g = Group("High Plateau");
        // Bands from south to north (x 14..55, top 6), carved by tunnels
        Box(g, "Plateau S", 14, 55, 0, Mid, -55, -24, Kind.Floor);
        // Running tunnel T1 (4.5 m clear, 4 m wide) right through to the east edge, with a skylight shaft
        Box(g, "T1 Roof W", 14, 30, 4.5f, Mid, -24, -20, Kind.Floor);
        Box(g, "T1 Roof E", 34, 55, 4.5f, Mid, -24, -20, Kind.Floor);
        Tunnel("T1 Running Tunnel", new Vector3(14, 0, -22), new Vector3(55, 0, -22), "run");
        Box(g, "Plateau Mid", 14, 55, 0, Mid, -20, 10, Kind.Floor);
        // Crouch tunnel from the plaza into a hidden room, which has a hatch up to the plateau top
        Box(g, "Crouch S", 14, 24, 0, Mid, 10, 15.75f, Kind.Floor);
        Box(g, "Crouch Roof", 14, 24, 2.4f, Mid, 15.75f, 18.25f, Kind.Floor);
        Box(g, "Crouch N", 14, 24, 0, Mid, 18.25f, 26, Kind.Floor);
        Tunnel("Crouch Tunnel (plateau)", new Vector3(14, 0, 17), new Vector3(24, 0, 17), "crouch");
        Box(g, "Room Roof", 24, 36, 4.5f, Mid, 10, 22, Kind.Floor);
        Box(g, "Room Roof N", 24, 31, 4.5f, Mid, 22, 26, Kind.Floor);
        Box(g, "Room Hatch Step", 31, 36, 0, Low, 22, 26, Kind.Ledge); // 2.5 → the hatch at 6
        // The room's second way out: a running tunnel east to a drop-off ledge over the pit
        Box(g, "Plateau Room E S", 36, 55, 0, Mid, 10, 12, Kind.Floor);
        Box(g, "Room Exit Roof", 36, 55, 4.5f, Mid, 12, 16, Kind.Floor);
        Tunnel("Room Exit Tunnel", new Vector3(36, 0, 14), new Vector3(55, 0, 14), "run");
        Box(g, "Plateau Room E N", 36, 55, 0, Mid, 16, 26, Kind.Floor);
        Box(g, "Plateau N", 14, 55, 0, Mid, 26, 52, Kind.Floor);

        // ── The dividing wall (x = 14), facing the plaza: ramp, ledge step, lift, tunnel mouths ──
        Ramp(g, "Plateau Ramp", 10, 14, -6, 14, 0, Mid);                 // 6 m over 20 m (~17°)
        Box(g, "Wall Step", 11.5f, 14, 0, Low, -44, -36, Kind.Ledge);     // Low tier, then a grab to 6
        Box(g, "Wall Step 2", 12, 14, 0, Low + 1, 36, 42, Kind.Ledge);
        Mover(g, "Plateau Lift", new Vector3(10, -1, 46), new Vector3(10, Mid - 1, 46), new Vector3(4, 1, 4), 2.5f);

        // ── On top (y = 6) ──
        // Arcade: a roofed walk along the plateau (roof at 11, the high tier's first step)
        var arcade = Group("Arcade", g);
        for (float x = 18; x <= 50; x += 8)
        {
            Box(arcade, "Arcade Pillar", x, x + 1, Mid, 11, -12, -11, Kind.Wall);
            Box(arcade, "Arcade Pillar", x, x + 1, Mid, 11, -3, -2, Kind.Wall);
        }
        Box(arcade, "Arcade Roof", 17, 52, 11, 12, -12.5f, -1.5f, Kind.Floor);
        Box(arcade, "Arcade Step", 52, 55, Mid, Mid + Low, -10, -4, Kind.Ledge); // 8.5 → roof 12

        // Huts with a roof bridge (roofs 11.5, bridge 3 m wide)
        Hut(g, "Hut A", 20, 28, 30, 38, Mid, 5.5f, 'E', 'S');
        Hut(g, "Hut B", 38, 46, 30, 38, Mid, 5.5f, 'W', 'S');
        Box(g, "Roof Bridge", 28, 38, Mid + 5.5f, Mid + 6, 32.5f, 35.5f, Kind.Floor);
        Box(g, "Hut Step", 46, 48, Mid, Mid + Low, 30, 34, Kind.Ledge);

        // Watch tower (top 16, high tier): a wall-run lane along its west side, ledges as the slow way
        Box(g, "Watch Tower", 46, 52, Mid, 16, -50, -44, Kind.Wall);
        Box(g, "Wall-Run Wall", 30, 46, Mid, 14, -51, -50, Kind.Wall);    // Run east along it, jump to the tower
        Box(g, "Tower Ledge 1", 44, 46, Mid, Mid + 3, -44, -41, Kind.Ledge);
        Box(g, "Tower Ledge 2", 46, 49, Mid, Mid + 6.5f, -44, -42, Kind.Ledge);
        Box(g, "Tower Cap Ledge", 52, 54, Mid, 13, -48, -45, Kind.Ledge);

        // Skybridge: arcade roof (12) north to the huts' roof bridge, the plateau's high-tier spine
        Box(g, "Skybridge", 33, 36, Mid + 5.5f, Mid + 6, -1.5f, 32.5f, Kind.Floor);
        Box(g, "Skybridge Pillar", 34, 35, Mid, Mid + 5.5f, 14, 15, Kind.Dark);
        Box(g, "Plateau Cover", 22, 28, Mid, Mid + Low, 2, 6, Kind.Ledge);

        // Cliff path along the plateau's east face (floor level, no rail): joins both tunnel exits to the pit maze
        Box(g, "Cliff Path N", 55, 57.5f, -1, 0, -38, 16, Kind.Floor);
        Box(g, "Cliff Path S", 55, 57.5f, -1, 0, -68, -42, Kind.Floor);
        gaps.Add(("Cliff path hop", new Vector3(56, 0, -38), new Vector3(56, 0, -42)));

        // Railing along the plateau edge above the ramp (a hint, not a cage)
        for (float z = 16; z <= 24; z += 2) Prop(g, "Railing-2m", new Vector3(14.2f, Mid, z), 90f);
        Prop(g, "Crate-1x1m", new Vector3(30, Mid, 0), 0f, 1.5f);
        Prop(g, "Crate-1x1m", new Vector3(31.5f, Mid, 0.5f), 20f, 1.2f);
    }

    // NW ground layer: blocks with running alleys, roofed tunnels, one crawl tunnel and one crouch tunnel;
    // the rooftops (5 m) are a second layer reached by ledges and a fan
    private static void BuildTunnelBlock()
    {
        var g = Group("Tunnel Block");
        const float top = 5f;

        // Row N (z 40..48)
        Box(g, "Block N1", -50, -42, 0, top, 40, 48, Kind.Floor);
        Box(g, "Block N2", -38, -30, 0, top, 40, 48, Kind.Floor);
        Box(g, "Block N3", -26, -18, 0, top, 40, 48, Kind.Floor);
        // Row M (z 28..36), N2's twin cut by a crawl tunnel along x
        Box(g, "Block M1", -50, -42, 0, top, 28, 36, Kind.Floor);
        Box(g, "Block M2 S", -38, -30, 0, top, 28, 30.75f, Kind.Floor);
        Box(g, "Block M2 Roof", -38, -30, 1.7f, top, 30.75f, 33.25f, Kind.Floor);
        Box(g, "Block M2 N", -38, -30, 0, top, 33.25f, 36, Kind.Floor);
        Tunnel("Crawl Tunnel (tunnel block)", new Vector3(-38, 0, 32), new Vector3(-30, 0, 32), "crawl");
        Box(g, "Block M3", -26, -18, 0, top, 28, 36, Kind.Floor);
        // Row S (z 14..24), S2 cut by a crouch tunnel along z
        Box(g, "Block S1", -50, -42, 0, top, 14, 24, Kind.Floor);
        Box(g, "Block S2 W", -38, -35.25f, 0, top, 14, 24, Kind.Floor);
        Box(g, "Block S2 Roof", -35.25f, -32.75f, 2.4f, top, 14, 24, Kind.Floor);
        Box(g, "Block S2 E", -32.75f, -30, 0, top, 14, 24, Kind.Floor);
        Tunnel("Crouch Tunnel (tunnel block)", new Vector3(-34, 0, 14), new Vector3(-34, 0, 24), "crouch");
        Box(g, "Block S3", -26, -18, 0, top, 14, 24, Kind.Floor);

        // Roofed alleys make running tunnels (4 m clear); open alleys between stay open to the sky
        Box(g, "Tunnel Roof E-W", -50, -18, 4, top, 36, 40, Kind.Floor);
        Tunnel("Running Tunnel (E-W)", new Vector3(-50, 0, 38), new Vector3(-18, 0, 38), "run");
        Box(g, "Tunnel Roof N-S", -42, -38, 4, top, 14, 28, Kind.Floor);
        Tunnel("Running Tunnel (N-S)", new Vector3(-40, 0, 14), new Vector3(-40, 0, 28), "run");

        // Ways up to the rooftops: ledge steps (low tier) and a fan in the open crossing
        Box(g, "Roof Step E", -18, -16, 0, Low, 18, 22, Kind.Ledge);
        Box(g, "Roof Step S", -48, -44, 0, Low, 12, 14, Kind.Ledge);
        Box(g, "Roof Step N", -30, -26, 0, Low, 48, 50, Kind.Ledge);
        Prop(g, "Fan", new Vector3(-28, 0, 26), 0f);
        Prop(g, "Vent", new Vector3(-22, 1.2f, 13.6f), 180f);
        Prop(g, "Crate-1x1m", new Vector3(-44, 0, 26), 15f, 1.2f);
    }

    // Open centre with light cover and a climbable tower (ledges spiral up to 10)
    private static void BuildPlaza()
    {
        var g = Group("Central Plaza");
        Box(g, "Centre Tower", -3, 3, 0, 10, -3, 3, Kind.Wall);
        Box(g, "Tower Ledge 1", 3, 5, 0, Low, -3, 1, Kind.Ledge);
        Box(g, "Tower Ledge 2", -1, 3, 0, 5, 3, 5, Kind.Ledge);
        Box(g, "Tower Ledge 3", -5, -3, 0, 7.5f, -1, 3, Kind.Ledge);
        Bounce(g, new Vector3(0, 0, -7), 32f); // Up to the tower top
        Prop(g, "HalfWall-2m", new Vector3(-8, 0, 8), 0f);
        Prop(g, "HalfWall-2m", new Vector3(8, 0, -9), 90f);
        Prop(g, "HalfWall-2m", new Vector3(-9, 0, -8), 45f);
        Prop(g, "Crate-1x1m", new Vector3(6, 0, 7), 10f, 1.5f);
        Prop(g, "Crate-1x1m", new Vector3(7.2f, 0, 7.4f), -15f, 1f);
        Lamp(g, new Vector3(0, 12, 0), 25f);

        // South bunker (medium, top 5): a running tunnel east-west, a crouch tunnel north-south, a low step up
        var b = Group("South Bunker", g);
        Box(b, "Bunker S W", -4, 2.75f, 0, 5, -40, -37, Kind.Floor);
        Box(b, "Bunker S Crouch Roof", 2.75f, 5.25f, 2.4f, 5, -40, -37, Kind.Floor);
        Box(b, "Bunker S E", 5.25f, 10, 0, 5, -40, -37, Kind.Floor);
        Box(b, "Bunker Tunnel Roof", -4, 10, 4, 5, -37, -33, Kind.Floor);
        Tunnel("Bunker Running Tunnel", new Vector3(-4, 0, -35), new Vector3(10, 0, -35), "run");
        Box(b, "Bunker N W", -4, 2.75f, 0, 5, -33, -30, Kind.Floor);
        Box(b, "Bunker N Crouch Roof", 2.75f, 5.25f, 2.4f, 5, -33, -30, Kind.Floor);
        Box(b, "Bunker N E", 5.25f, 10, 0, 5, -33, -30, Kind.Floor);
        Tunnel("Bunker Crouch S", new Vector3(4, 0, -41), new Vector3(4, 0, -37.2f), "crouch");
        Tunnel("Bunker Crouch N", new Vector3(4, 0, -32.8f), new Vector3(4, 0, -29), "crouch");
        Box(b, "Bunker Step", -2, 2, 0, Low, -30, -28, Kind.Ledge);

        // West catwalk (medium, 5): from the tunnel block's roofs south over the plaza to a landing block
        var c = Group("West Catwalk", g);
        Box(c, "Catwalk", -24, -21, 4.5f, 5, -8, 14, Kind.Floor);
        Box(c, "Catwalk Pillar", -23, -22, 0, 4.5f, 5, 6, Kind.Dark);
        Box(c, "Catwalk Pillar", -23, -22, 0, 4.5f, -3, -2, Kind.Dark);
        Box(c, "Landing W", -26, -19, 0, 5, -14, -12.5f, Kind.Floor);
        Box(c, "Landing Roof", -26, -19, 4, 5, -12.5f, -8.5f, Kind.Floor);
        Box(c, "Landing E", -26, -19, 0, 5, -8.5f, -8, Kind.Floor);
        Tunnel("Landing Tunnel", new Vector3(-26, 0, -10.5f), new Vector3(-19, 0, -10.5f), "run");
        Box(c, "Landing Step", -26, -23, 0, Low, -16, -14, Kind.Ledge);

        // Perch (high, 10): only by wall-running the wall south of the landing and jumping off it
        Box(c, "Wall-Run Wall W", -19, -18, 0, 12, -30, -14, Kind.Wall);
        Box(c, "Perch", -18, -13, 9, 10, -32, -26, Kind.Floor);
        Box(c, "Perch Strut", -16, -15, 0, 9, -31, -30, Kind.Dark);
    }

    // The north edge: a 14 m wall that's hard to leave, but covered in ledges, a window balcony over the pit
    // and a walkway along its top
    private static void BuildNorthWall()
    {
        var g = Group("North Wall");
        // Wall with a window (x -10..-4, y 3..7) out to a balcony over the pit
        Box(g, "North Wall W", -55, -10, 0, 14, 52, 55, Kind.Wall);
        Box(g, "North Wall Window Sill", -10, -4, 0, 3, 52, 55, Kind.Wall);
        Box(g, "North Wall Window Head", -10, -4, 7, 14, 52, 55, Kind.Wall);
        Box(g, "North Wall Mid", -4, 24, 0, 14, 52, 55, Kind.Wall);
        Box(g, "North Wall Window 2 Sill", 24, 30, 0, 9, 52, 55, Kind.Wall);
        Box(g, "North Wall Window 2 Head", 24, 30, 13, 14, 52, 55, Kind.Wall);
        Box(g, "North Wall E", 30, 55, 0, 14, 52, 55, Kind.Wall);
        Box(g, "Window 2 Balcony", 23, 31, 8, 9, 55, 58, Kind.Floor);
        Box(g, "Window 2 Step", 25, 29, Mid, 8, 50, 52, Kind.Ledge); // Plateau 6 → 8 → sill 9
        Box(g, "Window Balcony", -11, -3, 0, 3, 55, 58, Kind.Floor);
        // Ledge stairs up the face (each step a jump or grab apart)
        Box(g, "Wall Ledge 1", -46, -40, 0, Low, 50, 52, Kind.Ledge);
        Box(g, "Wall Ledge 2", -40, -34, 0, 5.5f, 50, 52, Kind.Ledge);
        Box(g, "Wall Ledge 3", -34, -28, 8.5f, 9.5f, 50, 52, Kind.Ledge);
        Box(g, "Wall Ledge 4", -28, -22, 11.5f, 12.5f, 50, 52, Kind.Ledge); // Then a 1.5 m hop to the top
        Box(g, "Wall Ledge Plateau", 40, 46, Mid, 10, 50, 52, Kind.Ledge);   // From the plateau
        Lamp(g, new Vector3(-7, 9, 50), 16f);
    }

    // South-west: open ground, a shed (hiding spot with two doors), a raised deck with a wall on its back
    private static void BuildSouthWest()
    {
        var g = Group("South West");
        Hut(g, "Shed", -46, -36, -46, -38, 0, 4.5f, 'N', 'E');
        Box(g, "Deck", -30, -18, 0, Low, -40, -30, Kind.Floor);
        Box(g, "Deck Back Wall", -31, -30, 0, 9, -42, -28, Kind.Wall);
        Box(g, "Deck Back Ledge", -32.5f, -31, 0, 5.5f, -36, -33, Kind.Ledge);
        Box(g, "South Parapet W", -55, -33, 0, 1.2f, -55, -54, Kind.Wall);
        Box(g, "South Parapet E", -28, -20, 0, 1.2f, -55, -54, Kind.Wall);
        Prop(g, "Crate-1x1m", new Vector3(-22, Low, -34), 0f, 1.4f);
        Prop(g, "HalfWall-2m", new Vector3(-14, 0, -24), 30f);
        Lamp(g, new Vector3(-41, 6, -42), 14f);
    }

    // ─── Low Isle (west) ──────────────────────────────────────────────────────

    private static void BuildLowIsle()
    {
        var g = Group("Low Isle");
        Box(g, "Low Isle Slab", -108, -76, -4, 0, -40, -8, Kind.Floor);
        Box(g, "Low Isle Underside", -105, -79, -9, -4, -37, -11, Kind.Dark);
        // High side: a mound (top 6) with a running tunnel through it
        Box(g, "Mound S", -108, -92, 0, Mid, -40, -26, Kind.Floor);
        Box(g, "Mound Tunnel Roof", -108, -92, 4.5f, Mid, -26, -22, Kind.Floor);
        Tunnel("Low Isle Tunnel", new Vector3(-108, 0, -24), new Vector3(-92, 0, -24), "run");
        Box(g, "Mound N", -108, -92, 0, Mid, -22, -8, Kind.Floor);
        Box(g, "Mound Step", -92, -89, 0, Low, -16, -12, Kind.Ledge);
        Hut(g, "Mound Hut", -104, -96, -20, -12, Mid, 4.5f, 'E', 'S');
        Lamp(g, new Vector3(-92, 12, -24), 18f);
        // Low side: a small block with a crawl hole
        Box(g, "Crawl Block S", -84, -78, 0, 3, -36, -33.25f, Kind.Wall);
        Box(g, "Crawl Block Roof", -84, -78, 1.7f, 3, -33.25f, -30.75f, Kind.Wall);
        Box(g, "Crawl Block N", -84, -78, 0, 3, -30.75f, -28, Kind.Wall);
        Tunnel("Low Isle Crawl", new Vector3(-84, 0, -32), new Vector3(-78, 0, -32), "crawl");
        Prop(g, "Crate-1x1m", new Vector3(-82, 0, -14), 25f, 1.3f);

        // Easy route from the main platform: a 3 m walkway, no rails
        Box(g, "Walkway to Low Isle", -76, -55, -1, 0, -20, -17, Kind.Floor);
        // Harder route: stepping stones (4, 4 and 7 m gaps)
        var s1 = Box(g, "Stone 1", -62, -59, -1, 1, -34, -31, Kind.Ledge);
        var s2 = Box(g, "Stone 2", -69, -66, -1, 2, -34, -31, Kind.Ledge);
        gaps.Add(("Main → Stone 1", new Vector3(-55, 0, -32.5f), new Vector3(-59, 1, -32.5f)));
        gaps.Add(("Stone 1 → Stone 2", new Vector3(-62, 1, -32.5f), new Vector3(-66, 2, -32.5f)));
        gaps.Add(("Stone 2 → Low Isle", new Vector3(-69, 2, -32.5f), new Vector3(-76, 0, -32.5f)));
    }

    // ─── High Isle (south-east) ───────────────────────────────────────────────

    private static void BuildHighIsle()
    {
        var g = Group("High Isle");
        // Low side top 4, high side top 10 with a running tunnel through it
        Box(g, "High Isle Slab", 72, 104, -4, 4, -104, -72, Kind.Floor);
        Box(g, "High Isle Underside", 75, 101, -10, -4, -101, -75, Kind.Dark);
        Box(g, "High Side S", 88, 104, 4, 10, -104, -90, Kind.Floor);
        Box(g, "High Side Tunnel Roof", 88, 104, 8.5f, 10, -90, -86, Kind.Floor);
        Tunnel("High Isle Tunnel", new Vector3(88, 4, -88), new Vector3(104, 4, -88), "run");
        Box(g, "High Side N", 88, 104, 4, 10, -86, -72, Kind.Floor);
        Box(g, "High Side Step", 85, 88, 4, 4 + Low, -80, -75, Kind.Ledge);
        // Tower (top 18) on the high side, with ledges
        Box(g, "Isle Tower", 96, 102, 10, 18, -102, -96, Kind.Wall);
        Box(g, "Isle Tower Ledge", 93, 96, 10, 14, -102, -99, Kind.Ledge);
        // Crawl hole on the low side
        Box(g, "Isle Crawl W", 74, 80, 4, 7, -100, -97.25f, Kind.Wall);
        Box(g, "Isle Crawl Roof", 74, 80, 5.7f, 7, -97.25f, -94.75f, Kind.Wall);
        Box(g, "Isle Crawl E", 74, 80, 4, 7, -94.75f, -92, Kind.Wall);
        Tunnel("High Isle Crawl", new Vector3(74, 4, -96), new Vector3(80, 4, -96), "crawl");
        Lamp(g, new Vector3(99, 21, -99), 18f);

        // The brave route: a moving platform from the plateau's south-east corner to the isle
        Mover(g, "Plateau Shuttle", new Vector3(58, Mid - 1, -58), new Vector3(70, 3, -70), new Vector3(4, 1, 4), 3f);
    }

    // Narrow walkable paths over the pit between the main platform and the High Isle: a maze whose walls are drops
    private static void BuildPitMaze()
    {
        var g = Group("Pit Maze");
        float w = 2.5f;
        Box(g, "Maze A", 48, 48 + w, -1, 0, -68, -55, Kind.Floor);            // South from the main platform
        Box(g, "Maze B", 48, 64, -1, 0, -68 - w, -68, Kind.Floor);             // East
        Box(g, "Maze C", 61.5f, 64, -1, 0, -84, -70.5f, Kind.Floor);           // South
        Box(g, "Maze D", 50, 61.5f, -1, 0, -86.5f, -84, Kind.Floor);           // West (to the south walkway)
        Box(g, "Maze E", 64, 69, -1, 0, -82.5f, -80, Kind.Floor);              // East toward the isle
        Box(g, "Maze Step", 69, 72, -1, 2, -83, -79, Kind.Ledge);               // Up 2, then 2 more onto the isle
        // A parallel short-cut with a hop: from Maze A straight south, a 3 m gap, then to Maze D
        Box(g, "Maze Short 1", 48, 48 + w, -1, 0, -79, -73, Kind.Floor);
        Box(g, "Maze Short 2", 48, 48 + w, -1, 0, -86.5f, -82, Kind.Floor);
        gaps.Add(("Maze shortcut hop", new Vector3(49, 0, -70.5f), new Vector3(49, 0, -73)));
        gaps.Add(("Maze short 1 → 2", new Vector3(49, 0, -79), new Vector3(49, 0, -82)));
    }

    // A long narrow walkway along the main platform's south side, from the Low Isle to the maze: no rails,
    // two hops, bridges back up to the main platform
    private static void BuildSouthWalkway()
    {
        var g = Group("South Walkway");
        float z0 = -64.5f, z1 = -62f;
        Box(g, "Walk Down From Low Isle", -92, -89.5f, -1, 0, -62, -40, Kind.Floor);
        Box(g, "Walk W", -92, -40, -1, 0, z0, z1, Kind.Floor);
        Box(g, "Walk Mid", -37, 10, -1, 0, z0, z1, Kind.Floor);   // 3 m hop at x -40..-37
        Box(g, "Walk E", 20, 50, -1, 0, z0, z1, Kind.Floor);      // A 10 m gap crossed on a shuttle
        Mover(g, "Walkway Shuttle", new Vector3(12, -1, (z0 + z1) / 2), new Vector3(18, -1, (z0 + z1) / 2), new Vector3(4, 1, 2.5f), 3f);
        // (Walk E runs into Maze A, so the south walkway joins the maze)
        gaps.Add(("Walkway hop W", new Vector3(-40, 0, -63), new Vector3(-37, 0, -63)));
        // Bridges up to the main platform (3 m wide)
        Box(g, "Bridge W", -32, -29, -1, 0, -62, -55, Kind.Floor);
        Box(g, "Bridge E", 6, 9, -1, 0, -62, -55, Kind.Floor);
    }

    // Kill plane under everything, an invisible ceiling high above
    private static void BuildBounds()
    {
        var g = Group("Bounds");
        var kill = new GameObject("Kill Plane");
        kill.transform.SetParent(g, false);
        kill.transform.position = new Vector3(0, -40, -20);
        var box = kill.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(400, 4, 400);
        kill.AddComponent<DeathZone>();

        var ceiling = new GameObject("Invisible Ceiling");
        ceiling.transform.SetParent(g, false);
        ceiling.transform.position = new Vector3(0, 60, -20);
        ceiling.AddComponent<BoxCollider>().size = new Vector3(400, 2, 400);
    }

    private static readonly (string name, float x, float z, float y)[] SpawnSpots =
    {
        ("Plaza North",      -2,  12, 0),
        ("Plaza South",       4, -12, 0),
        ("Plaza West",      -12,   0, 0),
        ("Tunnel East",     -14,  30, 0),
        ("Tunnel Roofs",    -46,  44, 5),
        ("South West",      -24, -46, 0),
        ("Shed Yard",       -41, -30, 0),
        ("Plateau Huts",     33,  44, Mid),
        ("Plateau Arcade",   34,  -7, Mid),
        ("Plateau South",    30, -40, Mid),
        ("Low Isle",        -84, -20, 0),
        ("High Isle",        80, -84, 4),
    };

    private static void BuildSpawns()
    {
        var g = Group("Spawn Points");
        int i = 0;
        foreach (var (name, x, z, y) in SpawnSpots)
        {
            var go = new GameObject($"Spawn {++i:00} {name}");
            go.transform.SetParent(g, false);
            var feet = new Vector3(x, y + 0.1f, z);
            Vector3 look = new Vector3(0, feet.y, 0) - feet;
            go.transform.SetPositionAndRotation(feet, Quaternion.LookRotation(look.sqrMagnitude > 1f ? look : Vector3.forward));
            go.AddComponent<SpawnPoint>();
        }
    }

    private static void PlaceSpawner()
    {
        var spawner = Object.FindFirstObjectByType<Spawner>(FindObjectsInactive.Include);
        if (spawner == null) { report.Add("! no Spawner in the scene"); return; }
        spawner.transform.position = new Vector3(-6, 0.1f, -6);
        var so = new SerializedObject(spawner);
        so.FindProperty("killHeight").floatValue = -30f;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ─── Composite pieces ─────────────────────────────────────────────────────

    /// <summary>A hollow hut: 1 m walls, a 1 m roof, a 3 m × 3.8 m door on each side listed.</summary>
    private static void Hut(Transform parent, string name, float x0, float x1, float z0, float z1, float floor, float height, params char[] doors)
    {
        var g = Group(name, parent);
        const float t = 1f, doorW = 3f, doorH = 3.8f;
        float top = floor + height;
        void Side(char side, float a0, float a1, float fixedA, float fixedB, bool alongX)
        {
            bool door = doors.Contains(side);
            float c = (a0 + a1) * 0.5f;
            if (!door)
            {
                if (alongX) Box(g, $"{name} {side}", a0, a1, floor, top, fixedA, fixedB, Kind.Wall);
                else Box(g, $"{name} {side}", fixedA, fixedB, floor, top, a0, a1, Kind.Wall);
                return;
            }
            if (alongX)
            {
                Box(g, $"{name} {side}1", a0, c - doorW / 2, floor, top, fixedA, fixedB, Kind.Wall);
                Box(g, $"{name} {side}2", c + doorW / 2, a1, floor, top, fixedA, fixedB, Kind.Wall);
                Box(g, $"{name} {side} Lintel", c - doorW / 2, c + doorW / 2, floor + doorH, top, fixedA, fixedB, Kind.Wall);
                Tunnel($"{name} door {side}", new Vector3(c, floor, fixedA - 1), new Vector3(c, floor, fixedB + 1), "run");
            }
            else
            {
                Box(g, $"{name} {side}1", fixedA, fixedB, floor, top, a0, c - doorW / 2, Kind.Wall);
                Box(g, $"{name} {side}2", fixedA, fixedB, floor, top, c + doorW / 2, a1, Kind.Wall);
                Box(g, $"{name} {side} Lintel", fixedA, fixedB, floor + doorH, top, c - doorW / 2, c + doorW / 2, Kind.Wall);
                Tunnel($"{name} door {side}", new Vector3(fixedA - 1, floor, c), new Vector3(fixedB + 1, floor, c), "run");
            }
        }
        Side('S', x0, x1, z0, z0 + t, true);
        Side('N', x0, x1, z1 - t, z1, true);
        Side('W', z0 + t, z1 - t, x0, x0 + t, false);
        Side('E', z0 + t, z1 - t, x1 - t, x1, false);
        Box(g, $"{name} Roof", x0, x1, top, top + 0.5f, z0, z1, Kind.Floor);
    }

    // ─── Building blocks ──────────────────────────────────────────────────────

    private enum Kind { Floor, Wall, Ledge, Dark }

    private static Transform Group(string name, Transform parent = null)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent != null ? parent : root, false);
        return t;
    }

    /// <summary>A box from its world-space extents. Floors: light top; walls: mid all over; ledges: orange top; dark: supports.</summary>
    private static GameObject Box(Transform parent, string name, float x0, float x1, float y0, float y1, float z0, float z1, Kind kind)
    {
        var size = new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0));
        var pb = ShapeGenerator.GenerateCube(PivotLocation.Center, size);
        pb.name = name;
        pb.transform.SetParent(parent, false);
        pb.transform.position = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
        Material sides = kind == Kind.Dark ? dark : mid;
        Material top   = kind switch { Kind.Ledge => ledge, Kind.Dark => dark, Kind.Wall => mid, _ => light };
        foreach (var face in pb.faces)
        {
            var uv = face.uv;
            uv.useWorldSpace = true;
            face.uv = uv;
            pb.SetMaterial(new[] { face }, UnityEngine.ProBuilder.Math.Normal(pb, face).y > 0.9f ? top : sides);
        }
        pb.ToMesh();
        pb.Refresh();
        var col = pb.gameObject.AddComponent<BoxCollider>(); // Boxes: exact, cheap, no seams
        col.size = size;
        GameObjectUtility.SetStaticEditorFlags(pb.gameObject, (StaticEditorFlags)~0);
        return pb.gameObject;
    }

    /// <summary>A ramp along +Z (x0..x1 wide) from height y0 at z0 to y1 at z1 (walkable up to ~50°).</summary>
    private static void Ramp(Transform parent, string name, float x0, float x1, float z0, float z1, float y0, float y1)
    {
        float run = z1 - z0, rise = y1 - y0;
        float length = Mathf.Sqrt(run * run + rise * rise);
        var pb = ShapeGenerator.GenerateCube(PivotLocation.Center, new Vector3(x1 - x0, 0.6f, length));
        pb.name = name;
        pb.transform.SetParent(parent, false);
        float angle = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
        pb.transform.rotation = Quaternion.Euler(-angle, 0f, 0f);
        // Its top surface runs from (z0, y0) to (z1, y1)
        Vector3 topMid = new((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
        pb.transform.position = topMid - pb.transform.up * 0.3f;
        foreach (var face in pb.faces)
        {
            var uv = face.uv; uv.useWorldSpace = true; face.uv = uv;
            pb.SetMaterial(new[] { face }, light);
        }
        pb.ToMesh();
        pb.Refresh();
        pb.gameObject.AddComponent<BoxCollider>().size = new Vector3(x1 - x0, 0.6f, length);
        GameObjectUtility.SetStaticEditorFlags(pb.gameObject, (StaticEditorFlags)~0);
        if (angle > 45f) report.Add($"! ramp {name} is {angle:0}° (too steep to walk)");
        else report.Add($"ramp {name}: {angle:0}°");
    }

    /// <summary>A plain moving platform (no trim: Justin dislikes the strips) between two base points.</summary>
    private static void Mover(Transform parent, string name, Vector3 aBase, Vector3 bBase, Vector3 size, float speed)
    {
        var g = Group(name, parent);
        var a = new GameObject("Point A").transform; a.SetParent(g, false); a.position = aBase + Vector3.up * (size.y * 0.5f);
        var b = new GameObject("Point B").transform; b.SetParent(g, false); b.position = bBase + Vector3.up * (size.y * 0.5f);
        var platform = Box(g, "Platform", aBase.x - size.x / 2, aBase.x + size.x / 2, aBase.y, aBase.y + size.y,
                           aBase.z - size.z / 2, aBase.z + size.z / 2, Kind.Floor);
        GameObjectUtility.SetStaticEditorFlags(platform, 0);
        var rb = platform.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        var mover = platform.AddComponent<MovingPlatform>();
        var so = new SerializedObject(mover);
        so.FindProperty("pointA").objectReferenceValue = a;
        so.FindProperty("pointB").objectReferenceValue = b;
        so.FindProperty("speed").floatValue = speed;
        so.FindProperty("waitAtEnds").boolValue = true;
        so.FindProperty("waitTime").floatValue = 1.5f;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static GameObject Prop(Transform parent, string prefab, Vector3 basePoint, float yaw, float scale = 1f)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Environment/Prefabs/{prefab}.prefab");
        if (asset == null) { report.Add($"! missing prefab {prefab}"); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * asset.transform.rotation;
        go.transform.localScale = asset.transform.localScale * scale;
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length > 0)
        {
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            go.transform.position += basePoint - new Vector3(b.center.x, b.min.y, b.center.z);
        }
        else go.transform.position = basePoint;
        return go;
    }

    private static void Bounce(Transform parent, Vector3 basePoint, float force)
    {
        var pad = Prop(parent, "BouncePadv2", basePoint, 0f);
        if (pad == null) return;
        var bounce = pad.GetComponentInChildren<BouncePad>();
        if (bounce == null) return;
        var so = new SerializedObject(bounce);
        so.FindProperty("bounceForce").floatValue = force;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Lamp(Transform parent, Vector3 pos, float range)
    {
        var go = new GameObject("Lamp");
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.85f, 0.65f);
        l.range = range;
        l.intensity = 2.5f;
        l.shadows = LightShadows.None;
    }

    private static void Tunnel(string name, Vector3 from, Vector3 to, string kind) => tunnels.Add((name, from, to, kind));

    // ─── Review ───────────────────────────────────────────────────────────────

    // Checks clearances along every tunnel and door, that every spawn point is clear, and measures every gap
    private static void Review()
    {
        report.Add("=== Pinnacle review ===");
        int mask = 1 << Structure;
        foreach (var (name, from, to, kind) in tunnels)
        {
            int samples = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(from, to) / 1.5f));
            int standBlocked = 0, crouchBlocked = 0, proneBlocked = 0;
            for (int i = 1; i < samples; i++)
            {
                Vector3 p = Vector3.Lerp(from, to, i / (float)samples) + Vector3.up * 0.05f;
                if (Blocked(p, Stand, mask)) standBlocked++;
                if (Blocked(p, Crouch, mask)) crouchBlocked++;
                if (Blocked(p, Radius * 2f + 0.02f, mask)) proneBlocked++;
            }
            string verdict = kind switch
            {
                "run"    => standBlocked == 0 ? "ok" : $"! a standing player is blocked at {standBlocked}/{samples - 1} points",
                "crouch" => crouchBlocked == 0 && standBlocked > 0 ? "ok" : crouchBlocked > 0 ? "! too low even crouched" : "! not low enough (can stand)",
                "crawl"  => proneBlocked == 0 && crouchBlocked > 0 ? "ok" : proneBlocked > 0 ? "! too low even prone" : "! not low enough (can crouch)",
                _ => "?",
            };
            report.Add($"{(verdict.StartsWith("!") ? "" : "  ")}{verdict.Replace("ok", $"ok   {kind,-6} {name}")}{(verdict.StartsWith("!") ? $"  [{kind} {name}]" : "")}");
        }

        foreach (var sp in Object.FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None))
        {
            Vector3 p = sp.transform.position;
            bool blocked = Blocked(p + Vector3.up * 0.1f, Stand, mask);
            bool floor = Physics.Raycast(p + Vector3.up, Vector3.down, 2f, mask);
            report.Add($"{(blocked || !floor ? "! " : "  ")}spawn {sp.name}: {(blocked ? "BLOCKED " : "")}{(floor ? "" : "NO FLOOR")}{(!blocked && floor ? "ok" : "")}");
        }

        foreach (var (name, a, b) in gaps)
        {
            float flat = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
            float rise = b.y - a.y;
            string note = flat > 9f ? "! longer than a sprint jump" : flat > 7.5f ? "risky (over 7.5 m)" : "ok";
            report.Add($"{(note.StartsWith("!") ? "" : "  ")}gap {name}: {flat:0.0} m across, {rise:+0.0;-0.0;0} m up  {note}");
        }
    }

    // A capsule of this height standing at p (feet) overlaps the level?
    private static bool Blocked(Vector3 feet, float height, int mask)
    {
        Vector3 bottom = feet + Vector3.up * Radius;
        Vector3 top = feet + Vector3.up * Mathf.Max(Radius, height - Radius);
        return Physics.CheckCapsule(bottom, top, Radius * 0.95f, mask, QueryTriggerInteraction.Ignore);
    }

    // ─── Pictures for the review ──────────────────────────────────────────────

    private static void RenderOverviews()
    {
        Shot("pinnacle_top", new Vector3(0, 160, -25), Quaternion.Euler(90, 0, 0), true, 125f);
        Shot("pinnacle_sw", new Vector3(-110, 70, -120), Quaternion.LookRotation(new Vector3(95, -55, 110)), false, 0f);
        Shot("pinnacle_ne", new Vector3(110, 60, 95), Quaternion.LookRotation(new Vector3(-105, -45, -110)), false, 0f);
        Shot("pinnacle_plaza", new Vector3(-30, 14, -30), Quaternion.LookRotation(new Vector3(40, -8, 45)), false, 0f);
        Shot("pinnacle_tunnels", new Vector3(-8, 20, 6), Quaternion.LookRotation(new Vector3(-30, -14, 30)), false, 0f);
    }

    private static void Shot(string file, Vector3 pos, Quaternion rot, bool ortho, float size)
    {
        var go = new GameObject("Review Camera");
        var cam = go.AddComponent<Camera>();
        go.transform.SetPositionAndRotation(pos, rot);
        cam.orthographic = ortho;
        if (ortho) cam.orthographicSize = size;
        cam.fieldOfView = 60f;
        cam.farClipPlane = 1000f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.12f, 0.16f);
        var rt = new RenderTexture(1600, ortho ? 1600 : 900, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes($"Library/TurboTag/scratch/{file}.png", tex.EncodeToPNG());
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(go);
    }

    private static void LevelTools_Sync()
    {
        // New scenes in the Levels folder join Build Settings (and the lobby's map list) automatically
        LevelTools.SyncBuildSettings();
    }

    private static void LoadMaterials()
    {
        light = Mat("Assets/Environment/Materials/Large/WallLight.mat");
        mid   = Mat("Assets/Environment/Materials/Large/WallOfflight.mat");
        dark  = Mat("Assets/Environment/Materials/Large/WallDark.mat");
        ledge = Mat("Assets/Environment/Materials/Concrete/AccentOrange.mat");
    }

    private static Material Mat(string path)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) report.Add($"! missing material {path}");
        return m;
    }
}
