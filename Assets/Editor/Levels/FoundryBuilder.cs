using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using static GreyboxKit;
using static KitLevel;

/// <summary>
/// Builds the Foundry level (Assets/Scenes/Levels/Foundry.unity) from Justin's modular kit (Assets/Environment/Prefabs),
/// on the kit standard (Docs/KitStandards.md): 5 m storeys (floors at 0, 5 and 10, a service level at −10 under the
/// yard), walls standing just outside the floor they enclose (pivot on their inner face) with 1 × 1 posts on convex
/// corners, ramps and stairs rising one storey toward their +Z.
///
/// Same principles as Fracture: a flat-reading central yard you can see across, with the height in distinct stacks
/// (the factory, the loading dock, the east deck and warehouse, the Stack tower) and pockets everywhere (the factory floor
/// and mezzanine, the dock's low room, the undercroft under the east deck, the warehouse hall, the service level).
///   Yard (0): 48 × 48, the Stack (round tower, 10) in the middle with a lift; two hole tiles drop into the service
///     level; a ramp trench down into it.
///   North: the factory (9 m walls, arched doors, windows, a mezzanine at 5 up stairs, a skylight in the roof at 10),
///     the loading dock (5) up a long ramp from the yard and a ramp on up to the factory roof; a catwalk at 10 from the
///     roof to the Stack.
///   East: a deck (5) with an undercroft under it flowing into the warehouse hall (mezzanine 5, roof 10).
///   West: over the void, a ring island with a fan blowing up through its hole (a ride up to the sky deck at 10 by
///     the factory roof), bridged from the yard; hex stepping stones south to the docks.
///   South: the docks (0) open to the void, two mirrored pits in the lane, container stacks, arch gates, a pier
///     on a catwalk with a teleport pad to the sky deck. West: a second island (a hall with a room on top, a hollow
///     tower with a roof hatch and a zip line down to a mini island by the floating plates).
/// All of it floats: a skirt round the main island's edge, dirt underneath, propellers holding it up (BuildIsland).
///
/// Every build checks for overlapping pieces (collider against collider) and writes them to the report.
/// Re-running rebuilds Geometry/Foundry; review output goes to Library/TurboTag/scratch/foundry_report.txt and
/// foundry_*.png. Menu: Turbo Tag/Levels/Rebuild Foundry (editor job: BuildFoundry).
/// </summary>
public static class FoundryBuilder
{
    private const string ScenePath  = "Assets/Scenes/Levels/Foundry.unity";
    private const string RootName   = "Foundry";
    private const string ReportPath = "Library/TurboTag/scratch/foundry_report.txt";

    private const float L0 = 0f, L5 = 5f, L10 = 10f, Service = -5f;


    [MenuItem("Turbo Tag/Levels/Rebuild Foundry")]
    public static void Build()
    {
        KitLevel.Reset(20261115, "Assets/Environment/Generated/Foundry_Island.asset", "Foundry Island", L0, Service);
        if (!System.IO.File.Exists(ScenePath)) AssetDatabase.CopyAsset("Assets/Scenes/Levels/Fracture.unity", ScenePath);
        var scene = Begin(ScenePath, RootName);
        var geometry = root.parent;
        for (int i = geometry.childCount - 1; i >= 0; i--)
            if (geometry.GetChild(i) != root) Object.DestroyImmediate(geometry.GetChild(i).gameObject);

        BuildYard();
        BuildServiceLevel();
        BuildFactory();
        BuildDock();
        BuildEast();
        BuildWest();
        BuildDocks();
        BuildWestIsland();
        BuildMiniIsland();
        BuildIsland();
        CheckAccess();
        report.Add($"cubbies: {cubbies.Count}");
        foreach (var c in cubbies) report.Add("  " + c);
        CheckOverlaps();
        // Low enough that a fall off the edge passes the skirt and the dirt (both climbable) before it counts
        Bounds(Vector3.zero, 360f, -110f, 60f);
        Spawns(SpawnSpots, Vector3.zero);
        PlaceSpawner(new Vector3(-12, 0.1f, 12), -100f);
        // A sunny day: sky, bright soft lighting so rooms and undersides read, haze, a light wind from the west-south-west
        Atmosphere.SunnyDay(root, new Vector3(1f, 0f, 0.35f), new Bounds(new Vector3(8, 12, -8), new Vector3(150, 30, 160)));

        Finish(scene, ReportPath, "Foundry", RenderOverviews);
    }

    // ─── The west island: a hall with a room on top, and a hollow tower to climb ──────────────────────────

    // x −112..−64, z −48..16 (48 × 64, smaller than the main island), past the hex stepping stones. Its own skirt,
    // dirt and propellers come from BuildIsland (every separate piece of ground floats on its own).
    private const float WBX0 = -112, WBX1 = -96, WBZ0 = -48, WBZ1 = -16, WBUpper = -32; // The hall (its upper room z −32..−16)
    private const float TX0 = -80, TX1 = -64, TZ0 = -8, TZ1 = 8, TowerTop = 19f;          // The hollow tower (roof top)

