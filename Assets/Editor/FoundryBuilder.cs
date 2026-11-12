using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using static GreyboxKit;

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
    private const string KitFolder  = "Assets/Environment/Prefabs/";

    private const float L0 = 0f, L5 = 5f, L10 = 10f, Service = -5f;
    private static readonly Vector3 N = Vector3.forward, S = Vector3.back, E = Vector3.right, W = Vector3.left;

    // ─── Kit usage rules (Justin, Nov 15, updated Nov 11) ───────────────────────
    // Open ground: big plain slabs (Floor-32x32m, Floor-32x16m), laid out so each open area divides into them; the rimmed
    // Platform-16x16m where a 16 × 16 piece is a feature (the Stack's dais, decks). Patterns (Floor-Tiles, Inlay, Grate,
    // Lanes, the 32 m patterned slabs) only for one room or one segment at a time, one kind per room: never mixed at
    // random. Floor-Plate-8m has bevelled corners that leave holes: ceilings only.
    // Holes and props are laid out evenly: on the grid, square to it, in rows, rings or mirrored pairs.
    // Props are sized to the penguin (2.9 m tall, 1.4 m wide): containers ×4/3, barriers ×1.5, crates 1.5 m.
    // Repeated wall panels are mixed; only Wall-Pipes may chain. No flat walls at the map edge. Wall-Climb-8m (the arrows)
    // only right under a top you can climb onto. Towers: mostly Wall-Tall, a few windows and doors.
    // Every ramp and flight of stairs has a floor at its start and a landing at its end, in its direction of travel.
    // Beams: balance beams or supports under decks and mezzanines.

    private static System.Random rng;
    private static readonly string[] LowerWalls =
        { "Wall-Grid-8m", "Wall-Hazard-8m", "Wall-Mid-8m", "Wall-Vented-8m", "WallRunPanel-8m", "WallWindow-8m", "Wall-Pipes-8m" };
    // Its arrows point up: only directly under a top players can climb onto (a 4 m wall under a deck or roof), never at random
    private const string ClimbWall = "Wall-Climb-8m";
    private static readonly string[] UpperWalls = { "Wall-Upper-8m", "Wall-Grid-8m", "Wall-Mid-8m", "Wall-Vented-8m" };
    private static string lastWall, lastUpper;

    // A random pick from the palette, mostly not the same as the last one (Wall-Pipes may repeat)
    private static string Mix(string[] palette, ref string last)
    {
        string id;
        int tries = 0;
        do id = palette[rng.Next(palette.Length)];
        while (id == last && id != "Wall-Pipes-8m" && ++tries < 20);
        return last = id;
    }

    private static string LowerWall() => Mix(LowerWalls, ref lastWall);
    private static string UpperWall() => Mix(UpperWalls, ref lastUpper);

    [MenuItem("Turbo Tag/Levels/Rebuild Foundry")]
    public static void Build()
    {
        rng = new System.Random(20261115);
        cubbies.Clear();
        lastWall = lastUpper = null;
        island.Clear();
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
        ZipLine(g, new Vector3(-70, TowerTop, 5), new Vector3(-64, L0, 42));
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

    /// <summary>
    /// A zip line from <paramref name="topBase"/> (where its high anchor stands) to <paramref name="bottomBase"/> (the low
    /// anchor): two ZipAnchor-6m posts turned to face each other (their arms point along local −X), a cable between the
    /// arm tips, and the ride (`World/ZipLine`) on the high one.
    /// </summary>
    private static void ZipLine(Transform g, Vector3 topBase, Vector3 bottomBase)
    {
        Vector3 flat = Vector3.ProjectOnPlane(bottomBase - topBase, Vector3.up).normalized;
        float YawToward(Vector3 d) => Mathf.Atan2(d.z, -d.x) * Mathf.Rad2Deg;   // Local −X along d
        var high = K(g, "ZipAnchor-6m", topBase, YawToward(flat));
        K(g, "ZipAnchor-6m", bottomBase, YawToward(-flat));
        // The arm tips: 2 m out from the post toward the other end, 0.4 below the top
        Vector3 from = topBase + flat * 2f + Vector3.up * 5.4f;
        Vector3 to = bottomBase - flat * 2f + Vector3.up * 5.4f;
        var cable = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cable.name = "Zip Cable";
        cable.transform.SetParent(g, false);
        Object.DestroyImmediate(cable.GetComponent<Collider>());
        cable.transform.SetPositionAndRotation((from + to) / 2f, Quaternion.LookRotation(to - from));
        cable.transform.localScale = new Vector3(0.07f, 0.07f, Vector3.Distance(from, to));
        cable.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Environment/Materials/Metals/BlackMetal.mat");
        var line = high.AddComponent<global::ZipLine>();
        var so = new SerializedObject(line);
        so.FindProperty("from").vector3Value = from;
        so.FindProperty("to").vector3Value = to;
        so.ApplyModifiedPropertiesWithoutUndo();
        float drop = from.y - to.y, run = Vector3.Distance(new Vector3(from.x, 0, from.z), new Vector3(to.x, 0, to.z));
        report.Add($"zip line: {from:F1} → {to:F1}, {run:0} m across, {drop:0} m down ({Mathf.Atan2(drop, run) * Mathf.Rad2Deg:0}°)");
    }

    // ─── Movement lines and cubbies (Nov 12): roof hatches, ceiling coffers, the hide report ──────────────

    /// <summary>
    /// A roof (or any slab) of 8 m cells over x0..x1, z0..z1 with its top at <paramref name="top"/>: plain Platform-8x4
    /// pairs, a Floor-Hole at each hatch (a drop in, a climb out), and light ceiling liners under every other cell. Each
    /// coffer cell gets a Ceiling-Coffer hung under the slab (open underneath: jump up into it) instead of a liner.
    /// </summary>
    private static void RoofCells(Transform g, float x0, float x1, float z0, float z1, float top, Vector2[] hatches, Vector2[] coffers)
    {
        for (float x = x0 + 4; x < x1; x += 8)
            for (float z = z0 + 4; z < z1; z += 8)
            {
                var cell = new Vector2(x, z);
                bool hatch = System.Array.IndexOf(hatches, cell) >= 0, coffer = System.Array.IndexOf(coffers, cell) >= 0;
                if (hatch) Tile(g, "Floor-Hole-8m", x, z, top);
                else foreach (float dz in new[] { -2f, 2f }) K(g, "Platform-8x4m", new Vector3(x, top - 1f, z + dz), 0f);
                if (coffer)
                {
                    K(g, "Ceiling-Coffer-8x8m", new Vector3(x, top - 1f - CofferHeight, z), 0f);
                    Cubby("Ceiling coffer", new Vector3(x, top - 1f - CofferHeight, z), "prone (1.7 m), jump up into it from below");
                }
                else if (!hatch) Ceiling(g, x, top - 1f, z);
            }
    }

    private const float CofferHeight = 2.1f;

    // Every hiding spot the build adds, with what fits in it: the report lists them
    private static readonly List<string> cubbies = new();

    private static void Cubby(string what, Vector3 at, string fits) => cubbies.Add($"{what} at ({at.x:0}, {at.y:0}, {at.z:0}): {fits}");

    // ─── Props sized to the penguin (2.9 m tall, 1.4 m wide) ──────────────────

    private const float ContainerScale = 4f / 3f;                    // 8 × 3.5 × 3.3: a penguin fits inside; one grid cell long
    private const float ContainerHeight = 2.6f * ContainerScale;
    private const float BarrierScale = 1.5f;                         // 6 × 1.8 × 1.5: chest-high cover, a plain hop
    private const float CrateSize = 1.5f;                            // Waist high
    private const float ShedScale = 1.5f;                            // Shed-4x4m's door is 2.6 m: ×1.5 → 3.9

    private static GameObject Container(Transform g, float x, float y, float z, float yaw) =>
        K(g, "CargoContainer-6m", new Vector3(x, y, z), yaw, Vector3.one * ContainerScale);

    private static GameObject Barrier(Transform g, float x, float y, float z, float yaw) =>
        K(g, "Barrier-4m", new Vector3(x, y, z), yaw, Vector3.one * BarrierScale);

    /// <summary>A floor slab of any size centred on (x, z) with its top at <paramref name="top"/>; ground ones are recorded
    /// for the island (every 8 m cell they cover).</summary>
    private static GameObject Slab(Transform g, string id, float x, float z, float top, float yaw = 0f)
    {
        var slab = K(g, id, new Vector3(x, top - 1f, z), yaw);
        if (top == L0 || top == Service)
        {
            var rs = slab.GetComponentsInChildren<Renderer>();
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            for (float cx = Mathf.Round(b.min.x) + 4f; cx < b.max.x; cx += 8f)
                for (float cz = Mathf.Round(b.min.z) + 4f; cz < b.max.z; cz += 8f)
                    island.Add(CellOf(cx, cz));
        }
        return slab;
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

    /// <summary>
    /// A 9 m facade panel on a footprint edge (two storeys: up to the roof slab at 9..10): 'W' window, 'D' arched door,
    /// 'P' pipe run, 'M' mixed, 'S' one tall solid panel; a mixed upper panel above from <paramref name="upperFrom"/> (4:
    /// stretched to 9; 5: standing on a slab that crosses the wall line at 4..5), all from <paramref name="baseY"/>.
    /// </summary>
    private static void Facade(Transform g, float x, float z, Vector3 outward, char kind, float upperFrom = 4f, bool upper = true, float baseY = 0f)
    {
        if (kind == 'S') { Wall(g, "Wall-Tall-8m", x, baseY, z, outward); return; }
        string lower = kind == 'W' ? "WallWindow-8m" : kind == 'P' ? "Wall-Pipes-8m" : kind == 'D' ? "Archway-8m"
                     : kind == 'N' ? "Wall-Niche-8m" : kind == 'Q' ? "Wall-NichePass-8m" : LowerWall();
        Wall(g, lower, x, baseY, z, outward);
        if (upper) Wall(g, UpperWall(), x, baseY + upperFrom, z, outward, (9f - upperFrom) / 4f);
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

    private static void Link(TeleportPad from, TeleportPad to, float exitYaw)
    {
        var so = new SerializedObject(from);
        so.FindProperty("partner").objectReferenceValue = to;
        so.ApplyModifiedPropertiesWithoutUndo();
        var so2 = new SerializedObject(to);
        so2.FindProperty("exitYaw").floatValue = exitYaw;
        so2.ApplyModifiedPropertiesWithoutUndo();
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

    // ─── Helpers ──────────────────────────────────────────────────────────────

    // ─── The floating island: a skirt round the edge, dirt underneath, propellers holding it up ──────────

    // Every 8 m ground cell (floor at 0 or the service level), recorded by Tile(); cell k spans 8k..8k + 8
    private static readonly HashSet<Vector2Int> island = new();
    private const float SkirtBottom = -16f;                         // The skirt: 16 m, its top flush with the floor (0)
    private const float DirtStep = 2f, DirtSlope = 1.5f, DirtPlateau = 32f;
    private const string IslandMeshPath = "Assets/Environment/Generated/Foundry_Island.asset";
    private static Vector2Int CellOf(float x, float z) => new(Mathf.FloorToInt(x / 8f), Mathf.FloorToInt(z / 8f));

    private static void BuildIsland()
    {
        var g = Group("Floating Island");
        int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
        foreach (var c in island) { minX = Mathf.Min(minX, c.x); maxX = Mathf.Max(maxX, c.x); minZ = Mathf.Min(minZ, c.y); maxZ = Mathf.Max(maxZ, c.y); }
        // The void outside the island (flood fill from beyond its corner); empty cells it can't reach are pits
        var outer = new HashSet<Vector2Int>();
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(new Vector2Int(minX - 1, minZ - 1)); outer.Add(new Vector2Int(minX - 1, minZ - 1));
        var dirs = new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            foreach (var d in dirs)
            {
                var n = c + d;
                if (n.x < minX - 1 || n.x > maxX + 1 || n.y < minZ - 1 || n.y > maxZ + 1 || island.Contains(n) || !outer.Add(n)) continue;
                queue.Enqueue(n);
            }
        }
        bool IsOuter(Vector2Int c) => !island.Contains(c) && (outer.Contains(c) || c.x < minX || c.x > maxX || c.y < minZ || c.y > maxZ);

        BuildSkirt(g, IsOuter, dirs);
        // Each separate piece of ground (4-connected cells) floats on its own dirt, largest first
        var left = new HashSet<Vector2Int>(island);
        var pieces = new List<HashSet<Vector2Int>>();
        while (left.Count > 0)
        {
            var start = default(Vector2Int);
            foreach (var c in left) { start = c; break; }
            var piece = new HashSet<Vector2Int> { start };
            var todo = new Queue<Vector2Int>(); todo.Enqueue(start); left.Remove(start);
            while (todo.Count > 0)
            {
                var c = todo.Dequeue();
                foreach (var d in dirs) if (left.Remove(c + d)) { piece.Add(c + d); todo.Enqueue(c + d); }
            }
            pieces.Add(piece);
        }
        pieces.Sort((a, b) => b.Count.CompareTo(a.Count));
        for (int k = 0; k < pieces.Count; k++)
            BuildDirt(g, IsOuter, pieces[k], k == 0 ? IslandMeshPath : IslandMeshPath.Replace(".asset", $"_{k}.asset"));
    }

    // Skirt panels (16 m tall flat faces to climb back up) hang round the outer edge just outside it, their tops flush
    // with the floor: a 1 m rim, which the walls on the map's edge stand on. A 1 × 1 post (a panel cut down) fills each
    // convex corner; runs along z stop 1 m short where the edge turns outward, so panels never cross
    private static void BuildSkirt(Transform parent, System.Func<Vector2Int, bool> isOuter, Vector2Int[] dirs)
    {
        var g = Group("Skirt", parent);
        var done = new HashSet<(Vector2Int, Vector2Int)>();
        var cells = new List<Vector2Int>(island);
        cells.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x)); // Runs start at their low end
        float YawOf(Vector2Int d) => d == Vector2Int.up ? 0f : d == Vector2Int.right ? 90f : d == Vector2Int.down ? 180f : 270f;
        Vector3 Centre(Vector2Int c) => new(c.x * 8f + 4f, SkirtBottom, c.y * 8f + 4f);
        Vector3 V(Vector2Int v) => new(v.x, 0f, v.y);
        int panels = 0, posts = 0;
        foreach (var c in cells)
            foreach (var d in dirs)
            {
                if (!isOuter(c + d) || !done.Add((c, d))) continue;
                var t = d.x == 0 ? Vector2Int.right : Vector2Int.up;
                var end = c;
                while (island.Contains(end + t) && isOuter(end + t + d)) { end += t; done.Add((end, d)); }
                // Along t from c's centre: −4 .. the far end; trimmed where a z run meets an x run at an inward corner
                float a = -4f, b = 4f + 8f * (end - c).x + 8f * (end - c).y;
                if (t == Vector2Int.up && island.Contains(c - t) && island.Contains(c - t + d)) a += 1f;
                if (t == Vector2Int.up && island.Contains(end + t) && island.Contains(end + t + d)) b -= 1f;
                // 16 m panels, one 8 m one if odd, all stretched a little to fit the run exactly
                float length = b - a;
                int eights = Mathf.Max(1, Mathf.RoundToInt(length / 8f));
                float stretch = length / (eights * 8f), at = a;
                for (int left = eights; left > 0; )
                {
                    bool big = left >= 2;
                    float piece = (big ? 16f : 8f) * stretch;
                    Vector3 pos = Centre(c) + V(t) * (at + piece / 2f) + V(d) * 4.5f;
                    K(g, big ? "Skirt-Wall-16m" : "Skirt-Wall-8m", pos, YawOf(d), new Vector3(stretch, 1f, 1f));
                    at += piece; left -= big ? 2 : 1; panels++;
                }
            }
        // Convex corners: the square outside both edges
        foreach (var c in cells)
            for (int i = 0; i < 4; i++)
            {
                Vector2Int a = dirs[i], b = dirs[(i + 1) % 4];
                if (!isOuter(c + a) || !isOuter(c + b)) continue;
                K(g, "Skirt-Wall-8m", Centre(c) + V(a) * 4.5f + V(b) * 4.5f, 0f, new Vector3(1f / 8f, 1f, 1f));
                posts++;
            }
        report.Add($"skirt: {panels} panels, {posts} corner posts");
    }

    // The dirt under it all: low-poly, flat-shaded; rock faces down from the skirt's foot (and down the pits), an
    // underside sloping in from the rim to a flat bottom where the big propellers are bolted on
    private static void BuildDirt(Transform parent, System.Func<Vector2Int, bool> isOuter, HashSet<Vector2Int> cells, string meshPath)
    {
        int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
        foreach (var c in cells) { minX = Mathf.Min(minX, c.x); maxX = Mathf.Max(maxX, c.x); minZ = Mathf.Min(minZ, c.y); maxZ = Mathf.Max(maxZ, c.y); }
        int per = Mathf.RoundToInt(8f / DirtStep);
        int i0 = minX * per, i1 = (maxX + 1) * per, j0 = minZ * per, j1 = (maxZ + 1) * per;
        Vector2Int CellOfSub(int i, int j) => new(Mathf.FloorToInt(i / (float)per), Mathf.FloorToInt(j / (float)per));
        bool Solid(int i, int j) => cells.Contains(CellOfSub(i, j));
        bool OuterSub(int i, int j) => isOuter(CellOfSub(i, j));

        // The rim: vertices where the island meets the outer void; depth grows with the distance from it
        var rim = new List<Vector2>();
        for (int i = i0; i <= i1; i++)
            for (int j = j0; j <= j1; j++)
            {
                bool solid = false, open = false;
                for (int a = -1; a <= 0; a++) for (int b = -1; b <= 0; b++) { solid |= Solid(i + a, j + b); open |= OuterSub(i + a, j + b); }
                if (solid && open) rim.Add(new Vector2(i, j) * DirtStep);
            }
        // The underside: sloping in from the rim to a flat bottom, a little rough
        var bottom = new Dictionary<Vector2Int, float>();
        var dist = new Dictionary<Vector2Int, float>();
        for (int i = i0; i <= i1; i++)
            for (int j = j0; j <= j1; j++)
            {
                if (!Solid(i - 1, j - 1) && !Solid(i, j - 1) && !Solid(i - 1, j) && !Solid(i, j)) continue;
                var key = new Vector2Int(i, j);
                var p = new Vector2(i, j) * DirtStep;
                float d = float.MaxValue;
                foreach (var r in rim) d = Mathf.Min(d, (r - p).sqrMagnitude);
                d = dist[key] = Mathf.Sqrt(d);
                float n = Mathf.PerlinNoise(p.x * 0.06f + 31.7f, p.y * 0.06f + 12.3f) - 0.5f;
                bottom[key] = SkirtBottom - 3f - Mathf.Min(d * DirtSlope, DirtPlateau) + n * (d * DirtSlope < DirtPlateau ? 4f : 1.5f);
            }

        // The propellers' mounts: the main one in the deepest middle, more spread out wherever the island is wide.
        // Each hangs from a lobe of dirt (a blunt cone down to a flat tip), so the lobe's tip is the lowest dirt
        // around it and the blades below it clear everything.
        var spots = new List<(Vector2Int v, float d)>();
        foreach (var kv in dist) spots.Add((kv.Key, kv.Value));
        spots.Sort((a, b) => b.d.CompareTo(a.d));
        var mounts = new List<(Vector2 at, float scale, float tipY)>();
        foreach (var (v, d) in spots)
        {
            if (mounts.Count >= 5) break;
            float scale = mounts.Count == 0 ? Mathf.Clamp(d / 9f, 1.8f, 3.5f) : 1.8f;   // The main one sized to the island
            if (d < (mounts.Count == 0 ? 0f : 14f)) break;
            var at = new Vector2(v.x, v.y) * DirtStep;
            if (mounts.Exists(m => (m.at - at).magnitude < 6f * (m.scale + scale) + 10f)) continue;
            // Low enough to clear the rest of the underside under the blades' sweep
            float floor = bottom[v];
            foreach (var kv in bottom)
                if ((new Vector2(kv.Key.x, kv.Key.y) * DirtStep - at).magnitude <= 6.5f * scale) floor = Mathf.Min(floor, kv.Value);
            mounts.Add((at, scale, floor - (mounts.Count == 0 ? 14f : 9f)));
        }
        foreach (var key in new List<Vector2Int>(bottom.Keys))
        {
            var p = new Vector2(key.x, key.y) * DirtStep;
            foreach (var m in mounts)
            {
                float r = (p - m.at).magnitude, flat = 1.2f * m.scale;
                float lobe = m.tipY + Mathf.Max(0f, r - flat) * 1.6f + (r > flat ? (Mathf.PerlinNoise(p.x * 0.2f, p.y * 0.2f) - 0.5f) * 2f : 0f);
                // Never steeper than 2.4 down from the rim: the rim stays a short band of rock, the lobes hang inside it
                lobe = Mathf.Max(lobe, SkirtBottom - 3f - dist[key] * 2.4f);
                bottom[key] = Mathf.Min(bottom[key], lobe);
            }
        }
        float Bottom(int i, int j) => bottom[new Vector2Int(i, j)];

        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var sideTris = new List<int>();
        var underTris = new List<int>();
        void Quad(List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing, bool side)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), facing) < 0f) { (b, d) = (d, b); }
            int s = verts.Count;
            foreach (var v in new[] { a, b, c, d })
            {
                verts.Add(v);
                uvs.Add(side ? new Vector2((v.x + v.z) / 6f, v.y / 6f) : new Vector2(v.x / 6f, v.z / 6f));
            }
            tris.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
        }
        Vector3 Under(int i, int j) => new(i * DirtStep, Bottom(i, j), j * DirtStep);

        var sideDirs = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
        for (int i = i0; i < i1; i++)
            for (int j = j0; j < j1; j++)
            {
                if (!Solid(i, j)) continue;
                Quad(underTris, Under(i, j), Under(i + 1, j), Under(i + 1, j + 1), Under(i, j + 1), Vector3.down, false);
                foreach (var (dx, dz) in sideDirs)
                {
                    if (Solid(i + dx, j + dz)) continue;
                    // The edge shared with the empty neighbour, its two corner vertices
                    int ax = dx == 1 ? i + 1 : i, az = dz == 1 ? j + 1 : j;
                    int bx = dx == 0 ? ax + 1 : ax, bz = dz == 0 ? az + 1 : az;
                    float top = OuterSub(i + dx, j + dz) ? SkirtBottom : L0 - 1f;
                    Vector3 a = Under(ax, az), b = Under(bx, bz);
                    Quad(sideTris, new Vector3(a.x, top, a.z), new Vector3(b.x, top, b.z), b, a, new Vector3(dx, 0, dz), true);
                }
            }

        var mesh = new Mesh { name = "Foundry Island", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(sideTris, 0);
        mesh.SetTriangles(underTris, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(meshPath));
        AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(mesh, meshPath);

        var dirt = new GameObject("Island Dirt");
        dirt.transform.SetParent(parent, false);
        dirt.AddComponent<MeshFilter>().sharedMesh = mesh;
        dirt.AddComponent<MeshRenderer>().sharedMaterials = new[]
        {
            AssetDatabase.LoadAssetAtPath<Material>("Assets/Imported/Materials/Cartoon_Texture_Pack/ROCKS/ROCKS_Cliff/Materials/Rocks_Cliff_A_BC_A.mat"),
            AssetDatabase.LoadAssetAtPath<Material>("Assets/Imported/Materials/Cartoon_Texture_Pack/DIRT/Dirt_Path/Materials/Dirt_Path.mat"),
        };
        dirt.AddComponent<MeshCollider>().sharedMesh = mesh;
        GameObjectUtility.SetStaticEditorFlags(dirt, (StaticEditorFlags)~0);
        float lowest = float.MaxValue; foreach (var y in bottom.Values) lowest = Mathf.Min(lowest, y);
        report.Add($"dirt: {verts.Count} verts, bottom at {lowest:0.0}");

        // The big propellers on the lobes' tips, hubs sunk a little into the dirt; neighbours turn opposite ways
        for (int k = 0; k < mounts.Count; k++)
        {
            var m = mounts[k];
            Propeller(parent, "Propeller-Large-12m", new Vector3(m.at.x, m.tipY + 0.8f * m.scale, m.at.y), 2.76f, m.scale,
                      k == 0 ? 60f : (k % 2 == 0 ? 120f : -120f));
            report.Add($"propeller ×{m.scale:0.0} at ({m.at.x:0}, {m.tipY:0.0}, {m.at.y:0})");
        }
    }

    /// <summary>
    /// A propeller hanging from whatever is above it: its hub's top at <paramref name="hubTop"/>, blades below, spinning
    /// (cosmetic: no collider, not static so it can turn).
    /// </summary>
    private static GameObject Propeller(Transform g, string id, Vector3 hubTop, float capHeight, float scale, float degreesPerSecond)
    {
        var p = K(g, id, hubTop - Vector3.up * capHeight * scale, 0f, Vector3.one * scale);
        foreach (var c in p.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (var t in p.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
        var spin = p.AddComponent<CosmeticSpin>();
        var so = new SerializedObject(spin);
        so.FindProperty("degreesPerSecond").floatValue = degreesPerSecond;
        so.ApplyModifiedPropertiesWithoutUndo();
        return p;
    }

    /// <summary>Small propellers under a floating piece, at offsets (x, z) from its middle, their hubs against its underside.</summary>
    private static void PropsUnder(Transform g, GameObject piece, float scale, params Vector2[] offsets)
    {
        var rs = piece.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return;
        Bounds b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        int k = 0;
        foreach (var o in offsets)
            Propeller(g, "Propeller-Small-3m", new Vector3(b.center.x + o.x, b.min.y + 0.15f, b.center.z + o.y), 0.71f, scale,
                      (k++ % 2 == 0 ? 1f : -1f) * 420f);
    }

    private static readonly Dictionary<string, GameObject> kit = new();

    /// <summary>A kit piece with its pivot at <paramref name="pivot"/> (kit pivots: bottom centre), turned by <paramref name="yaw"/>.</summary>
    private static GameObject K(Transform parent, string id, Vector3 pivot, float yaw, Vector3? scale = null)
    {
        if (!kit.TryGetValue(id, out var asset) || asset == null)
            kit[id] = asset = AssetDatabase.LoadAssetAtPath<GameObject>(KitFolder + id + ".prefab");
        if (asset == null) { report.Add($"! missing kit piece {id}"); return new GameObject("Missing " + id); }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
        go.transform.SetPositionAndRotation(pivot, Quaternion.Euler(0f, yaw, 0f) * asset.transform.rotation);
        if (scale.HasValue) go.transform.localScale = Vector3.Scale(asset.transform.localScale, scale.Value);
        return go;
    }

    /// <summary>An 8 m (or 16 m) floor tile centred on (x, z) with its top at <paramref name="top"/>.</summary>
    private static GameObject Tile(Transform g, string id, float x, float z, float top, float yaw = 0f)
    {
        if (top == L0 || top == Service) island.Add(CellOf(x, z)); // The ground the floating island carries
        return K(g, id, new Vector3(x, top - 1f, z), yaw);
    }

    // ─── Walls on the kit standard: pivot on the inner face, the body behind it ─────────────────────────

    // The yaw that turns a wall's face (its −Z side, where the body is) toward outward
    private static float Facing(Vector3 outward) => Mathf.Atan2(-outward.x, -outward.z) * Mathf.Rad2Deg;

    /// <summary>A wall panel on a footprint edge (its middle at x, z), standing just outside it facing outward.</summary>
    private static GameObject Wall(Transform g, string id, float x, float y, float z, Vector3 outward, float yScale = 1f) =>
        K(g, id, new Vector3(x, y, z), Facing(outward), new Vector3(1f, yScale, 1f));

    /// <summary>The 1 × 1 post filling the square outside a convex footprint corner, where two outward walls meet.</summary>
    private static void Post(Transform g, float x, float y, float z, Vector3 outA, Vector3 outB, float height)
    {
        bool Close(Vector3 u, Vector3 v) => (u - v).sqrMagnitude < 0.01f;
        foreach (float yaw in new[] { 0f, 90f, 180f, 270f })
        {
            // The post's body is in its local −x/−z quadrant
            var q = Quaternion.Euler(0f, yaw, 0f);
            Vector3 mx = q * Vector3.left, mz = q * Vector3.back;
            if (!(Close(mx, outA) && Close(mz, outB)) && !(Close(mx, outB) && Close(mz, outA))) continue;
            bool tall = height > 4.5f;
            K(g, tall ? "WallCornerPost-1x9m" : "WallCornerPost-1x4m", new Vector3(x, y, z), yaw,
              new Vector3(1f, height / (tall ? 9f : 4f), 1f));
            return;
        }
    }

    /// <summary>
    /// A ceiling liner under a slab that's seen from inside (the kit's slabs have a black underside): a Floor-Plate
    /// flattened to 2 cm and turned face-down just under the slab, so rooms get a light panelled ceiling. No collider, no
    /// shadow, no headroom lost. <paramref name="sx"/>, <paramref name="sz"/> scale it from 8 × 8. Each is 8 % oversize so
    /// the plates' bevelled corners overlap their neighbours (no black gaps), alternate ones 1 cm lower so they don't flicker.
    /// </summary>
    private static void Ceiling(Transform g, float x, float slabBottom, float z, float yaw = 0f, float sx = 1f, float sz = 1f)
    {
        bool odd = (Mathf.RoundToInt(x / 4f) + Mathf.RoundToInt(z / 4f)) % 2 != 0;
        var c = K(g, "Floor-Plate-8m", new Vector3(x, slabBottom - (odd ? 0.01f : 0f), z), yaw, new Vector3(sx * 1.08f, -0.02f, sz * 1.08f));
        c.name = "Ceiling";
        foreach (var col in c.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
        foreach (var r in c.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    /// <summary>A walk-through check for a prop with its own raised floor inside (a container): the path starts on the
    /// floor found at the path's middle, and must fit a running penguin.</summary>
    private static void InsideTunnel(string name, Vector3 from, Vector3 to, string kind = "run")
    {
        Physics.SyncTransforms();
        float floor = to.y;
        foreach (float t in new[] { 0.25f, 0.5f, 0.75f })
            if (Physics.Raycast(Vector3.Lerp(from, to, t) + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 3f)) floor = Mathf.Max(floor, hit.point.y);
        report.Add($"{name}: floor inside at {floor:0.00}");
        Tunnel(name, new Vector3(from.x, floor, from.z), new Vector3(to.x, floor, to.z), kind);
    }

    // ─── Access check ─────────────────────────────────────────────────────────

    /// <summary>
    /// Every ramp, flight of stairs, catwalk and bridge needs 2 m of clear space beyond each end at its landing height
    /// (penguin-tall, a little narrower than the piece): anything standing there more than 0.5 m above the landing (a
    /// container, a wall, a barrier) is reported as "! blocked". Floors and low lips don't count.
    /// </summary>
    private static void CheckAccess()
    {
        Physics.SyncTransforms();
        int blocked = 0, checkedEnds = 0;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            var go = t.gameObject;
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(go)) continue;
            string n = go.name;
            bool slope = n.StartsWith("Ramp-") || n.StartsWith("Stairs-");
            bool walk = n == "Catwalk-8x2m" || n.StartsWith("Bridge-");
            if (!slope && !walk) continue;
            var own = go.GetComponentsInChildren<Collider>(true);
            Bounds b = own[0].bounds;
            foreach (var c in own) b.Encapsulate(c.bounds);
            Vector3 along = slope ? go.transform.forward : go.transform.right;      // Ramps rise along +Z; walks run along x
            along = Vector3.ProjectOnPlane(along, Vector3.up).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, along);
            float half = Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(along.x), 0f, Mathf.Abs(along.z))));
            float width = 2f * Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(side.x), 0f, Mathf.Abs(side.z))));
            foreach (float sign in new[] { -1f, 1f })
            {
                Vector3 end = b.center + along * (sign * half);
                float landing = float.NegativeInfinity;
                foreach (var c in own)
                    if (c.Raycast(new Ray(new Vector3(end.x, b.max.y + 5f, end.z) - along * (sign * 0.3f), Vector3.down), out RaycastHit hit, 100f))
                        landing = Mathf.Max(landing, hit.point.y);
                if (float.IsNegativeInfinity(landing)) continue;
                checkedEnds++;
                // From 0.1 to 2 m past the end, 1.1 m in from each side (walls alongside a landing don't block it)
                var centre = new Vector3(end.x, landing + 0.5f + 1.45f, end.z) + along * (sign * 1.05f);
                var box = new Vector3(Mathf.Max(0.5f, width - 2.2f) / 2f, 1.45f, 0.95f);
                foreach (var hit in Physics.OverlapBox(centre, box, Quaternion.LookRotation(along), ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.transform.IsChildOf(go.transform) || hit.bounds.max.y <= landing + 0.5f) continue;
                    var other = PrefabUtility.GetOutermostPrefabInstanceRoot(hit.gameObject) ?? hit.gameObject;
                    report.Add($"! blocked: {n} at ({go.transform.position.x:0.#}, {go.transform.position.z:0.#}), its {(slope ? (sign > 0 ? "top" : "bottom") : "end")} by {other.name} at ({other.transform.position.x:0.#}, {other.transform.position.y:0.#}, {other.transform.position.z:0.#})");
                    blocked++;
                    break;
                }
            }
        }
        report.Add($"access: {checkedEnds} ramp/stair/walk ends checked, {blocked} blocked");
    }

    // ─── Overlap check ────────────────────────────────────────────────────────

    /// <summary>
    /// Every pair of placed pieces whose colliders cut into each other by more than 5 cm goes in the report. Allowed: a
    /// ramp or stairs whose bottom slab sits in the floor it starts from (the kit's ramps carry their own 1 m slab).
    /// </summary>
    private static void CheckOverlaps()
    {
        Physics.SyncTransforms();
        var pieces = new List<(GameObject go, Collider[] cols, Bounds b)>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)) continue;
            var cols = System.Array.FindAll(t.GetComponentsInChildren<Collider>(true), c => !c.isTrigger && c.enabled);
            if (cols.Length == 0) continue;
            Bounds b = cols[0].bounds;
            foreach (var c in cols) b.Encapsulate(c.bounds);
            pieces.Add((t.gameObject, cols, b));
        }
        bool Concave(Collider c) => c is MeshCollider m && !m.convex;
        bool Sloped(GameObject go) => go.name.StartsWith("Ramp-") || go.name.StartsWith("Stairs-");
        string Name(GameObject go) => $"{go.name} ({go.transform.position.x:0.#}, {go.transform.position.y:0.#}, {go.transform.position.z:0.#})";
        int found = 0, seated = 0;
        for (int i = 0; i < pieces.Count; i++)
            for (int j = i + 1; j < pieces.Count; j++)
            {
                var (ga, ca, ba) = pieces[i];
                var (gb, cb, bb) = pieces[j];
                if (!ba.Intersects(bb)) continue;
                float depth = 0f;
                foreach (var x in ca)
                    foreach (var y in cb)
                    {
                        if (!x.bounds.Intersects(y.bounds)) continue;
                        if (Concave(x) && Concave(y))
                        {
                            // Two exact meshes can't be tested against each other: the bounds' overlap, at its thinnest
                            Vector3 lo = Vector3.Max(x.bounds.min, y.bounds.min), hi = Vector3.Min(x.bounds.max, y.bounds.max);
                            depth = Mathf.Max(depth, Mathf.Min(hi.x - lo.x, Mathf.Min(hi.y - lo.y, hi.z - lo.z)));
                            continue;
                        }
                        var (p, q) = Concave(x) ? (y, x) : (x, y);
                        if (Physics.ComputePenetration(p, p.transform.position, p.transform.rotation,
                                                       q, q.transform.position, q.transform.rotation, out _, out float d))
                            depth = Mathf.Max(depth, d);
                    }
                if (depth <= 0.05f) continue;
                // A ramp's slab in the floor it starts from
                if (Sloped(ga) != Sloped(gb))
                {
                    var (ramp, other) = Sloped(ga) ? (ba, bb) : (bb, ba);
                    float top = Mathf.Min(ramp.max.y, other.max.y);
                    if (top <= ramp.min.y + 1.05f) { seated++; continue; }
                }
                report.Add($"! overlap {Name(ga)} × {Name(gb)}: {depth:0.00} m");
                found++;
            }
        report.Add($"overlaps: {found} (of {pieces.Count} pieces; {seated} ramp slabs seated in their floors, allowed)");

        // Where the walkable tops of the sloped and raised pieces actually are (rays against their own colliders)
        foreach (var (go, cols, b) in pieces)
        {
            bool slope = Sloped(go), deck = go.name.StartsWith("Catwalk") || go.name.StartsWith("Bridge");
            if (!slope && !deck) continue;
            float TopAt(Vector3 at)
            {
                float best = float.NegativeInfinity;
                foreach (var c in cols)
                    if (c.Raycast(new Ray(new Vector3(at.x, b.max.y + 5f, at.z), Vector3.down), out RaycastHit hit, 100f))
                        best = Mathf.Max(best, hit.point.y);
                return best;
            }
            var f = go.transform.forward;
            float half = Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(f.x), 0f, Mathf.Abs(f.z)))) - 0.3f;
            report.Add(slope
                ? $"{Name(go)}: {TopAt(b.center - f * half):0.0} at the back → {TopAt(b.center + f * half):0.0} at the front"
                : $"{Name(go)}: deck at {TopAt(b.center):0.00}");
        }
    }

    /// <summary>A moving kit platform (Platform-4x4m) between two pivots.</summary>
    private static void KitMover(Transform parent, string name, Vector3 aPivot, Vector3 bPivot, float speed)
    {
        var g = Group(name, parent);
        var a = new GameObject("Point A").transform; a.SetParent(g, false); a.position = aPivot;
        var b = new GameObject("Point B").transform; b.SetParent(g, false); b.position = bPivot;
        var platform = K(g, "Platform-4x4m", aPivot, 0f);
        foreach (var t in platform.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
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
