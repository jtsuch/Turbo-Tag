using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static GreyboxKit;
using static KitLevel;
using static RoomLevel;

/// <summary>
/// Builds Cistern (Assets/Scenes/Levels/SearchDestroy/Cistern.unity): the second Search &amp; Destroy map,
/// an old waterworks on three floor levels (−5 underground, 0 ground, 5 upper walks). Plan approved Nov 13 2026.
/// What sets it apart from Facility:
///   - turned round: defenders north, attackers south, A west, B east;
///   - **A is a high site** (floor 5): attackers climb to it; a **hatch** in its floor opens from A Under, the low room
///     beneath it (a ledge grab out);
///   - **B is a pit** (plant zone at −5) with ledges at 0 along its south and north walls and a perch at 5 over the north
///     ledge (the defenders' one-way drop); B Tunnels enter it at pit level;
///   - **mid is a shaft** (−5 to 10): a ledge at 0 along its west wall (Mid Court → A Under: the way to A) over a floor at
///     −5 (Mid Stairs → B Tunnels: the way to B), and a balcony at 5 along its north wall on the defenders' **high walk**,
///     a through path: defense spawn ↔ mid's balcony ↔ A Heaven ↔ A site, and on east to B's perch.
/// Flow: offense spawn → A lobby (→ A main: a stair room climbing west, a walk north at 5, A's west door; → Mid Court) and
/// B lobby (→ B main: a yard, a hall north, B's south ledge; → Mid Stairs down to mid's floor). Defense spawn → A Back
/// (stairs up into A's north side), B Back (B's north ledge), the high walk (stairs up).
/// Rooms, walls, sealing, ceilings, balconies, lights and callouts come from the shared RoomLevel builder.
/// Review: Library/TurboTag/scratch/cistern_report.txt and cistern_*.png.
/// Menu: Turbo Tag/Levels/Search &amp; Destroy/Rebuild Cistern (editor job: BuildCistern).
/// </summary>
public static class CisternBuilder
{
    private const string ScenePath  = "Assets/Scenes/Levels/SearchDestroy/Cistern.unity";
    private const string RootName   = "Cistern";
    private const string ReportPath = "Library/TurboTag/scratch/cistern_report.txt";
    private const float D = -5f, G = 0f, L5 = 5f;
    private const float GridX = -72f, GridZ = -64f;   // Cell (i, j) spans x = −72 + 8i .. +8, z = −64 + 8j .. +8