    private static void BuildWestIsland()
    {
        var g = Group("West Island");
        // Ground: big plain slabs (two 32 × 32 west, two 32 × 16 east)
        Slab(g, "Floor-32x32m", -96, -32, L0);
        Slab(g, "Floor-32x32m", -96, 0, L0);
        Slab(g, "Floor-32x16m", -72, -32, L0, 90f);
        Slab(g, "Floor-32x16m", -72, 0, L0, 90f);

        // ── The hall (0, roof 5) with a room on its north half (5, roof 10) ──
        Slab(g, "Floor-32x16m", -104, -32, L5, 90f);                       // The hall's roof: the room's floor
        K(g, "Platform-16x16m", new Vector3(-104, L10 - 1, -24), 0f);       // The room's roof
        for (float x = -108; x <= -100; x += 8)
        {
            for (float z = -44; z <= -20; z += 8) Ceiling(g, x, L5 - 1, z);
            for (float z = -28; z <= -20; z += 8) Ceiling(g, x, L10 - 1, z);
        }
        // West (the map's edge) and south (the map's edge): windows; two storeys where the room is
        foreach (float z in new[] { -44f, -36f }) Wall(g, "WallWindow-8m", WBX0, 0, z, W);
        Facade(g, WBX0, -28, W, 'W');
        Facade(g, WBX0, -20, W, 'M');
        foreach (float x in new[] { -108f, -100f }) Wall(g, "WallWindow-8m", x, 0, WBZ0, S);
        // East: a door into the hall, the ramp up onto its roof, then the two-storey part
        Wall(g, "Archway-8m", WBX1, 0, -44, E);
        Facade(g, WBX1, -28, E, 'N');
        Cubby("Wall niche", new Vector3(-94, 0, -28), "standing (3.5 m), facing the east yard");
        Facade(g, WBX1, -20, E, 'P');
        // North: a door into the hall (under the room) and a window
        Facade(g, -108, WBZ1, N, 'D');
        Facade(g, -100, WBZ1, N, 'W');
        Post(g, WBX0, 0, WBZ0, W, S, 4f);
        Post(g, WBX1, 0, WBZ0, E, S, 4f);
        Post(g, WBX0, 0, WBZ1, W, N, 9f);
        Post(g, WBX1, 0, WBZ1, E, N, 9f);
        // The room's south face, standing on the hall's roof: a door out onto it, and a panel
        Wall(g, "Archway-8m", -108, L5, WBUpper, S);
        Wall(g, ClimbWall, -100, L5, WBUpper, S);                                         // Climbs onto the room's roof
        Post(g, WBX0, 4, WBUpper, W, S, 5f);
        Post(g, WBX1, L5, WBUpper, E, S, 4f);     // From 5: the ramp's top fills 4..5 below it
        Tunnel("West Hall Door E", new Vector3(WBX1 + 2, 0, -44), new Vector3(WBX1 - 2, 0, -44), "run");
        Tunnel("West Hall Door N", new Vector3(-108, 0, WBZ1 + 2), new Vector3(-108, 0, WBZ1 - 2), "run");
        Tunnel("West Room Door", new Vector3(-108, L5, WBUpper - 2), new Vector3(-108, L5, WBUpper + 2), "run");
        // Up onto the hall's roof: a ramp rising west straight onto it; the hard way on up to the room's roof: a container
        K(g, "Ramp-Hollow-8x10m", new Vector3(-91, L0, -36), 90f);   // The room under its top opens into the hall
        InsideTunnel("Under the hall ramp", new Vector3(-97.5f, 0, -36), new Vector3(-94f, 0, -36));
        Cubby("Under the hall ramp", new Vector3(-94, 0, -36), "standing near its mouth (3.3–4.3 m), opens into the hall");
        Container(g, -101, L5, -37, 90f);          // Against the room, clear of its door and the ramp's top: 8.5 → its roof (10)
        Lamp(g, new Vector3(-104, 3f, -36), 16f);
        Lamp(g, new Vector3(-104, 8f, -24), 14f);

        // ── The hollow tower: 16 × 16, tall walls (two rows, 0..18) with arched doors west and north and a window in each
        // side's upper row; a roof (19) with a hatch you climb out through. Inside, a staircase of 3 m steps (plain jumps)
        // up the south and east walls to just under the hatch (a ledge grab out). From the roof, a zip line down to the
        // mini island by the floating plates ──
        Facade(g, TX0, -4, W, 'S');            Facade(g, TX0, 4, W, 'D');            // West: a door
        Facade(g, -76, TZ1, N, 'D');           Facade(g, -68, TZ1, N, 'S');          // North: a door
        Facade(g, -76, TZ0, S, 'S');
        // A high niche on the south face (alcove 3.6..6.2: a ledge grab up), then a short panel to the second row
        Wall(g, "Wall-Niche-High-8m", -68, 0, TZ0, S);
        Wall(g, "Wall-Upper-8m", -68, 7f, TZ0, S, 0.5f);
        Cubby("High wall niche", new Vector3(-68, 3.6f, TZ0 - 1.5f), "crouch (2.6 m), a ledge grab up from the ground");
        Facade(g, TX1, -4, E, 'S');            Facade(g, TX1, 4, E, 'S');            // The map's edge
        Facade(g, TX0, -4, W, 'S', baseY: 9f); Facade(g, TX0, 4, W, 'W', baseY: 9f);
        Facade(g, -76, TZ1, N, 'S', baseY: 9f); Facade(g, -68, TZ1, N, 'W', baseY: 9f);
        Facade(g, -76, TZ0, S, 'W', baseY: 9f); Facade(g, -68, TZ0, S, 'S', baseY: 9f);
        Facade(g, TX1, -4, E, 'W', baseY: 9f); Facade(g, TX1, 4, E, 'S', baseY: 9f);
        foreach (float y in new[] { 0f, 9f })
        {
            Post(g, TX0, y, TZ0, W, S, 9f); Post(g, TX1, y, TZ0, E, S, 9f);
            Post(g, TX0, y, TZ1, W, N, 9f); Post(g, TX1, y, TZ1, E, N, 9f);
        }
        Tunnel("Tower Door W", new Vector3(TX0 - 2, 0, 4), new Vector3(TX0 + 2, 0, 4), "run");
        Tunnel("Tower Door N", new Vector3(-76, 0, TZ1 + 2), new Vector3(-76, 0, TZ1 - 2), "run");
        // The roof (slab 18..19): the hatch tile over the staircase's top, plain tiles elsewhere, light ceilings inside
        foreach (float x in new[] { -76f, -68f })
            foreach (float z in new[] { -4f, 4f })
            {
                bool hatch = x == -68f && z == -4f;
                Tile(g, hatch ? "Floor-Hole-8m" : "Floor-Tiles-8m", x, z, TowerTop);
                if (!hatch) Ceiling(g, x, TowerTop - 1, z);
            }
        // The staircase: columns of blocks cut to 3 m (3, 6, 9, 12, 15), the last one under the hatch (19: a ledge grab)
        var steps = new[] { new Vector2(-78, -6), new Vector2(-74, -6), new Vector2(-70, -6), new Vector2(-66, -6), new Vector2(-66, -2) };
        for (int i = 0; i < steps.Length; i++)
            for (int h = 0; h <= i; h++)
                K(g, "Block-4x4x4m", new Vector3(steps[i].x, h * 3f, steps[i].y), 0f, new Vector3(1f, 0.75f, 1f));
        // A zip line from the roof's north edge down to the mini island by the plates (MiniIsland)
        ZipLineBetween(g, new Vector3(-70, TowerTop, 5), new Vector3(-64, L0, 42));
        Lamp(g, new Vector3(-72, 12f, 0), 20f);

        // Cover, evenly: two containers mirrored on the north yard, two barriers in a row on the east yard
        // Two sheds facing each other across the north yard (their doors on local −Z), a scaffold to climb on the east yard
        // ×1.5 (6 × 6 × 6): the model's door is only 2.6 m tall; scaled it's 3.9, a standing penguin walks in
        K(g, "Shed-4x4m", new Vector3(-100, L0, 10), 0f, Vector3.one * ShedScale);
        K(g, "Shed-4x4m", new Vector3(-100, L0, -10), 180f, Vector3.one * ShedScale);
        Tunnel("Shed N door", new Vector3(-100, 0, 4), new Vector3(-100, 0, 10), "run");
        Tunnel("Shed S door", new Vector3(-100, 0, -4), new Vector3(-100, 0, -10), "run");
        K(g, "Scaffold-4x4x5m", new Vector3(-74, L0, -34), 0f);
        Lamp(g, new Vector3(-88, 8, -24), 22f);

        // ── The way over: a catwalk straight on from the docks' lane (z −44, rim to rim), and a third hex stone ──
        const float From = -63f, To = -41f;
        int n = 3;
        float piece = (To - From) / n;
        for (int i = 0; i < n; i++)
        {
            var walk = K(g, "Catwalk-8x2m", new Vector3(From + piece * (i + 0.5f), L0 - 1, -44), 0f, new Vector3(piece / 8f, 1f, 1f));
            if (i == 1) PropsUnder(g, walk, 0.8f, Vector2.zero);
        }
        PropsUnder(g, K(g, "Platform-Hex-8m", new Vector3(-57, L0 - 1, -14), 0f), 1.6f, Vector2.zero);
        gaps.Add(("Hex 2 to hex 3", new Vector3(-46, 0, -15), new Vector3(-53, 0, -14)));
        gaps.Add(("Hex 3 to the west island", new Vector3(-61, 0, -14), new Vector3(-63, 0, -14)));
    }

