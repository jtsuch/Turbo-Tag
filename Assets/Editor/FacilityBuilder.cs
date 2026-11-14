using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static GreyboxKit;
using static KitLevel;
using static RoomLevel;

/// <summary>
/// Builds Facility (Assets/Scenes/Levels/SearchDestroy/Facility.unity): an indoor Search &amp; Destroy map made of
/// **rooms** (rectangles of 8 m cells, each with its own floor height and ceiling) joined by doorways, following Justin's
/// S&amp;D notes (CLAUDE.md, "Search &amp; Destroy map design"). Draft 3 (Nov 13 2026): smaller rooms, mostly two-storey
/// ceilings (mid and A site three), one-storey upper corridors, small sites with the plant zone in the middle.
///
///   Defense spawn (west) → a link north to A site and one south to B site, plus stairs up to the **defender walk**: a
///     one-storey corridor at 5 into mid's west balcony (a vantage point over mid; a flank route for attackers).
///   Offense spawn (east) → the A lobby and the B lobby only.
///   A main: A lobby → a long room north → west through a hall (a container makes you step aside) → a jog south into a
///     small room → a door into A site's east side. Three turns, no straight line.
///   B main, two levels: from B lobby into a room with stairs. Flat: west through a door, a dip south, a room north,
///     a door into B site under its ledge. Upper: up the stairs into a one-storey corridor at 5 that runs west and ends
///     on B site's ledge.
///   Mid (three storeys): a west balcony (the defender walk arrives on it, stairs down), a column of containers down the
///     middle (a stacked pair in the centre), five doors: mid top / mid bottom from the lobbies' connectors, A short and
///     B short to the sites, the defender walk up high.
/// Floors, walls (storey by storey, sealed at every corner), ceilings, balconies, lights and callouts come from the shared
/// RoomLevel builder (Cistern uses it too); this file holds the layout, the stairs and the dressing.
/// Review: Library/TurboTag/scratch/facility_report.txt and facility_*.png.
/// Menu: Turbo Tag/Levels/Search &amp; Destroy/Rebuild Facility (editor job: BuildFacility).
/// </summary>
public static class FacilityBuilder
{
    private const string ScenePath  = "Assets/Scenes/Levels/SearchDestroy/Facility.unity";
    private const string RootName   = "Facility";
    private const string ReportPath = "Library/TurboTag/scratch/facility_report.txt";
    private const float G = 0f, L5 = 5f;
    private const float OX = -80f, OZ = -68f;   // The grid's origin: cell (i, j) spans x = OX + 8i .. +8, z = OZ + 8j .. +8