    // Ties between rooms with the same ceiling put the wall in the later one: the big rooms first, the walks last
    private static readonly Room[] Layout =
    {
        new() { Name = "Mid",           Callout = "Mid",           I0 = 7,  I1 = 10, J0 = 6,  J1 = 10, Base = -1, Storeys = 3, Tile = "Floor-Lanes-8m" },
        new() { Name = "B Site",        Callout = "B Site",        I0 = 13, I1 = 16, J0 = 7,  J1 = 10, Base = -1, Storeys = 3, Tile = "Floor-Grate-8m" },
        new() { Name = "A Site",        Callout = "A Site",        I0 = 1,  I1 = 4,  J0 = 9,  J1 = 11, Base = 1,  Storeys = 2, Tile = "Floor-Tiles-8m",
                Hatches = new[] { new Vector2Int(4, 9) } },
        new() { Name = "Defense Spawn", Callout = "Defense Spawn", I0 = 7,  I1 = 10, J0 = 12, J1 = 14, Tile = "Floor-Inlay-8m" },
        new() { Name = "Offense Spawn", Callout = "Offense Spawn", I0 = 7,  I1 = 10, J0 = 0,  J1 = 2,  Tile = "Floor-Inlay-8m" },
        new() { Name = "A Lobby",       Callout = "A Lobby",       I0 = 3,  I1 = 6,  J0 = 0,  J1 = 3 },
        new() { Name = "B Lobby",       Callout = "B Lobby",       I0 = 11, I1 = 14, J0 = 0,  J1 = 3 },
        new() { Name = "A Main Stairs", Callout = "A Main",        I0 = 1,  I1 = 3,  J0 = 4,  J1 = 6 },
        new() { Name = "Mid Court",     Callout = "Mid Court",     I0 = 5,  I1 = 7,  J0 = 4,  J1 = 5 },
        new() { Name = "Mid Stairs",    Callout = "Mid Stairs",    I0 = 10, I1 = 12, J0 = 4,  J1 = 5,  Base = -1, Storeys = 2 },
        new() { Name = "B Yard",        Callout = "B Main",        I0 = 15, I1 = 17, J0 = 0,  J1 = 2,  Unlabelled = true },
        new() { Name = "B Hall",        Callout = "B Main",        I0 = 16, I1 = 17, J0 = 3,  J1 = 6 },
        new() { Name = "A Back",        Callout = "A Back",        I0 = 2,  I1 = 6,  J0 = 12, J1 = 13 },
        new() { Name = "A Under",       Callout = "A Under",       I0 = 4,  I1 = 6,  J0 = 9,  J1 = 10, Storeys = 1 },
        new() { Name = "B Back",        Callout = "B Back",        I0 = 11, I1 = 14, J0 = 11, J1 = 12, Storeys = 1 },
        new() { Name = "B Tunnels",     Callout = "B Tunnels",     I0 = 11, I1 = 12, J0 = 8,  J1 = 8,  Base = -1, Storeys = 1 },
        new() { Name = "A Main Walk",   Callout = "A Main",        I0 = 0,  I1 = 0,  J0 = 6,  J1 = 9,  Base = 1,  Storeys = 1, Unlabelled = true },
        new() { Name = "A Heaven",      Callout = "A Heaven",      I0 = 5,  I1 = 6,  J0 = 10, J1 = 10, Base = 1,  Storeys = 1 },
        new() { Name = "High Walk",     Callout = "High Walk",     I0 = 8,  I1 = 13, J0 = 11, J1 = 11, Base = 1,  Storeys = 1 },
    };

    private static readonly (int ai, int aj, int bi, int bj, char kind, int storey)[] Doors =
    {
        (7, 1, 6, 1, 'D', 0), (10, 1, 11, 1, 'D', 0),      // Offense spawn → A lobby, B lobby
        (3, 3, 3, 4, 'D', 0),                              // A lobby → A main stairs room
        (1, 6, 0, 6, 'O', 1),                              // Top of its stairs → A main walk (5)
        (0, 9, 1, 9, 'D', 1),                              // A main walk → A site's west side
        (5, 3, 5, 4, 'D', 0),                              // A lobby → Mid Court
        (7, 5, 7, 6, 'D', 0),                              // Mid Court → mid's ledge (0)
        (6, 9, 7, 9, 'D', 0),                              // Mid's ledge → A Under (the hatch up into A)
        (4, 10, 5, 10, 'D', 1),                            // A Heaven → A site's east side
        (6, 10, 7, 10, 'D', 1),                            // Mid's balcony (5) → A Heaven
        (14, 1, 15, 1, 'D', 0),                            // B lobby → B yard
        (17, 2, 17, 3, 'D', 0),                            // B yard → B hall
        (16, 6, 16, 7, 'D', 0),                            // B hall → B's south ledge
        (12, 3, 12, 4, 'D', 0),                            // B lobby → Mid Stairs' landing (0)
        (10, 5, 10, 6, 'D', -1),                           // Mid Stairs (−5) → mid's floor
        (10, 8, 11, 8, 'D', -1), (12, 8, 13, 8, 'D', -1),  // Mid's floor → B Tunnels → B's pit floor
        (7, 13, 6, 13, 'D', 0),                            // Defense spawn → A Back
        (2, 12, 2, 11, 'O', 1),                            // A Back's stairs → A site's north side (5)
        (10, 12, 11, 12, 'D', 0),                          // Defense spawn → B Back
        (14, 11, 14, 10, 'D', 0),                          // B Back → B's north ledge
        (8, 12, 8, 11, 'O', 1),                            // Defense spawn's stairs → the high walk (5)
        (9, 11, 9, 10, 'D', 1),                            // High walk → mid's balcony
        (13, 11, 13, 10, 'D', 1),                          // High walk → B's perch (a 10 m drop into the pit)
    };