    // ─── The mini island by the floating plates: where the tower's zip line lands ──────────────────────

    // x −72..−56, z 32..48 (16 × 16), west of the plates on the beam route; its own skirt, dirt and propeller
    private static void BuildMiniIsland()
    {
        var g = Group("Mini Island");
        Slab(g, "Platform-16x16m", -64, 40, L0);
        // A bridge over to the first plate (rim to plate)
        const float From = -55f, To = RingX - 2f;   // The plate's west edge
        K(g, "Bridge-8x4m", new Vector3((From + To) / 2f, L0 - 1, 34.2f), 0f, new Vector3((To - From) / 8f, 1f, 1f));
        // A little cover either side of where the zip line drops you
        Barrier(g, -68, L0, 36, 90f);
        Barrier(g, -60, L0, 36, 90f);
        Lamp(g, new Vector3(-64, 6, 40), 14f);
    }



    // ─── Yard (0) and the Stack ───────────────────────────────────────────────

    private static void BuildYard()
    {
        var g = Group("Yard");
        // A pinwheel of big plain slabs round the Stack's rimmed dais (16 × 16): north, east and west 32 × 16; the south
        // side is the service yard
        Slab(g, "Floor-32x16m", -8, 16, L0, 0f);      // North: x −24..8, z 8..24
        Slab(g, "Floor-32x16m", 16, 8, L0, 90f);      // East: x 8..24, z −8..24
        Slab(g, "Floor-32x16m", -16, -8, L0, 90f);    // West: x −24..−8, z −24..8
        Slab(g, "Platform-16x16m", 0, 0, L0);         // The Stack's dais
        // The service yard (x −8..24, z −24..−8): one block of tiles, all one kind; its east end is the trench down
        for (float x = -4; x <= 12; x += 8) for (float z = -20; z <= -12; z += 8) Tile(g, "Floor-Tiles-8m", x, z, L0);
        // In front of the factory and dock: one straight lane east–west (z 24..32); beside the dock a rimmed loading bay
        for (float x = -36; x <= 20; x += 8) Tile(g, "Floor-Lanes-8m", x, 28, L0, 0f);      // The stripe runs along x
        Slab(g, "Platform-16x16m", 16, 40, L0);
        // The loading bay: a forklift and a row of pallet stacks, low to high
        K(g, "Forklift-3m", new Vector3(18, L0, 44), 180f);
        foreach (var (id, x) in new[] { ("PalletStack-Low-0p6m", 12f), ("PalletStack-Mid-1p2m", 14f), ("PalletStack-High-1p8m", 16f) })
            K(g, id, new Vector3(x, L0, 46), 90f);   // Clear of the dock room's slide gap

        // The Stack: a round tower (10) in the middle, a lift beside it (a 1 m hop on, up to the top)
        K(g, "Tower-Round-8m", new Vector3(0, L0, 0), 0f);
        KitMover(g, "Stack Lift", new Vector3(-6.5f, L0, 0), new Vector3(-6.5f, L10 - 1f, 0), 4f);
        Lamp(g, new Vector3(0, L10 + 6, 0), 26f);

        // Cover, laid out evenly: four barriers in a ring round the Stack, a container on each side (a stacked pair to
        // climb on the west)
        // Cover, laid out evenly: four L-covers in a pinwheel round the Stack (each turned a quarter from the last), a
        // stacked pair of containers west to climb, a container open at both ends east to run through
        for (int i = 0; i < 4; i++)
        {
            float a = i * 90f * Mathf.Deg2Rad;
            K(g, "LCover-4m", new Vector3(Mathf.Sin(a) * 12f, L0, Mathf.Cos(a) * 12f), i * 90f);
        }
        Container(g, -16, L0, -4, 90f);
        Container(g, -16, L0 + ContainerHeight, -4, 90f);
        // Two crawl platforms in the west corners, mirrored: a hop up (2.2) on top, a crawl space underneath (along z)
        foreach (float z in new[] { 16f, -16f })
        {
            K(g, "Platform-Crawl-8x8m", new Vector3(-16, L0, z), 0f);
            // A support post stands in the middle: each entrance is checked up to it (you crawl round it inside)
            Tunnel($"Crawl platform {z:0} south way in", new Vector3(-16.1f, 0, z - 5), new Vector3(-16.1f, 0, z - 1.2f), "crawl");
            Tunnel($"Crawl platform {z:0} north way in", new Vector3(-16.1f, 0, z + 5), new Vector3(-16.1f, 0, z + 1.2f), "crawl");
            Cubby("Crawl platform", new Vector3(-16, 0, z), "prone (1.7 m), open at both ends, a post in the middle");
        }
        K(g, "CargoContainer-Tunnel-8m", new Vector3(16, L0, 4), 90f);
        InsideTunnel("Yard container", new Vector3(16, 0, -1), new Vector3(16, 0, 9));
        Lamp(g, new Vector3(-12, 8, -12), 24f);
        Lamp(g, new Vector3(14, 8, 14), 24f);
    }