    // Ties between two rooms with the same ceiling put the wall in the later one, so the big rooms come first and the
    // upper corridors last
    private static readonly Room[] Layout =
    {
        new() { Name = "Defense Spawn", Callout = "Defense Spawn",  I0 = 0,  I1 = 2,  J0 = 6,  J1 = 10, Tile = "Floor-Inlay-8m" },
        new() { Name = "A Site", Callout = "A Site",         I0 = 2,  I1 = 5,  J0 = 12, J1 = 15, Storeys = 3, Tile = "Floor-Tiles-8m" },
        new() { Name = "B Site", Callout = "B Site",         I0 = 2,  I1 = 5,  J0 = 1,  J1 = 4,  Tile = "Floor-Grate-8m" },
        new() { Name = "Mid", Callout = "Mid",            I0 = 7,  I1 = 11, J0 = 6,  J1 = 10, Storeys = 3, Tile = "Floor-Lanes-8m" },
        new() { Name = "Offense Spawn", Callout = "Offense Spawn",  I0 = 17, I1 = 19, J0 = 6,  J1 = 10, Tile = "Floor-Inlay-8m" },
        new() { Name = "A Link",         I0 = 1,  I1 = 2,  J0 = 11, J1 = 11 },
        new() { Name = "B Link",         I0 = 1,  I1 = 2,  J0 = 5,  J1 = 5 },
        new() { Name = "A Short", Callout = "A Short",        I0 = 5,  I1 = 7,  J0 = 11, J1 = 11 },
        new() { Name = "B Short", Callout = "B Short",        I0 = 5,  I1 = 7,  J0 = 5,  J1 = 5 },
        new() { Name = "Mid Top", Callout = "Mid Top",        I0 = 12, I1 = 13, J0 = 10, J1 = 11 },
        new() { Name = "Mid Bottom", Callout = "Mid Bottom",     I0 = 12, I1 = 13, J0 = 5,  J1 = 6 },
        new() { Name = "A Lobby", Callout = "A Lobby",        I0 = 14, I1 = 17, J0 = 11, J1 = 14 },
        new() { Name = "B Lobby", Callout = "B Lobby",        I0 = 14, I1 = 17, J0 = 2,  J1 = 5 },
        new() { Name = "A Main Long", Callout = "A Main", Unlabelled = true,    I0 = 12, I1 = 15, J0 = 15, J1 = 15 },
        new() { Name = "A Main Hall", Callout = "A Main",    I0 = 8,  I1 = 11, J0 = 14, J1 = 15 },
        new() { Name = "A Main Corner", Callout = "A Main", Unlabelled = true,  I0 = 6,  I1 = 7,  J0 = 13, J1 = 14 },
        new() { Name = "B Main Stairs", Callout = "B Main", Unlabelled = true,  I0 = 11, I1 = 13, J0 = 1,  J1 = 3 },
        new() { Name = "B Main Dip", Callout = "B Main",     I0 = 8,  I1 = 10, J0 = 0,  J1 = 1 },
        new() { Name = "B Main Corner", Callout = "B Main", Unlabelled = true,  I0 = 6,  I1 = 7,  J0 = 0,  J1 = 3 },
        new() { Name = "Defender Walk", Callout = "Defender Walk",  I0 = 3,  I1 = 6,  J0 = 8,  J1 = 8,  Base = 1, Storeys = 1 },
        new() { Name = "Upper B Main", Callout = "Upper B Main",   I0 = 6,  I1 = 11, J0 = 4,  J1 = 4,  Base = 1, Storeys = 1 },
    };

    // Openings between two rooms' cells in one storey: D a one-storey archway, O a wide opening (8 m, one storey high)
    private static readonly (int ai, int aj, int bi, int bj, char kind, int storey)[] Doors =
    {
        (1, 10, 1, 11, 'D', 0),                            // Defense spawn → A link
        (2, 11, 2, 12, 'O', 0),                            // A link → A site
        (1, 6, 1, 5, 'D', 0),                              // Defense spawn → B link
        (2, 5, 2, 4, 'O', 0),                              // B link → B site
        (2, 8, 3, 8, 'O', 1),                              // Defense spawn (top of its stairs) → the defender walk
        (6, 8, 7, 8, 'D', 1),                              // Defender walk → mid's balcony
        (7, 11, 7, 10, 'D', 0), (5, 11, 5, 12, 'D', 0),    // Mid ↔ A short ↔ A site
        (7, 5, 7, 6, 'D', 0), (5, 5, 5, 4, 'D', 0),        // Mid ↔ B short ↔ B site
        (17, 10, 17, 11, 'D', 0), (17, 6, 17, 5, 'D', 0),  // Offense spawn → A lobby, B lobby
        (12, 10, 11, 10, 'D', 0), (13, 11, 14, 11, 'D', 0),// Mid ↔ mid top ↔ A lobby
        (12, 6, 11, 6, 'D', 0), (13, 5, 14, 5, 'D', 0),    // Mid ↔ mid bottom ↔ B lobby
        (14, 14, 14, 15, 'O', 0), (15, 14, 15, 15, 'O', 0),// A lobby → A main long
        (12, 15, 11, 15, 'D', 0),                          // → A main hall
        (8, 14, 7, 14, 'D', 0),                            // → A main corner
        (6, 13, 5, 13, 'D', 0),                            // → A site
        (14, 2, 13, 2, 'O', 0), (14, 3, 13, 3, 'O', 0),    // B lobby → B main stairs room
        (11, 1, 10, 1, 'D', 0),                            // → B main dip (flat route)
        (8, 0, 7, 0, 'D', 0),                              // → B main corner
        (6, 2, 5, 2, 'D', 0),                              // → B site, under its ledge
        (11, 3, 11, 4, 'O', 1),                            // Top of the stairs → upper B main
        (6, 4, 5, 4, 'D', 1),                              // Upper B main → B site's ledge
    };