    [MenuItem("Turbo Tag/Levels/Search & Destroy/Rebuild Cistern")]
    public static void Build()
    {
        Reset(20261114, "Assets/Environment/Generated/Cistern_Unused.asset", "Cistern", G);
        if (!System.IO.File.Exists(ScenePath)) AssetDatabase.CopyAsset("Assets/Scenes/Levels/SearchDestroy/Facility.unity", ScenePath);
        var scene = Begin(ScenePath, RootName);
        var geometry = root.parent;
        for (int i = geometry.childCount - 1; i >= 0; i--)
            if (geometry.GetChild(i) != root) Object.DestroyImmediate(geometry.GetChild(i).gameObject);

        Setup(Layout, Doors, GridX, GridZ);
        BuildFloors();
        BuildWalls();
        BuildCeilings();
        BuildLedgesAndStairs();
        DressRooms();
        BuildSpawnsAndSites();
        BuildCallouts();
        BuildLights();
        CheckAccess();
        Headroom("A main stairs", new Vector3(-54, G, -12), new Vector3(-64, L5, -12));
        Headroom("A Back stairs", new Vector3(-52, G, 42), new Vector3(-52, L5, 32));
        Headroom("Defense spawn stairs", new Vector3(-4, G, 42), new Vector3(-4, L5, 32));
        Headroom("Mid ledge stairs", new Vector3(2, D, -4), new Vector3(-8, G, -4));
        Headroom("Mid Stairs", new Vector3(14, D, -28), new Vector3(24, G, -28));
        Headroom("B pit stairs north", new Vector3(60, D, 6), new Vector3(60, G, 16));
        Headroom("B pit stairs south", new Vector3(52, D, 10), new Vector3(52, G, 0));
        report.Add($"cubbies: {cubbies.Count}");
        foreach (var c in cubbies) report.Add("  " + c);
        // Barriers after the access checks: they're pre-round walls, so the checks shouldn't see them (they can stand
        // right by a doorway); the overlap check still does
        BuildSpawnBarriers();
        CheckOverlaps();
        Bounds(Vector3.zero, 400f, -25f, 30f);
        PlaceSpawner(new Vector3(-12f, 0.1f, 0f), -20f);
        Atmosphere.SunnyDay(root, new Vector3(1f, 0f, 0.35f), new Bounds(new Vector3(0, 5, -4), new Vector3(150, 20, 130)), windOn: false);

        Finish(scene, ReportPath, "Cistern", RenderOverviews);
    }

    // ─── Ledges, balconies, stairs ────────────────────────────────────────────

    private static void BuildLedgesAndStairs()
    {
        var g = Group("Ledges");
        // A main: stairs climbing west up the stair room's north row to the walk at 5
        K(g, "Stairs-8x10m", new Vector3(-59, G - 1f, -12), -90f);
        // A Back: stairs south up into A site's north side
        K(g, "Stairs-8x10m", new Vector3(-52, G - 1f, 37), 180f);
        // Defense spawn: stairs south up to the high walk
        K(g, "Stairs-8x10m", new Vector3(-4, G - 1f, 37), 180f);

        // Mid: a ledge at 0 along the west wall (Mid Court → A Under), stairs up to it from the floor at −5, and the
        // high walk's balcony at 5 along the north wall (a ledge grab up from the ledge's north end)
        Balcony(g, -16, -8, -16, 16, G, 'E', -4f);
        K(g, "Stairs-8x10m", new Vector3(-3, D - 1f, -4), -90f);
        BalconyOver(g, -16, 16, 16, 24, L5, 'S', D);   // Its pillars stand on the floor at −5

        // Mid Stairs: a landing at 0 inside B lobby's door, stairs down west along the south row to the floor at −5
        // (the door into mid is in the north row, clear of the flight)
        Balcony(g, 24, 32, -32, -16, G, 'W', -28f);
        K(g, "Stairs-8x10m", new Vector3(19, D - 1f, -28), 90f);

        // B pit: ledges at 0 along the south and north walls, a perch at 5 over the north ledge (from the high walk),
        // stairs from the pit floor up to each ledge
        Balcony(g, 32, 64, -8, 0, G, 'N', 52f);
        Balcony(g, 32, 64, 16, 24, G, 'S', 60f);
        Balcony(g, 32, 48, 16, 24, L5, 'S');
        K(g, "Stairs-8x10m", new Vector3(60, D - 1f, 11), 0f);
        K(g, "Stairs-8x10m", new Vector3(52, D - 1f, 5), 180f);
    }