    // ─── Service level (−5) under the service yard's east half ────────────────

    private static void BuildServiceLevel()
    {
        var g = Group("Service Level");
        // One storey down (x 8..24, z −24..−8): grated floor, a pipe run all round (pipes may chain), posts on the corners
        for (float x = 12; x <= 20; x += 8) for (float z = -20; z <= -12; z += 8) Tile(g, "Floor-Grate-8m", x, z, Service);
        foreach (float x in new[] { 12f, 20f })
        {
            Wall(g, "Wall-Pipes-8m", x, Service, -24, S);
            Wall(g, "Wall-Pipes-8m", x, Service, -8, N);
        }
        foreach (float z in new[] { -20f, -12f })
        {
            Wall(g, "Wall-Pipes-8m", 8, Service, z, W);
            Wall(g, "Wall-Pipes-8m", 24, Service, z, E);  // Under the east block's floor; the deck's face stands above it
        }
        Post(g, 8, Service, -24, W, S, 4f);  Post(g, 24, Service, -24, E, S, 4f);
        Post(g, 8, Service, -8, W, N, 4f);   Post(g, 24, Service, -8, E, N, 4f);
        // Its ceiling: the service yard's tiles over its west half; the east half is the open trench
        foreach (float z in new[] { -20f, -12f }) Ceiling(g, 12, L0 - 1, z);
        // Out: a ramp up the trench rising south, straight onto the docks (−5 → 0)
        K(g, "Ramp-8x10m", new Vector3(20, Service - 1, -19), 180f);
        Lamp(g, new Vector3(14, Service + 3.5f, -14), 14f);
    }

    // ─── Factory (walls 9, mezzanine 5, roof 10) ──────────────────────────────

    // Footprint x −40..−8, z 32..48 (the lane z 24..32 runs in front of it)
    private const float FX0 = -40, FX1 = -8, FZ0 = 32, FZ1 = 48;

    private static void BuildFactory()
    {
        var g = Group("Factory");
        // Indoors, one pattern for the whole room
        for (float x = FX0 + 4; x < FX1; x += 8) for (float z = FZ0 + 4; z < FZ1; z += 8) Tile(g, "Floor-Tiles-8m", x, z, L0);

        // South face (to the yard): two arched doors, two windows
        Facade(g, -36, FZ0, S, 'Q');   // A crouch squeeze through the wall (3 m deep) from the lane
        Tunnel("Factory squeeze", new Vector3(-36, 0, FZ0 - 4), new Vector3(-36, 0, FZ0 + 2), "crouch");
        Cubby("Factory squeeze", new Vector3(-36, 0, FZ0 - 1.5f), "crouch (2.6 m), a way in as well as a hide");
        Facade(g, -28, FZ0, S, 'D');
        Facade(g, -20, FZ0, S, 'N');   // A niche facing the lane (not a window: its sill would stick into the stairs inside)
        Cubby("Wall niche", new Vector3(-20, 0, FZ0 - 1.5f), "standing (3.5 m), facing the lane");
        Facade(g, -12, FZ0, S, 'D');
        // North face (the map's edge): never flat: windows, a pipe run, mixed panels; west face: a door and a window
        Facade(g, -36, FZ1, N, 'P');
        Facade(g, -28, FZ1, N, 'W');
        Facade(g, -20, FZ1, N, 'M');
        Facade(g, -12, FZ1, N, 'W');
        Facade(g, FX0, 36, W, 'D');
        Facade(g, FX0, 44, W, 'W');
        // East face (inside the dock's low room): the dock deck's slab crosses it at 4..5, so its upper panels stand on
        // the deck; none above the window, where the ramp from the deck comes up onto the roof
        Facade(g, FX1, 36, E, 'D', 5f);
        Facade(g, FX1, 44, E, 'W', 5f, false);
        Post(g, FX0, 0, FZ0, W, S, 9f);
        Post(g, FX0, 0, FZ1, W, N, 9f);
        Tunnel("Factory Door S1", new Vector3(-28, 0, FZ0 - 2), new Vector3(-28, 0, FZ0 + 2), "run");
        Tunnel("Factory Door S2", new Vector3(-12, 0, FZ0 - 2), new Vector3(-12, 0, FZ0 + 2), "run");
        Tunnel("Factory Door W", new Vector3(FX0 - 2, 0, 36), new Vector3(FX0 + 2, 0, 36), "run");
        Tunnel("Factory Door E", new Vector3(FX1 + 2, 0, 36), new Vector3(FX1 - 2, 0, 36), "run");

        // Mezzanine (5: 4 m under it and 4 m over it) along the north wall, on a line of beams and round pillars, up a
        // flight of stairs rising north straight onto it
        for (float x = -36; x <= -12; x += 8) { K(g, "Platform-8x4m", new Vector3(x, L5 - 1, 46), 0f); Ceiling(g, x, L5 - 1, 46, 0f, 1f, 0.5f); }
        for (float x = -36; x <= -12; x += 8) K(g, "Beam-8m", new Vector3(x, L5 - 2, 45), 0f);
        foreach (float x in new[] { -32f, -24f, -16f }) K(g, "Pillar-Round-2m", new Vector3(x, 0, 45), 0f, new Vector3(1f, 0.75f, 1f));
        K(g, "Stairs-8x10m", new Vector3(-20, L0 - 1, 39), 0f);
        foreach (float x in new[] { -38f, -30f }) K(g, "Railing-2m", new Vector3(x, L5, 44.3f), 0f);
        K(g, "Generator-4x2p5m", new Vector3(-34, 0, 40), 90f);
        Barrier(g, -29, 0, 38, 0f);
        Lamp(g, new Vector3(-24, 7.5f, 40), 18f);

        // Roof (10): one plain slab; a vent at each end
        // Cells: a hatch over the mezzanine (a drop in, the container-free climb out), coffers either side of it
        RoofCells(g, FX0, FX1, FZ0, FZ1, L10, new[] { new Vector2(-28, 44) }, System.Array.Empty<Vector2>());
        Prop(g, "Vent", new Vector3(-32, L10, 40), 0f);
        Prop(g, "Vent", new Vector3(-16, L10, 40), 0f);
    }