    [MenuItem("Turbo Tag/Levels/Search & Destroy/Rebuild Facility")]
    public static void Build()
    {
        Reset(20261113, "Assets/Environment/Generated/Facility_Unused.asset", "Facility", G);
        if (!System.IO.File.Exists(ScenePath)) AssetDatabase.CopyAsset("Assets/Scenes/Levels/Stairfall.unity", ScenePath); // Any built level: its geometry is cleared below
        var scene = Begin(ScenePath, RootName);
        var geometry = root.parent;
        for (int i = geometry.childCount - 1; i >= 0; i--)
            if (geometry.GetChild(i) != root) Object.DestroyImmediate(geometry.GetChild(i).gameObject);

        Setup(Layout, Doors, OX, OZ);

        BuildFloors();
        BuildWalls();
        BuildCeilings();
        BuildBalconiesAndStairs();
        DressRooms();
        BuildSpawnsAndSites();
        BuildCallouts();
        BuildLights();
        CheckAccess();
        Headroom("Defense spawn stairs", new Vector3(-66, G, 0), new Vector3(-56, L5, 0));
        Headroom("Mid balcony stairs", new Vector3(-6, G, -8), new Vector3(-16, L5, -8));
        Headroom("A site balcony stairs", new Vector3(-60, G, 42), new Vector3(-60, L5, 52));
        Headroom("B site ledge stairs", new Vector3(-50, G, -56), new Vector3(-40, L5, -56));
        Headroom("B main stairs", new Vector3(12, G, -46), new Vector3(12, L5, -36));
        report.Add($"cubbies: {cubbies.Count}");
        foreach (var c in cubbies) report.Add("  " + c);
        // Barriers after the access checks: they're pre-round walls, so the checks shouldn't see them (they can stand
        // right by a doorway); the overlap check still does
        BuildSpawnBarriers();
        CheckOverlaps();
        Bounds(Vector3.zero, 400f, -25f, 30f);
        PlaceSpawner(new Vector3(8f, 0.1f, 0f), -20f);
        Atmosphere.SunnyDay(root, new Vector3(1f, 0f, 0.35f), new Bounds(new Vector3(0, 7, -4), new Vector3(170, 14, 140)), windOn: false);

        Finish(scene, ReportPath, "Facility", RenderOverviews);
    }

    private static void BuildBalconiesAndStairs()
    {
        var g = Group("Balconies");
        // Defense spawn: stairs east up to the defender walk's archway at 5
        K(g, "Stairs-8x10m", new Vector3(-61, G - 1f, 0), 90f);
        // Mid: a balcony along the west wall (the defender walk arrives on it), stairs down east from its middle-south
        Balcony(g, -24, -16, -20, 20, L5, 'E', -8f);
        K(g, "Stairs-8x10m", new Vector3(-11, G - 1f, -8), -90f);
        // A site: a balcony along the back (north) wall, stairs up its west end
        Balcony(g, -64, -32, 52, 60, L5, 'S', -60f);
        K(g, "Stairs-8x10m", new Vector3(-60, G - 1f, 47), 0f);
        // B site: a ledge along the east wall (upper B main arrives on it), stairs up from the west along its south end
        Balcony(g, -40, -32, -60, -28, L5, 'W', -56f);
        K(g, "Stairs-8x10m", new Vector3(-45, G - 1f, -56), 90f);
        // B main: stairs north up the stairs room's west side into upper B main
        K(g, "Stairs-8x10m", new Vector3(12, G - 1f, -41), 0f);
    }