    // ─── Dressing ─────────────────────────────────────────────────────────────

    private static void DressRooms()
    {
        var g = Group("Dressing");
        // A site (tiled, floor 5): a hall of tall pillars, an L cover and crates round the plant zone
        foreach (var (x, z) in new[] { (-56f, 28f), (-40f, 28f), (-60f, 18f) })
            for (int k = 0; k < 2; k++) K(g, "Pillar-Round-2m", new Vector3(x, L5 + 4.5f * k, z), 0f, new Vector3(1.5f, 1.1f, 1.5f));
        K(g, "LCover-4m", new Vector3(-52, L5, 13), 0f);
        K(g, "CrateStack-2x2-3m", new Vector3(-44, L5, 21), 0f);
        Cubby("A hatch", new Vector3(-36, G, 12), "a ledge grab up from A Under into A site");

        // B pit (grated, floor −5): pumps and a tank (generators, a container) on the pit floor; barriers on the ledges
        K(g, "Generator-4x2p5m", new Vector3(42, D, 4), 0f);
        Container(g, 40, D, 12, 0f);
        Barrier(g, 40, G, -2.5f, 0f);
        Barrier(g, 46, G, 18.5f, 0f);

        // Mid (floor −5): a container and a generator on the floor, crates by the stairs
        Container(g, 8, D, 4, 90f);
        K(g, "Generator-4x2p5m", new Vector3(-2, D, 12), 90f);
        K(g, "CrateStack-2x2-3m", new Vector3(10, D, -6), 0f);

        // Lobbies: a stacked container pair hides which way the attackers go
        Container(g, -32, G, -48, 90f);
        Container(g, -32, G + ContainerHeight, -48, 90f);
        Container(g, 32, G, -48, 90f);
        Container(g, 32, G + ContainerHeight, -48, 90f);

        // B hall: a container in the middle (no straight look from the yard door to the pit)
        Container(g, 64, G, -24, 0f);
        // A main stair room: crates in the south half
        K(g, "CrateStack-2x2-3m", new Vector3(-46, G, -20), 0f);
        // The high walk: two staggered crate stacks
        K(g, "CrateStack-2x2-3m", new Vector3(12, L5, 29), 0f);
        K(g, "CrateStack-2x2-3m", new Vector3(26, L5, 26), 0f);
        // A Back: crates beside the stairs
        K(g, "CrateStack-2x2-3m", new Vector3(-36, G, 44), 0f);
    }

    // ─── Spawns and sites ─────────────────────────────────────────────────────

    private static void BuildSpawnsAndSites()
    {
        var g = Group("Spawn Points");
        // Defenders round their stairs (x −8..0, z 32..42); attackers in two rows
        Spawns(g, SpawnPoint.Side.Defend, new Vector3(0, G, 0),
               new Vector3(-12, G, 52), new Vector3(-12, G, 40), new Vector3(4, G, 52), new Vector3(12, G, 52), new Vector3(6, G, 40), new Vector3(13, G, 45));
        Spawns(g, SpawnPoint.Side.Attack, new Vector3(0, G, 0),
               new Vector3(-10, G, -58), new Vector3(0, G, -58), new Vector3(10, G, -58), new Vector3(-10, G, -48), new Vector3(0, G, -48), new Vector3(10, G, -48));
        var sites = Group("Bomb Sites");
        // A: the middle of the high site (its hatch just east of the zone); B: the pit floor between the stairs
        Site(sites, "A", new Vector3(-48, L5, 16), 16f);
        Site(sites, "B", new Vector3(40, D, 8), 16f);
    }