    // ─── Loading dock (5) between the factory and the yard's north-east ───────

    private static void BuildDock()
    {
        var g = Group("Loading Dock");
        // A rimmed 16 × 16 deck at 5 (x −8..8, z 32..48) over a low room, in through the factory's east door
        K(g, "Platform-16x16m", new Vector3(0, L5 - 1, 40), 0f);
        for (float x = -4; x <= 4; x += 8) for (float z = 36; z <= 44; z += 8) { Tile(g, "Floor-Inlay-8m", x, z, L0); Ceiling(g, x, L5 - 1, z); }
        // The room's faces: south (the ramp up closes its west half), east, north (the map's edge)
        Wall(g, ClimbWall, 4, 0, FZ0, S);                                                 // Climbs onto the deck
        foreach (float z in new[] { 36f, 44f }) Wall(g, z == 44f ? ClimbWall : "Wall-SlideGap-8m", 8, 0, z, E);
        Tunnel("Dock room slide gap", new Vector3(6, 0, 36), new Vector3(10, 0, 36), "crawl");   // Its second way out
        foreach (float x in new[] { -4f, 4f }) Wall(g, LowerWall(), x, 0, FZ1, N);
        Post(g, 8, 0, FZ0, E, S, 4f);
        Post(g, 8, 0, FZ1, E, N, 4f);
        // Up from the yard: a long ramp rising north onto the deck's south edge
        K(g, "Ramp-16x8m", new Vector3(-4, L0 - 1, 24), 0f);
        // On up to the factory roof (5 → 10): a ramp along the deck's north half rising west, onto the roof's edge
        K(g, "Ramp-8x10m", new Vector3(-3, L5 - 1, 44), -90f);
        // Cover on the deck (clear of both ramps' ends); the hard way up from the yard: a container in front of the room
        // (0 → 3.5 → the wall top 4 → the deck)
        Container(g, 5.5f, L5, 36, 90f);
        Container(g, 4, L0, 28.5f, 0f);

        // Catwalk at 10 from the factory roof's south-east corner, over the dock's front edge, round a corner piece (the
        // straight pieces have rails on both sides) and south to the Stack: one jump onto its top
        K(g, "Catwalk-8x2m", new Vector3(-4, L10 - 1, 33), 0f);
        K(g, "Catwalk-Corner-4x4m", new Vector3(3, L10 - 1, 33), 180f);   // Open to the west and the south
        for (float z = 26; z >= 10; z -= 8) K(g, "Catwalk-8x2m", new Vector3(3, L10 - 1, z), 90f);
        foreach (var p in new[] { new Vector3(3, 0, 24), new Vector3(3, 0, 14) })
        {
            K(g, "Pillar-Round-2m", p, 0f, new Vector3(1f, 1.125f, 1f));
            K(g, "Pillar-Round-2m", p + Vector3.up * 4.5f, 0f, new Vector3(1f, 1.125f, 1f));
        }
        gaps.Add(("Catwalk end to the Stack top", new Vector3(3, L10, 6.2f), new Vector3(3, L10, 2.7f)));
        Lamp(g, new Vector3(0, L10 + 5, 40), 20f);
    }

    // ─── East: a raised deck (5) and the warehouse (roof 10) ──────────────────

    // The deck (5): x 24..40, z −40..24, open underneath (4 m: the undercroft). The warehouse: x 40..56, same z, an
    // indoor hall 9 m high under its roof (10), with a mezzanine (5) along its east side
    private const float HX0 = 40, HX1 = 56, HZ0 = -40, HZ1 = 24;