    // ─── Dressing: cover that breaks the lines ────────────────────────────────

    private static void DressRooms()
    {
        var g = Group("Dressing");
        // A site (tiled): a container across the middle of the plant zone (blocks the line in from A main), crates and
        // L covers round it, a barrier facing the A short door
        Container(g, -48, G, 44, 90f);
        K(g, "CrateStack-2x2-3m", new Vector3(-40, G, 36), 0f);
        K(g, "LCover-4m", new Vector3(-56, G, 36), 0f);
        K(g, "LCover-4m", new Vector3(-40, G, 49), 90f);
        Barrier(g, -45, G, 32, 0f);

        // B site (grated): a container in the plant zone, crates and an L cover
        Container(g, -48, G, -44, 0f);
        K(g, "CrateStack-2x2-3m", new Vector3(-56, G, -36), 0f);
        K(g, "LCover-4m", new Vector3(-57, G, -50), 90f);

        // Mid: containers down the middle (x 0): one in the top lane, a stacked pair in the centre, one in the bottom lane;
        // walking across means going round them; crates by the balcony
        Container(g, 0, G, 15.5f, 90f);
        Container(g, 0, G, 0, 90f);
        Container(g, 0, G + ContainerHeight, 0, 90f);
        Container(g, 0, G, -15.5f, 90f);
        K(g, "CrateStack-2x2-3m", new Vector3(-9, G, 13), 0f);
        K(g, "CrateStack-2x2-3m", new Vector3(-8, G, -16.5f), 0f);

        // Defender walk (one storey at 5): a crate stack to slow the long look down it
        K(g, "CrateStack-2x2-3m", new Vector3(-40, L5, 2), 0f);
        // Upper B main (one storey at 5): two staggered crate stacks
        K(g, "CrateStack-2x2-3m", new Vector3(-4, L5, -30.5f), 0f);
        K(g, "CrateStack-2x2-3m", new Vector3(-20, L5, -33.5f), 0f);

        // Lobbies: a stacked container pair hides the split (main or connector)
        Container(g, 48, G, 36, 0f);
        Container(g, 48, G + ContainerHeight, 36, 0f);
        Container(g, 48, G, -36, 0f);
        Container(g, 48, G + ContainerHeight, -36, 0f);

        // A main: crates in the long room, a container blocking the hall's top lane (step south round it), crates in
        // the corner room
        K(g, "CrateStack-2x2-3m", new Vector3(24, G, 57.5f), 0f);
        Container(g, 0, G, 55.5f, 90f);
        K(g, "CrateStack-2x2-3m", new Vector3(-24, G, 44), 0f);

        // B main (flat): crates in the stairs room, a container in the dip's top lane, crates in the corner room
        K(g, "CrateStack-2x2-3m", new Vector3(24, G, -50), 0f);
        Container(g, -4, G, -55.5f, 0f);
        K(g, "CrateStack-2x2-3m", new Vector3(-24, G, -58), 0f);
    }

    private static void BuildSpawnsAndSites()
    {
        var g = Group("Spawn Points");
        // Defenders round their stairs (x −66..−56, z −4..4); attackers in two rows
        Spawns(g, SpawnPoint.Side.Defend, new Vector3(0, G, 0),
               new Vector3(-76, G, -14), new Vector3(-76, G, 0), new Vector3(-76, G, 14), new Vector3(-62, G, -14), new Vector3(-62, G, 14), new Vector3(-70, G, -7));
        Spawns(g, SpawnPoint.Side.Attack, new Vector3(0, G, 0),
               new Vector3(74, G, -12), new Vector3(74, G, 0), new Vector3(74, G, 12), new Vector3(64, G, -12), new Vector3(64, G, 0), new Vector3(64, G, 12));
        var sites = Group("Bomb Sites");
        // Plant zones: the 16 × 16 in each site's middle (balconies, ledges and stairs are outside)
        Site(sites, "A", new Vector3(-48, G, 44), 16f);
        Site(sites, "B", new Vector3(-48, G, -44), 16f);
    }