    // Pre-round walls (Justin, Nov 13: keep both teams well short of mid): defenders hold the spawn, A Back, B Back, both sites, A Heaven and
    // the high walk (its door onto mid's balcony shut; a lid over the A hatch), peeking into the mains and tunnels; attackers hold
    // their spawn and both lobbies, 2 m into the mains, Mid Court and Mid Stairs' landing
    private static void BuildSpawnBarriers()
    {
        var g = Group("Spawn Barriers");
        var d = SpawnPoint.Side.Defend; var a = SpawnPoint.Side.Attack;
        SpawnWall(g, d, -72, -64, L5, 10, 2.75f, 3.25f);          // A main walk
        SpawnWall(g, d, -38.1f, -33.9f, 4.8f, 4.95f, 9.9f, 14.1f); // A hatch (a lid)
        SpawnWall(g, d, -21.75f, -21.25f, L5, 10, 16, 24);        // A Heaven, short of mid's balcony
        SpawnWall(g, d, 0, 8, L5, 10, 25f, 25.5f);                 // The high walk's door onto mid's balcony (shut)
        SpawnWall(g, d, 27.25f, 27.75f, D, G, 0, 8);              // B tunnels
        SpawnWall(g, d, 56, 72, G, 10, -12.75f, -12.25f);         // B hall
        SpawnWall(g, a, -64, -40, G, 10, -29.75f, -29.25f);       // A main stairs room, 2 m in
        SpawnWall(g, a, -32, -8, G, 10, -29.75f, -29.25f);        // Mid Court, 2 m in
        SpawnWall(g, a, 50.75f, 51.25f, G, 10, -64, -40);         // B yard, 2 m in
        // Mid Stairs: only the 2 m inside the door on its landing (an L: across the landing, and along the top of the
        // flight down so nobody steps off onto it)
        SpawnWall(g, a, 24.25f, 32, G, 10, -29.75f, -29.25f);
        SpawnWall(g, a, 24.25f, 24.75f, G, 10, -32, -29.75f);
    }

    private static void RenderOverviews()
    {
        var ceilings = root.Find("Ceilings");
        if (ceilings != null) ceilings.gameObject.SetActive(false);
        Shot("cistern_top", new Vector3(0, 200, -4), Quaternion.Euler(90, 0, 0), true, 66f);
        Shot("cistern_sw", new Vector3(-100, 100, -110), Quaternion.LookRotation(new Vector3(100, -100, 106)), false, 0f);
        if (ceilings != null) ceilings.gameObject.SetActive(true);
        Shot("cistern_mid", new Vector3(-12, 2.8f, -12), Quaternion.LookRotation(new Vector3(1, -0.2f, 0.8f)), false, 0f);
        Shot("cistern_pit", new Vector3(60, 2.8f, -6), Quaternion.LookRotation(new Vector3(-0.6f, -0.35f, 1)), false, 0f);
        Shot("cistern_a", new Vector3(-62, 7.8f, 12), Quaternion.LookRotation(new Vector3(1, -0.05f, 0.25f)), false, 0f);
        Shot("cistern_under", new Vector3(-18, 2.8f, 12), Quaternion.LookRotation(new Vector3(-1, 0.25f, 0)), false, 0f);
        Shot("cistern_walk", new Vector3(-4, 7.8f, 28), Quaternion.LookRotation(new Vector3(1, -0.05f, 0)), false, 0f);
        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        EditorSceneManager.SaveScene(root.gameObject.scene);
    }
}