    private static void BuildEast()
    {
        var g = Group("East Deck and Warehouse");
        // Deck and roof: two plain slabs each; indoors underneath (undercroft and hall, one room): two big tiled slabs
        foreach (float z in new[] { -24f, 8f }) Slab(g, "Floor-32x16m", 32, z, L5, 90f);
        // Warehouse roof: a plain slab over its south half; cells over the north half, with a hatch over the mezzanine's
        // container (the hard way up and a drop in) and coffers over the mezzanine either side
        Slab(g, "Floor-32x16m", 48, -24, L10, 90f);
        RoofCells(g, HX0, HX1, -8, HZ1, L10, new[] { new Vector2(52, -4) }, System.Array.Empty<Vector2>());
        Slab(g, "Floor-32x32m-Tiles", 40, -24, L0);
        Slab(g, "Floor-32x32m-Tiles", 40, 8, L0);
        for (float z = -36; z <= 20; z += 8)
        {
            foreach (float x in new[] { 28f, 36f }) Ceiling(g, x, L5 - 1, z);
            foreach (float x in new[] { 44f, 52f }) if (z < -8) Ceiling(g, x, L10 - 1, z);
        }

        // The deck's west face (to the yard, 0..4 under its slab): mixed panels, two archways into the undercroft, none
        // where the ramp up from the yard meets the deck
        for (float z = -36; z <= 20; z += 8)
        {
            if (z == 20) continue;
            // Beside the service trench (z −24..−8) there's no floor outside the deck: the panel stands just inside its
            // edge instead (x 24..25), above the service room's east wall
            if (z == -20 || z == -12) { Wall(g, LowerWall(), 25, 0, z, W); continue; }
            Wall(g, z == -4 || z == 12 ? "Archway-8m" : z == -28 || z == 4 ? ClimbWall : z == -36 ? "Wall-Niche-8m" : LowerWall(), 24, 0, z, W);
        }
        Cubby("Wall niche", new Vector3(22, 0, -36), "standing (3.5 m), facing the docks");
        Post(g, 24, 0, HZ0, W, S, 4f);
        Post(g, 24, 0, HZ1, W, N, 4f);
        Tunnel("Undercroft Arch S", new Vector3(22, 0, -4), new Vector3(30, 0, -4), "run");
        Tunnel("Undercroft Arch N", new Vector3(22, 0, 12), new Vector3(30, 0, 12), "run");
        Tunnel("Undercroft to Hall", new Vector3(30, 0, 4), new Vector3(44, 0, 4), "run");
        // The deck's ends (the map's edge)
        foreach (float x in new[] { 28f, 36f }) { Wall(g, LowerWall(), x, 0, HZ0, S); Wall(g, LowerWall(), x, 0, HZ1, N); }
        // The warehouse's face above the deck (5..9), none where the ramp from the deck meets the roof; posts at its ends
        for (float z = -36; z <= 20; z += 8)
            if (z != -28) Wall(g, z == -20 || z == 12 ? ClimbWall : z == 4 ? "Archway-8m" : LowerWall(), HX0, L5, z, W);   // Climbs onto the roof; a door onto the rafter
        // The rafter: a balance beam at 5 from the deck's door across the hall to the mezzanine (12 m)
        K(g, "Rafter-8m", new Vector3(46, L5 - 4.4f, 4), 0f, new Vector3(1.5f, 1f, 1f));
        Tunnel("Deck door to the rafter", new Vector3(37, L5, 4), new Vector3(41.5f, L5, 4), "run");
        Post(g, HX0, 4, HZ0, W, S, 5f);
        Post(g, HX0, 4, HZ1, W, N, 5f);
        // The warehouse's outer faces (the map's edge on three sides): never flat: windows, pipes, mixed panels
        char[] faces = { 'W', 'P', 'M', 'W', 'P', 'W', 'M', 'W' };
        int f = 0;
        foreach (float x in new[] { 44f, 52f }) { Facade(g, x, HZ0, S, faces[f++ % faces.Length]); Facade(g, x, HZ1, N, faces[f++ % faces.Length]); }
        for (float z = -36; z <= 20; z += 8) Facade(g, HX1, z, E, faces[f++ % faces.Length]);
        Post(g, HX1, 0, HZ0, E, S, 9f);
        Post(g, HX1, 0, HZ1, E, N, 9f);

        // Inside the hall: a mezzanine (5) along the east wall on a line of beams and round pillars, up stairs from the
        // hall floor (rising east, straight onto it); partitions and containers to hide behind
        for (float z = -36; z <= 20; z += 8) { K(g, "Platform-8x4m", new Vector3(54, L5 - 1, z), 90f); Ceiling(g, 54, L5 - 1, z, 90f, 1f, 0.5f); }
        for (float z = -36; z <= 20; z += 8) K(g, "Beam-8m", new Vector3(52.5f, L5 - 2, z), 90f);
        foreach (float z in new[] { -32f, -8f, 16f }) K(g, "Pillar-Round-2m", new Vector3(52.5f, 0, z), 0f, new Vector3(1f, 0.75f, 1f));
        K(g, "Stairs-8x10m", new Vector3(47, L0 - 1, -20), 90f);
        Container(g, 54, L5, -4, 90f);
        K(g, LowerWall(), new Vector3(47, 0, 8), 0f);            // Partitions
        K(g, LowerWall(), new Vector3(46, 0, -28), 90f);
        K(g, "PipeRack-8m", new Vector3(44, 0, 16), 0f);                 // Run under it, climb onto it
        K(g, "CrateStack-Stepped-4p5m", new Vector3(49.5f, 0, -12), 0f);  // 1.5 → 3 → 4.5, east: a hop onto the mezzanine (5)

        // Ramps rising east: yard → deck, deck → warehouse roof
        // Yard → deck: a hollow ramp (rising east, no slab of its own); the room under its top opens into the undercroft
        K(g, "Ramp-Hollow-8x10m", new Vector3(19, L0, 20), -90f);
        InsideTunnel("Under the yard ramp", new Vector3(25.5f, 0, 20), new Vector3(22f, 0, 20));
        Cubby("Under the yard ramp", new Vector3(22, 0, 20), "standing near its mouth (3.3–4.3 m), opens into the undercroft");
        K(g, "Ramp-8x10m", new Vector3(35, L5 - 1, -28), 90f);
        // The hard way up each: containers against the faces
        Container(g, 20.5f, L0, -3, 90f);    // 3.5 → the wall top 4 → deck 5
        Container(g, 36.5f, L5, 13, 90f);    // 8.5 → roof 10, against the climb wall, clear of the rafter door

        // Cover up top, evenly: a container each side of a barrier on the deck; a pillar and two vents on the roof
        Barrier(g, 34, L5, 0, 90f);
        Container(g, 30, L5, 14, 0f);
        Container(g, 30, L5, -14, 0f);
        K(g, "Pillar-Round-4m", new Vector3(48, L10, -8), 0f);
        Prop(g, "Vent", new Vector3(48, L10, -32), 0f);
        Prop(g, "Vent", new Vector3(48, L10, 20), 0f);

        // A vent run through the undercroft into the hall: two straight ducts end to end (the T has a divider inside)
        K(g, "Vent-Duct-8m", new Vector3(32, L0, -12), 90f);
        K(g, "Vent-Duct-8m", new Vector3(40, L0, -12), 90f);
        InsideTunnel("Vent run", new Vector3(27f, 0, -12), new Vector3(45f, 0, -12), "crawl");
        Cubby("Vent run", new Vector3(36, 0, -12), "prone (1.7 m), the undercroft to the hall");
        Lamp(g, new Vector3(32, 2.5f, -4), 12f);
        Lamp(g, new Vector3(32, 2.5f, 12), 12f);
        Lamp(g, new Vector3(46, 7f, -12), 18f);
        Lamp(g, new Vector3(46, 7f, 12), 18f);
        Lamp(g, new Vector3(48, L10 + 6, -8), 22f);
    }