    // Pre-round walls: defenders hold the spawn, the links and both sites (a step into the shorts, peeking into the
    // mains' corner rooms, part way along the defender walk); attackers hold their spawn and both lobbies (a couple of
    // metres into the mains and the mid connectors). Justin, Nov 13: keep both teams well short of mid.
    private static void BuildSpawnBarriers()
    {
        var g = Group("Spawn Barriers");
        var d = SpawnPoint.Side.Defend; var a = SpawnPoint.Side.Attack;
        SpawnWall(g, d, -31.25f, -30.75f, G, 10, 20, 28);      // A short, just past the site's door
        SpawnWall(g, d, -27.75f, -27.25f, G, 10, 36, 52);      // A main corner
        SpawnWall(g, d, -31.25f, -30.75f, G, 10, -28, -20);    // B short, just past the site's door
        SpawnWall(g, d, -27.75f, -27.25f, G, 10, -68, -36);    // B main corner
        SpawnWall(g, d, -27.75f, -27.25f, L5, 10, -36, -28);   // Upper B main
        SpawnWall(g, d, -36.25f, -35.75f, L5, 10, -4, 4);      // Defender walk, half way: well short of mid's balcony
        SpawnWall(g, a, 30.75f, 31.25f, G, 10, 52, 60);        // A main long, just past the lobby's doors
        SpawnWall(g, a, 29.75f, 30.25f, G, 10, 12, 28);        // Mid top, 2 m in
        SpawnWall(g, a, 29.25f, 29.75f, G, 10, -60, -36);      // B main stairs room, 2 m in
        SpawnWall(g, a, 29.75f, 30.25f, G, 10, -28, -12);      // Mid bottom, 2 m in
    }

    private static void RenderOverviews()
    {
        var ceilings = root.Find("Ceilings");
        if (ceilings != null) ceilings.gameObject.SetActive(false);
        Shot("facility_top", new Vector3(0, 200, -4), Quaternion.Euler(90, 0, 0), true, 72f);
        Shot("facility_sw", new Vector3(-110, 100, -120), Quaternion.LookRotation(new Vector3(110, -100, 116)), false, 0f);
        if (ceilings != null) ceilings.gameObject.SetActive(true);
        Shot("facility_mid", new Vector3(14, 2.8f, 16), Quaternion.LookRotation(new Vector3(-1, 0.05f, -0.3f)), false, 0f);
        Shot("facility_mid_balcony", new Vector3(-22, 7.8f, 0), Quaternion.LookRotation(new Vector3(1, -0.15f, 0.1f)), false, 0f);
        Shot("facility_a", new Vector3(-34, 2.8f, 40), Quaternion.LookRotation(new Vector3(-1, 0.12f, 0.2f)), false, 0f);
        Shot("facility_b", new Vector3(-34, 2.8f, -48), Quaternion.LookRotation(new Vector3(-1, 0.12f, 0.3f)), false, 0f);
        Shot("facility_walk", new Vector3(-54, 7.8f, 0), Quaternion.LookRotation(new Vector3(1, 0, 0)), false, 0f);
        Shot("facility_upperb", new Vector3(14, 7.8f, -32), Quaternion.LookRotation(new Vector3(-1, 0, 0)), false, 0f);
        Shot("facility_amain", new Vector3(14, 2.8f, 56), Quaternion.LookRotation(new Vector3(-1, 0.05f, -0.2f)), false, 0f);
        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        EditorSceneManager.SaveScene(root.gameObject.scene);
    }
}