    // ─── West: over the void ──────────────────────────────────────────────────

    private const float RingX = -45.1f;

    private static void BuildWest()
    {
        var g = Group("West Void");
        // The ring island (0), a fan below its hole blowing up through it
        var ring = K(g, "Platform-Ring-16m", new Vector3(RingX, L0 - 1, 16), 0f);
        PropsUnder(g, ring, 1.4f, new Vector2(6.5f, 0), new Vector2(-6.5f, 0), new Vector2(0, 6.5f), new Vector2(0, -6.5f));
        Prop(g, "Fan", new Vector3(RingX, -12f, 16), 0f);
        // Bridged from the yard's west edge (past the skirt's rim) to a plate by the ring
        var bridge = K(g, "Bridge-8x4m", new Vector3(-29, L0 - 1, 16), 0f);
        PropsUnder(g, bridge, 1f, new Vector2(-1.5f, 0));
        PropsUnder(g, K(g, "Platform-4x4m", new Vector3(-35, L0 - 1, 16), 0f), 1f, Vector2.zero);
        // Sky deck (10) west of the factory, flush with its roof: the fan's updraft rides you up beside it
        var sky = K(g, "Platform-16x16m", new Vector3(-48, L10 - 1, 32), 0f);
        PropsUnder(g, sky, 1.4f, new Vector2(4.5f, 4.5f), new Vector2(-4.5f, 4.5f), new Vector2(4.5f, -4.5f), new Vector2(-4.5f, -4.5f));
        // (kept clear: a landing spot)
        // A beam from the ring's north edge, under the sky deck, to plates at the factory's west door: the narrow way
        // (you can be knocked off)
        K(g, "Beam-8m", new Vector3(RingX, L0 - 1, 28.2f), 90f);
        PropsUnder(g, K(g, "Platform-4x4m", new Vector3(RingX, L0 - 1, 34.2f), 0f), 1f, Vector2.zero);
        PropsUnder(g, K(g, "Platform-4x4m", new Vector3(RingX + 2, L0 - 1, 38.2f), 0f), 1f, Vector2.zero); // On to the door
        // Hex stepping stones south from the ring to the docks
        PropsUnder(g, K(g, "Platform-Hex-8m", new Vector3(-46, L0 - 1, -2), 0f), 1.6f, Vector2.zero);
        PropsUnder(g, K(g, "Platform-Hex-8m", new Vector3(-42, L0 - 1, -15), 0f), 1.6f, Vector2.zero);
        gaps.Add(("Ring to hex 1", new Vector3(-45, 0, 8.5f), new Vector3(-46, 0, 1.4f)));
        gaps.Add(("Hex 1 to hex 2", new Vector3(-45, 0, -5.4f), new Vector3(-43, 0, -11.5f)));
        gaps.Add(("Hex 2 to the docks", new Vector3(-41, 0, -18.4f), new Vector3(-37, 0, -23.5f)));
        Lamp(g, new Vector3(RingX, 6, 16), 18f);
    }

    // ─── South: the docks (0), open to the void ───────────────────────────────

    // Two pits in the lane, mirrored about the docks' middle (x −8)
    private static readonly float[] LanePits = { -28f, 12f };

    private static void BuildDocks()
    {
        var g = Group("Docks");
        // x −40..24, z −64..−24: a plain quay north and south (32 × 16 slabs), one straight lane between them (z −48..−40)
        foreach (float x in new[] { -24f, 8f })
        {
            Slab(g, "Floor-32x16m", x, -32, L0);
            Slab(g, "Floor-32x16m", x, -56, L0);
        }
        for (float x = -36; x <= 20; x += 8)
            if (System.Array.IndexOf(LanePits, x) < 0) Tile(g, "Floor-Lanes-8m", x, -44, L0, 0f);
        // Arch gates from the yard
        K(g, "Archway-8m", new Vector3(-12, L0, -24), 0f);
        K(g, "Archway-8m", new Vector3(12, L0, -24), 0f);
        Tunnel("Dock Gate W", new Vector3(-12, 0, -22), new Vector3(-12, 0, -27), "run");
        Tunnel("Dock Gate E", new Vector3(12, 0, -22), new Vector3(12, 0, -27), "run");
        // North quay: a stacked pair of containers west (a climb); east, the gatehouse: a deck at 5 up its own ramp,
        // 6 m across from the east deck (a sprint jump over), with a walk-through under it
        Container(g, -32, L0, -32, 0f);
        Container(g, -32, L0 + ContainerHeight, -32, 0f);
        K(g, "Struct-Gatehouse-16x8m", new Vector3(10, L0, -32), 180f);
        gaps.Add(("Gatehouse deck to the east deck", new Vector3(18, L5, -32), new Vector3(24, L5, -32)));
        Barrier(g, -8, L0, -58, 0f);
        // South quay: two slide tunnels (slide or crawl through, along z), mirrored about x −8
        foreach (float x in new[] { -16f, 0f })
        {
            K(g, "Tunnel-Slide-8x4m", new Vector3(x, L0, -56), 0f);
            Tunnel($"Slide tunnel {x:0}", new Vector3(x - 0.1f, 0, -61), new Vector3(x - 0.1f, 0, -51), "crawl");
            Cubby("Slide tunnel", new Vector3(x, 0, -56), "prone (1.7 m), open at both ends");
        }
        // South quay: two open containers, their open ends (local +X) facing each other across the middle; crate stacks
        // on the north quay and cable spools by the pier, mirrored
        K(g, "CargoContainer-Open-8m", new Vector3(-32, L0, -56), 0f);
        K(g, "CargoContainer-Open-8m", new Vector3(16, L0, -56), 180f);
        InsideTunnel("Dock container W", new Vector3(-26, 0, -56), new Vector3(-31, 0, -56));
        InsideTunnel("Dock container E", new Vector3(10, 0, -56), new Vector3(15, 0, -56));
        K(g, "CrateStack-2x2-3m", new Vector3(-20, L0, -30), 0f);
        foreach (float x in new[] { -26f, 10f }) K(g, "CableSpool-Upright-2m", new Vector3(x, L0, -60), 0f);
        // The pier: a catwalk out over the void (past the skirt's rim), a teleport pad at its end to the sky deck (and back)
        for (float z = -69; z >= -77; z -= 8)
        {
            var walk = K(g, "Catwalk-8x2m", new Vector3(-20, L0 - 1, z), 90f);
            if (z < -69) PropsUnder(g, walk, 0.8f, Vector2.zero);
        }
        PropsUnder(g, K(g, "Platform-8x4m", new Vector3(-20, L0 - 1, -83), 0f), 1.2f, new Vector2(-2, 0), new Vector2(2, 0));
        var a = K(g, "TeleportPad", new Vector3(-20, L0, -83), 0f).AddComponent<TeleportPad>();
        var b = K(g, "TeleportPad", new Vector3(-48, L10, 28), 0f).AddComponent<TeleportPad>();
        Link(a, b, 0f);
        Link(b, a, 180f);
        Lamp(g, new Vector3(-8, 8, -44), 24f);
        Lamp(g, new Vector3(-20, 5, -81), 14f);
    }


    // ─── Spawns ───────────────────────────────────────────────────────────────

    private static readonly (string name, float x, float z, float y)[] SpawnSpots =
    {
        ("Yard North-West",  -14,   6, L0),
        ("Yard South-West",  -20, -22, L0),
        ("Yard East",         12,  22, L0),
        ("Service Level",     12, -12, Service),
        ("Factory Mezzanine",-32,  46, L5),
        ("Factory Roof",     -14,  44, L10),
        ("Loading Dock",       5,  44, L5),
        ("East Deck",         30,  -2, L5),
        ("Warehouse Roof",    48,  12, L10),
        ("Warehouse Hall",    43, -34, L0),
        ("Ring Island",    RingX,  22, L0),
        ("Docks West",       -24, -52, L0),
        ("Docks East",         2, -44, L0),
        ("Sky Deck",         -54,  38, L10),
        ("West Hall",       -104, -44, L0),
        ("West Room",       -104, -24, L5),
        ("West Tower",       -72,   4, L0),
    };


    private static void RenderOverviews()
    {
        // Let the wind run a few seconds so it shows in the pictures
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(6f, true, true);
        Shot("foundry_top", new Vector3(-30, 170, -16), Quaternion.Euler(90, 0, 0), true, 100f);
        Shot("foundry_westisle", new Vector3(-40, 28, -80), Quaternion.LookRotation(new Vector3(-48, -18, 64)), false, 0f);
        Shot("foundry_zip", new Vector3(-95, 20, 26), Quaternion.LookRotation(new Vector3(28, -6, -6)), false, 0f);
        Shot("foundry_tower", new Vector3(-78, 2.5f, 6), Quaternion.LookRotation(new Vector3(0.6f, 0.9f, -1f)), false, 0f);
        Shot("foundry_sw", new Vector3(-95, 70, -100), Quaternion.LookRotation(new Vector3(95, -60, 100)), false, 0f);
        Shot("foundry_ne", new Vector3(95, 75, 100), Quaternion.LookRotation(new Vector3(-95, -65, -100)), false, 0f);
        Shot("foundry_yard", new Vector3(-20, 6, -20), Quaternion.LookRotation(new Vector3(20, -2, 30)), false, 0f);
        Shot("foundry_factory", new Vector3(-24, 3, 14), Quaternion.LookRotation(new Vector3(0, 0.5f, 1)), false, 0f);
        Shot("foundry_service", new Vector3(10, -2.5f, -10), Quaternion.LookRotation(new Vector3(10, -1.5f, -10)), false, 0f);
        Shot("foundry_east", new Vector3(10, 10, -30), Quaternion.LookRotation(new Vector3(30, -3, 25)), false, 0f);
        Shot("foundry_hall", new Vector3(42, 2.8f, -34), Quaternion.LookRotation(new Vector3(0.45f, 0.02f, 1f)), false, 0f);
        Shot("foundry_island", new Vector3(-150, -10, -150), Quaternion.LookRotation(new Vector3(158, -15, 146)), false, 0f);
        Shot("foundry_under", new Vector3(120, -100, 100), Quaternion.LookRotation(new Vector3(-112, 70, -104)), false, 0f);
        Shot("foundry_edge", new Vector3(80, -4, -75), Quaternion.LookRotation(new Vector3(-30, -8, 40)), false, 0f);
        Shot("foundry_west", new Vector3(-95, 4, 0), Quaternion.LookRotation(new Vector3(50, -6, 15)), false, 0f);
        Shot("foundry_mezz", new Vector3(-38, 2.5f, 34), Quaternion.LookRotation(new Vector3(1f, 0.05f, 0.6f)), false, 0f);
    }
}
