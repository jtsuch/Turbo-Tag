using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using static GreyboxKit;
using static KitLevel;

/// <summary>
/// Builds Skyreach (Assets/Scenes/Levels/Skyreach.unity): Skyreach's vertical map of stacked "mainlands"
/// built from Justin's modular kit on the Fracture model (CLAUDE.md, "Fracture is the model map"). Replaced the
/// greybox Skyreach (Nov 13 2026).
///
///   The town (0): one big open floor, walled in on the west and north by a U of mesas (three storeys, roof 15) and on
///     the east by the East mesa (15) and the NE terrace (10); open to the void on the south, where the gatehouse
///     stands. Long house tunnels, a two-storey house with a loft, crawl platforms, the arcade (a pavilion), the fan.
///   The undercroft (−5) under the town: corridors round 16 × 16 blocks (two of them hide rooms), in by two ramp
///     trenches and two drop holes (bounce pads back up), and out south through the cliff onto a pier to the Dock Isle.
///   The mesas are buildings you go inside: storeys at 0, 5 and 10 with stairs between them, a cave three storeys tall
///     with the cave lift up to the roof, doors onto the decks of the ramp terraces. Each mesa has an easy way up (a
///     ramp terrace: ramp → a one-storey annex (5) → ramp → a two-storey annex (10) → ramp → the roof), a hard way
///     (a 5 m step block → a 10 m step block → a climb face) and an inside way (the stairs).
///   On the U's corner the High mesa (30, a hall inside, its own ramp terrace on the U roof), on it the Crown (45, an
///     inside climb to its hatch); on the East mesa the East tower (30, an inside staircase to its hatch). The Sky Isle
///     (45) floats over the town: the Sky lift from the town, a long catwalk from the Crown (one hop at the end), and a
///     zip line from it down to the East mesa. The Dock Isle (−5) floats south: the undercroft's pier, a ramp bridge
///     back up to the town, a zip line down to it from the gatehouse.
///
/// Re-running rebuilds Geometry/Skyreach; review output goes to Library/TurboTag/scratch/skyreach_report.txt and
/// skyreach_*.png. Menu: Turbo Tag/Levels/Rebuild Skyreach (editor job: BuildSkyreach).
/// </summary>
public static class SkyreachBuilder
{
    private const string ScenePath  = "Assets/Scenes/Levels/Skyreach.unity";
    private const string RootName   = "Skyreach";
    private const string ReportPath = "Library/TurboTag/scratch/skyreach_report.txt";

    private const float G = 0f, U = -5f, L5 = 5f, L10 = 10f, L15 = 15f, L20 = 20f, L25 = 25f, L30 = 30f, L45 = 45f;

    [MenuItem("Turbo Tag/Levels/Rebuild Skyreach")]
    public static void Build()
    {
        Reset(20261113, "Assets/Environment/Generated/Skyreach_Island.asset", "Skyreach Island", G, U);
        if (!System.IO.File.Exists(ScenePath)) AssetDatabase.CopyAsset("Assets/Scenes/Levels/Fracture.unity", ScenePath);
        var scene = Begin(ScenePath, RootName);
        var geometry = root.parent;
        for (int i = geometry.childCount - 1; i >= 0; i--)
            if (geometry.GetChild(i) != root) Object.DestroyImmediate(geometry.GetChild(i).gameObject);

        BuildGround();
        BuildUndercroft();
        BuildTown();
        BuildWestMesa();
        BuildNorthMesa();
        BuildTerrace();
        BuildEastMesa();
        BuildHighMesa();
        BuildSky();
        BuildDock();
        BuildIsland();
        CloseCliffOpening();
        CheckAccess();
        report.Add($"cubbies: {cubbies.Count}");
        foreach (var c in cubbies) report.Add("  " + c);
        CheckOverlaps();
        Bounds(new Vector3(0, 0, -20), 460f, -120f, 80f);
        Spawns(SpawnSpots, new Vector3(0, 0, -10));
        CheckSpawnSpacing(SpawnSpots);
        PlaceSpawner(new Vector3(0, 0.1f, -24), -110f);
        Atmosphere.SunnyDay(root, new Vector3(1f, 0f, 0.35f), new Bounds(new Vector3(0, 20, -10), new Vector3(180, 50, 220)));

        Finish(scene, ReportPath, "Skyreach", RenderOverviews);
    }

    // ─── Layout ───────────────────────────────────────────────────────────────

    // The main island x −80..80, z −72..80: cliff paths (8 m) on the west, north and east edges
    private static readonly Vector4 Main = new(-80, 80, -72, 80);
    // The mesas: the U (west and north) and the East mesa, three storeys (0, 5, 10), roofs at 15
    private static readonly Vector4 WestRect  = new(-72, -40, -40, 72);
    private static readonly Vector4 NorthRect = new(-40, 40, 40, 72);
    private static readonly Vector4 EastRect  = new(40, 72, -72, 0);
    private static readonly Vector4 TerraceRect = new(40, 72, 40, 72);      // NE terrace, roof 10
    // The undercroft (−5) under the town, and the passage out through the south cliff (x 0..8)
    private static readonly Vector4 UnderRect = new(-32, 32, -64, 24);
    private static readonly Vector2 PassageCell = new(4, -68);
    // Holes in the town: two ramp trenches down to the undercroft, two drop holes
    // (both in the undercroft's 16 m middle corridor, clear of the blocks' walls)
    private static readonly Vector4 TrenchW = new(-8, 8, -32, -16), TrenchE = new(-8, 8, -56, -40);
    private static readonly Vector2 DropS = new(-4, -36), DropN = new(12, 20);
    // Long house tunnel tiles (town 0)
    private static readonly Vector2[] LongHouse = { new(-20, -60), new(-20, -52) };
    // The cave in the West mesa (three storeys tall) and its lift shaft through the roof
    private static readonly Vector4 Cave = new(-64, -48, -16, 0);
    private static readonly Vector2 LiftShaft = new(-52, -12);
    // The Dock Isle (−5)
    private static readonly Vector4 DockRect = new(-16, 24, -120, -96);

    private static bool In(Vector4 r, Vector2 c) => Inside(r, c);
    private static bool InMesa(Vector2 c) => In(WestRect, c) || In(NorthRect, c) || In(EastRect, c);

    // ─── Ground ───────────────────────────────────────────────────────────────

    private static void BuildGround()
    {
        var g = Group("Ground");
        var plain = new HashSet<Vector2>();
        var tiles = new HashSet<Vector2>();
        for (float x = Main.x + 4; x < Main.y; x += 8)
            for (float z = Main.z + 4; z < Main.w; z += 8)
            {
                var c = new Vector2(x, z);
                if (In(TrenchW, c) || In(TrenchE, c) || c == DropS || c == DropN || System.Array.IndexOf(LongHouse, c) >= 0) continue;
                // Indoors (the mesas and the terrace) one pattern; outdoors big plain slabs
                if (InMesa(c) || In(TerraceRect, c)) tiles.Add(c); else plain.Add(c);
            }
        Fill(g, plain, null, G, false, Main.x, Main.z);
        Fill(g, tiles, "Floor-Tiles-8m", G, false, Main.x, Main.z);
        foreach (var c in LongHouse) Tile(g, "Tunnel-8x8m", c.x, c.y, G);
        Tunnel("Long house", new Vector3(-20, G, -69), new Vector3(-20, G, -45), "run");
        K(g, "CrateStack-2x2-3m", new Vector3(-14.5f, G, -60), 0f);   // Up onto its roof (4)

        // Drop holes into the undercroft: a hole tile with a curb round the hole, a bounce pad straight below
        foreach (var p in new[] { DropS, DropN })
        {
            Tile(g, "Floor-Hole-8m", p.x, p.y, G);
            Curb(g, "Drop Hole Curb", new Vector4(p.x - 2.05f, p.x + 2.05f, p.y - 2.05f, p.y + 2.05f), G);
            Bounce(g, new Vector3(p.x, U, p.y), 34f);
            Cubby("Drop hole", new Vector3(p.x, U, p.y), "a drop into the undercroft; its bounce pad throws you back up");
        }
        // The trenches: curbs round them except where the ramp arrives
        Curb(g, "Trench W Curb", TrenchW, G, ('N', -4f), ('N', 4f));
        Curb(g, "Trench E Curb", TrenchE, G, ('S', -4f), ('S', 4f));
    }

    // ─── Undercroft (−5) ──────────────────────────────────────────────────────

    // Corridors 8 m wide round six 16 × 16 blocks (two of them hide rooms: a squeeze in, a slide gap out), the town's
    // slab for a ceiling; the trenches and drop holes come down into the corridors; the passage south to the pier
    private static readonly Vector4[] Blocks =
    {
        new(-24, -8, -56, -40), new(8, 24, -56, -40),
        new(-24, -8, -32, -16), new(8, 24, -32, -16),
        new(-24, -8, -8, 8),    new(8, 24, -8, 8),
    };

    private static void BuildUndercroft()
    {
        var g = Group("Undercroft");
        var cells = new HashSet<Vector2>();
        for (float x = UnderRect.x + 4; x < UnderRect.y; x += 8)
            for (float z = UnderRect.z + 4; z < UnderRect.w; z += 8)
                cells.Add(new Vector2(x, z));
        cells.Add(PassageCell);
        foreach (var c in cells)
        {
            Tile(g, "Floor-Grate-8m", c.x, c.y, U);
            bool open = In(TrenchW, c) || In(TrenchE, c) || c == DropS || c == DropN;
            if (!open && !System.Array.Exists(Blocks, b => In(b, c))) Ceiling(g, c.x, G - 1f, c.y);
        }
        // Its outer walls (standing outside it, under the town's slab), the passage's sides
        Building(g, "Undercroft Walls", UnderRect.x, UnderRect.y, UnderRect.z, UnderRect.w, U, 1,
                 south: "PMPSXSMP", north: "PMPMPMPM", west: "PMPMPMPMPMP", east: "MPMPMPMPMPM", roof: false);
        Cut(g, "Wall-Mid-8m", 0, U, -68.5f, W, 7f);
        Cut(g, "Wall-Mid-8m", 8, U, -68.5f, E, 7f);
        Tunnel("Undercroft passage", new Vector3(4, U, -58), new Vector3(4, U, -80), "run");
        // The blocks; two are hide rooms with a squeeze in and a slide gap out
        for (int i = 0; i < Blocks.Length; i++)
        {
            var b = Blocks[i];
            bool room = i == 0 || i == 5;
            string west = i == 0 ? "GM" : i == 5 ? "QM" : "MP", east = i == 0 ? "QM" : i == 5 ? "GM" : "PM";
            Building(g, room ? "Hide Room" : "Undercroft Block", b.x, b.y, b.z, b.w, U, 1,
                     south: "MP", north: "PM", west: west, east: east, roof: false);
            if (room)
            {
                for (float x = b.x + 4; x < b.y; x += 8) for (float z = b.z + 4; z < b.w; z += 8) Ceiling(g, x, G - 1f, z);
                Cubby("Undercroft hide room", new Vector3((b.x + b.y) / 2f, U, (b.z + b.w) / 2f), "standing, dark, a squeeze in and a slide gap out");
                Lamp(g, new Vector3((b.x + b.y) / 2f, U + 3f, (b.z + b.w) / 2f), 6f);
            }
        }
        // The ramps up the trenches (−5 → 0) onto the town
        K(g, "Ramp-8x10m", new Vector3(0, U - 1f, -21), 0f);        // Rising north onto the town at z −16
        K(g, "Ramp-8x10m", new Vector3(0, U - 1f, -51), 180f);      // Rising south onto the town at z −56
        foreach (var p in new[] { new Vector3(-28, U + 3f, -40), new Vector3(28, U + 3f, -40), new Vector3(0, U + 3f, -48),
                                  new Vector3(0, U + 3f, -4), new Vector3(-28, U + 3f, 12), new Vector3(28, U + 3f, -20),
                                  new Vector3(0, U + 3f, 20), new Vector3(4, U + 3f, -66) })
            Lamp(g, p, 14f);
    }

    // The passage cell's south edge has no skirt (KitLevel.skirtOpenings): a tunnel mouth out onto the pier, skirt below
    // and above it
    private static void CloseCliffOpening()
    {
        var g = Group("Cliff Opening");
        K(g, "Tunnel-8x8m", new Vector3(PassageCell.x, U - 1f, -76), 0f);   // The mouth, out of the cliff (not ground: no skirt round it)
        // Below the mouth's floor and above its roof: skirt pieces cut to fit (the island's rim is a 1 m band at z −72..−73)
        K(g, "Skirt-Wall-8m", new Vector3(PassageCell.x, SkirtBottom, -72.5f), 180f, new Vector3(1f, (U - 1f - SkirtBottom) / 16f, 1f));
    }

    // ─── The town (0) ─────────────────────────────────────────────────────────

    private static void BuildTown()
    {
        var g = Group("Town");
        // The two-storey house (roof 5, doors all round, a climb face) with the loft on its roof (10): a hiding room
        Building(g, "House", -8, 8, -8, 8, G, 1, south: "DW", north: "WD", west: "DN", east: "DC");
        Building(g, "Loft", -8, 0, -8, 0, L5, 1, south: "W", north: "D", west: "W", east: "D", drop: "SW");
        Cubby("The loft", new Vector3(-4, L5, -4), "standing, a room on the house's roof (two doors)");
        Lamp(g, new Vector3(0, 3f, 0), 12f);

        // The arcade: a pavilion (deck 5 on piers, its ramp down south)
        K(g, "Struct-Pavilion-16x16m", new Vector3(-16, G, 20), 180f);
        // The gatehouse on the open south edge (deck 5, its ramp down west); a zip line from its deck down to the dock
        K(g, "Struct-Gatehouse-16x8m", new Vector3(24, G, -64), 180f);
        ZipLineBetween(g, new Vector3(28, L5, -64), new Vector3(16, U, -110));
        // Two crawl platforms mirrored about the middle, either side of the south drop hole
        foreach (float x in new[] { -20f, 20f })
        {
            K(g, "Platform-Crawl-8x8m", new Vector3(x, G, -36), 0f);
            Tunnel($"Town crawl {x:0} south", new Vector3(x - 0.1f, G, -41), new Vector3(x - 0.1f, G, -37.2f), "crawl");
            Tunnel($"Town crawl {x:0} north", new Vector3(x - 0.1f, G, -31), new Vector3(x - 0.1f, G, -34.8f), "crawl");
            Cubby("Crawl platform", new Vector3(x, G, -36), "prone (1.7 m), open at both ends");
        }
        // Cover, in rows and pairs
        foreach (float x in new[] { -28f, 28f }) Container(g, x, G, -56, 0f);
        foreach (float x in new[] { -12f, 12f }) Barrier(g, x, G, -52, 0f);
        K(g, "CargoContainer-Tunnel-8m", new Vector3(16, G, 32), 0f);
        InsideTunnel("Town container", new Vector3(11, 0, 32), new Vector3(21, 0, 32));
        Cubby("Container tunnel", new Vector3(16, G, 32), "standing, open at both ends");
        // The fan below the North mesa's south face: an updraft ride up beside it (to the roof at 15)
        Prop(g, "Fan", new Vector3(32, G, 34), 0f);
        // The SW corner yard and the NE yard: a shed each
        K(g, "Shed-4x4m", new Vector3(-56, G, -60), 180f, Vector3.one * ShedScale);   // Its door north
        Tunnel("SW shed door", new Vector3(-56, G, -54), new Vector3(-56, G, -59), "run");
        K(g, "Shed-4x4m", new Vector3(60, G, 20), 90f, Vector3.one * ShedScale);     // Its door west
        Tunnel("NE shed door", new Vector3(54, G, 20), new Vector3(59, G, 20), "run");
        Lamp(g, new Vector3(0, 9, -30), 26f);
        Lamp(g, new Vector3(-20, 8, 20), 20f);
        Lamp(g, new Vector3(20, 8, -50), 20f);
    }

    // ─── West mesa (15) ───────────────────────────────────────────────────────

    private static readonly List<Vector2> wellsW5 = new(), wellsW10 = new();

    private static void BuildWestMesa()
    {
        var g = Group("West Mesa");
        // Stairs: 0 → 5 rising north (top z 24), 5 → 10 rising north (top z 40) on the mesa's west strip
        wellsW5.Clear(); wellsW10.Clear();
        wellsW5.AddRange(Stairs(g, -68, 24, G, true));
        wellsW10.AddRange(Stairs(g, -68, 48, L5, true));
        // The U's floors: the West and North mesas share them (the North mesa's own call adds its stairwells)
        Storeys(g, WestRect, G, (c, s) =>
            (s < 3 && In(Cave, c)) || c == LiftShaft
            || (s == 1 && wellsW5.Contains(c)) || (s == 2 && wellsW10.Contains(c)), System.Array.Empty<Vector2>());
        // The cave: three storeys tall, the lift up through the roof
        KitMover(g, "Cave Lift", new Vector3(LiftShaft.x, G, LiftShaft.y), new Vector3(LiftShaft.x, L15 - 1f, LiftShaft.y), 4f);
        Lamp(g, new Vector3(-56, 8f, -8), 18f);

        // Faces. South (z −40): the climb-block stack at x −60/−52 (5 m block, 10 m block, climb faces above)
        Face(g, "West Mesa S", 'S', WestRect.z, WestRect.x, G, "MWM", "D4WM", "DMC9", "DWW");
        // West (x −72, the cliff path): the cliff tunnel door from the cave at z −8, windows and pipes
        Face(g, "West Mesa W", 'W', WestRect.x, WestRect.z, G,
             "PWM", "MWP", "NWM", "DWW", "PWM", "MWP", "QWM", "PWW", "MWP", "NWM", "PWW", "MWP", "PWM", "MWW");
        // East (x −40, the town), z −40..40: the gallery door into the cave at z −12; annex 1 (z −24..−8, roof 5) and
        // annex 2 (z −8..8, roof 10) against it: doors from their rooms and decks, the bands they cover left out, the
        // ramp from annex 2's roof arriving at z 4 (storey 3 none)
        Face(g, "West Mesa E", 'E', WestRect.y, WestRect.z, G,
             "MWM", "DWW", "DDW4", "DDW4", "DMD9", "MMX9", "NWM", "MWP", "MWW", "SWS");
        TallPost(g, WestRect.x, WestRect.z, W, S);
        TallPost(g, WestRect.y, WestRect.z, E, S);
        TallPost(g, WestRect.x, WestRect.w, W, N);

        // Easy: the ramp terrace. Annex 1 (x −40..−24, z −24..−8, roof 5) and annex 2 (z −8..8, roof 10); ramp A from the
        // town onto annex 1, ramp B on annex 1 onto annex 2, ramp C on annex 2 west onto the mesa roof
        Building(g, "West Annex 1", -40, -24, -24, -8, G, 1, south: "-X", north: null, west: null, east: "DG", posts: "SE");
        Cut(g, "Archway-8m", -35.5f, G, -8, S, 7f);                               // Annex 2's south face, over annex 1
        Cut(g, "WallWindow-8m", -35.5f, L5, -8, S, 7f);
        Building(g, "West Annex 2", -40, -24, -8, 8, G, 2, south: null, north: "-D", west: null, east: "WD", posts: "NE");
        Cut(g, "WallWindow-8m", -35.5f, G, -24, S, 7f);                         // Meeting the mesa's wall: 7 m
        Cut(g, "WallWindow-8m", -35.5f, G, 8, N, 7f);
        Cut(g, UpperWall(), -35.5f, 4f, 8, N, 7f);
        // Annex 2's south face: lower panels into annex 1, upper ones standing on annex 1's roof (the ramp arrives at x −28)
        Wall(g, LowerWall(), -28, G, -8, S);
        Post(g, -24, L5, -8, E, S, 4f);
        Tunnel("West annex 1 to 2", new Vector3(-36, G, -11), new Vector3(-36, G, -5), "run");
        K(g, "Ramp-8x10m", new Vector3(-28, G - 1f, -29), 0f);          // A: ground → annex 1, rising north
        K(g, "Ramp-8x10m", new Vector3(-28, L5 - 1f, -13), 0f);         // B: annex 1 → annex 2, rising north
        K(g, "Ramp-8x10m", new Vector3(-35, L10 - 1f, 4), -90f);        // C: annex 2 → mesa roof, rising west
        Lamp(g, new Vector3(-32, 3f, -16), 10f);
        Lamp(g, new Vector3(-32, 6f, 0), 12f);

        // Hard: the step blocks against the south face: step 1 (5) with a climb face, step 2 (10) with a climb face above
        // step 1's roof, the mesa's climb face above step 2's roof
        Building(g, "West Step 1", -64, -56, -48, -40, G, 1, south: "C", north: null, west: null, east: null, posts: "SW");
        Building(g, "West Step 2", -56, -48, -48, -40, G, 2, south: "W", north: null, west: null, east: null, posts: "SE");
        // Their sides meeting the mesa's face: 7 m
        Cut(g, "WallWindow-8m", -64, G, -44.5f, W, 7f);
        Cut(g, "WallWindow-8m", -48, G, -44.5f, E, 7f);
        Cut(g, UpperWall(), -48, 4f, -44.5f, E, 7f);
        Cut(g, "Archway-8m", -56, G, -44.5f, W, 7f);
        Cut(g, ClimbWall, -56, L5, -44.5f, W, 7f);
        Post(g, -56, L5, -48, W, S, 4f);

        // On the roof (15): two huts with a roof bridge, a crawl platform
        K(g, "Shed-4x4m", new Vector3(-64, L15, -28), 90f, Vector3.one * ShedScale);
        K(g, "Shed-4x4m", new Vector3(-48, L15, -28), -90f, Vector3.one * ShedScale);
        K(g, "Bridge-8x4m", new Vector3(-56, L15 + 6f - 1f, -28), 0f, new Vector3(10f / 8f, 1f, 1f));
        K(g, "Platform-Crawl-8x8m", new Vector3(-56, L15, 8), 0f);
        Tunnel("West roof crawl S", new Vector3(-56.1f, L15, 3), new Vector3(-56.1f, L15, 6.8f), "crawl");
        Cubby("West roof crawl platform", new Vector3(-56, L15, 8), "prone (1.7 m), open at both ends");
        Lamp(g, new Vector3(-56, L15 + 8f, -10), 22f);
    }

    // ─── North mesa (15) ──────────────────────────────────────────────────────

    private static void BuildNorthMesa()
    {
        var g = Group("North Mesa");
        var wells5 = new List<Vector2>(Stairs(g, 20, 56, G, true));     // 0 → 5 (top z 56)
        var wells10 = new List<Vector2>(Stairs(g, -28, 64, L5, true));  // 5 → 10 (top z 64)
        var hatch = new Vector2(4, 60);
        Storeys(g, NorthRect, G, (c, s) => (s == 1 && wells5.Contains(c)) || (s == 2 && wells10.Contains(c)), new[] { hatch });
        Container(g, 4, L10, 66.5f, 0f);   // Under the roof hatch: a climb out (13.5 → 15)
        Cubby("North mesa roof hatch", new Vector3(hatch.x, L15, hatch.y), "a drop in onto the top storey, a container to climb out");
        // Faces. South (z 40, the town), from x −40: the inner corner column cut short; the through-run door at x −20;
        // the step blocks at x −28/−36 (climb faces); a squeeze and a niche
        Column(g, "North Mesa S", -35.5f, NorthRect.z, S, "SWS", G, 7f);
        Face(g, "North Mesa S", 'S', NorthRect.z, -32, G, "DMC9", "MMC9", "D4WM", "DWW", "QWM", "NWM", "MWP", "PWM", "MWW");
        // North (z 72, the cliff path): the through-run's north door at x −20
        Face(g, "North Mesa N", 'N', NorthRect.w, NorthRect.x, G, "PWM", "MWP", "NWW", "PWM", "DWW", "MWP", "PWM", "MWW", "PWM", "MWP");
        // East (x 40): against the NE terrace (rows at 0..9, roof 10): a squeeze and a door between them, its roof
        // band left out, above it windows and the ramp's arrival from the terrace roof (z 60)
        Face(g, "North Mesa E", 'E', NorthRect.y, NorthRect.z, G, "QMW9", "DMW9", "MMX9", "MMW9");
        TallPost(g, NorthRect.x, NorthRect.w, W, N);   // (the West mesa's north face ends here)
        Post(g, NorthRect.y, L10 - 1f, NorthRect.w, E, N, 5f);   // Above the terrace's north wall

        // Hard: step blocks against the south face: step 1 (x −16..−8, 5) with a climb face, step 2 (x −32..−16, 10) with
        // a climb face above step 1's roof, the mesa's climb faces above step 2's roof
        Building(g, "North Step 1", -16, -8, 32, 40, G, 1, south: "C", north: null, west: null, east: null, posts: "SE");
        Building(g, "North Step 2", -32, -16, 32, 40, G, 2, south: "WM", north: null, west: null, east: null, posts: "SW");
        // Their sides meeting the mesa's face: 7 m
        Cut(g, "WallWindow-8m", -8, G, 35.5f, E, 7f);
        Cut(g, "WallWindow-8m", -32, G, 35.5f, W, 7f);
        Cut(g, UpperWall(), -32, 4f, 35.5f, W, 7f);
        Cut(g, "Archway-8m", -16, G, 35.5f, E, 7f);
        Cut(g, ClimbWall, -16, L5, 35.5f, E, 7f);
        Post(g, -16, L5, 32, E, S, 4f);

        // On the roof: a watch hut, a vent run to crawl
        K(g, "Shed-4x4m", new Vector3(-8, L15, 64), 0f, Vector3.one * ShedScale);
        foreach (float x in new[] { 20f, 28f }) K(g, "Vent-Duct-8m", new Vector3(x, L15, 52), 90f);
        InsideTunnel("North roof vent run", new Vector3(15f, L15, 52), new Vector3(33f, L15, 52), "crawl");
        Cubby("North roof vent run", new Vector3(24, L15, 52), "prone (1.7 m), open at both ends");
        Lamp(g, new Vector3(0, 7f, 56), 20f);
        Lamp(g, new Vector3(0, L15 + 8f, 56), 22f);
    }

    // ─── NE terrace (10) ──────────────────────────────────────────────────────

    private static void BuildTerrace()
    {
        var g = Group("NE Terrace");
        // Two storeys (roof 10) in the NE corner, a mezzanine (5) along its north wall; its west side is the North
        // mesa's east face. In through an annex (x 40..56, z 24..40, roof 5) on its south side; out east to the cliff path
        Building(g, "Terrace", 40, 72, 40, 72, G, 2, south: null, north: "WPWM", west: null, east: "DWPW", posts: "NE");
        Mezzanine(g, 48, 72, 64, 72, L5, 'S');   // (the North mesa's wall stands in its first metre at x 40)
        K(g, "Stairs-8x10m", new Vector3(68, G - 1f, 59), 0f);
        // Its south face: the annex's room below (x 40..56) and the yard (x 56..72)
        Wall(g, "Archway-8m", 44, G, 40, S); Wall(g, LowerWall(), 52, G, 40, S);
        Wall(g, "WallWindow-8m", 52, L5, 40, S);                      // x 44: ramp B arrives (none)
        Facade(g, 60, 40, S, 'D'); Facade(g, 68, 40, S, 'C');
        Post(g, 72, G, 40, E, S, 9f);
        Tunnel("Terrace south door", new Vector3(60, G, 37), new Vector3(60, G, 43), "run");
        Tunnel("Annex to terrace", new Vector3(44, G, 37), new Vector3(44, G, 43), "run");
        Building(g, "Terrace Annex", 40, 56, 24, 40, G, 1, south: "DW", north: null, west: "W-", east: "XX", posts: "SW SE");
        Cut(g, "WallWindow-8m", 40, G, 35.5f, W, 7f);                         // Meeting the North mesa's wall: 7 m
        K(g, "Ramp-8x10m", new Vector3(61, G - 1f, 32), -90f);          // A: the yard → annex roof (5), rising west
        K(g, "Ramp-8x10m", new Vector3(44, L5 - 1f, 35), 0f);           // B: annex roof → terrace roof (10), rising north
        K(g, "Ramp-8x10m", new Vector3(45, L10 - 1f, 60), -90f);        // C: terrace roof → North mesa roof (15), rising west
        K(g, "Shed-4x4m", new Vector3(64, L10, 52), 0f, Vector3.one * ShedScale);
        Tunnel("Terrace hut door", new Vector3(64, L10, 46), new Vector3(64, L10, 51), "run");
        Lamp(g, new Vector3(56, 7f, 56), 18f);
    }

    // ─── East mesa (15) and the East tower (30) ───────────────────────────────

    private static void BuildEastMesa()
    {
        var g = Group("East Mesa");
        var wells5 = new List<Vector2>(Stairs(g, 68, -40, G, false));     // 0 → 5, rising south (top z −40)
        var wells10 = new List<Vector2>(Stairs(g, 44, -48, L5, false));   // 5 → 10, rising south (top z −48)
        Storeys(g, EastRect, G, (c, s) => (s == 1 && wells5.Contains(c)) || (s == 2 && wells10.Contains(c)), System.Array.Empty<Vector2>());
        // Faces. West (x 40, the town): the tunnel door at z −20, a niche, climb faces over the step blocks (z −44/−36)
        Face(g, "East Mesa W", 'W', EastRect.x, EastRect.z, G, "MWM", "PWW", "SWP", "D4WM", "DMC9", "SWS", "DWM", "NWW", "PWM");
        // East (x 72, the cliff path): the tunnel door out at z −20
        Face(g, "East Mesa E", 'E', EastRect.y, EastRect.z, G, "PWM", "MWP", "PWM", "MWW", "PWP", "MWM", "DWM", "PWW", "MWM");
        // South (z −72, the void): windows and pipes
        Face(g, "East Mesa S", 'S', EastRect.z, EastRect.x, G, "PWM", "MWP", "PWW", "WWM");
        // North (z 0): annex 2 (x 40..56, roof 10) and annex 1 (x 56..72, roof 5) against it: doors from their rooms and
        // decks; ramp C from annex 2's roof arrives at x 44 (storey 3 none)
        Face(g, "East Mesa N", 'N', EastRect.w, EastRect.x, G, "DMX9", "DMD9", "DWW4", "MDW4");
        TallPost(g, EastRect.x, EastRect.z, W, S);
        TallPost(g, EastRect.y, EastRect.z, E, S);
        Tunnel("East mesa run-through", new Vector3(37, G, -20), new Vector3(75, G, -20), "run");

        // Easy: the ramp terrace on the north face. Annex 1 (x 56..72, z 0..16, roof 5), annex 2 (x 40..56, roof 10)
        Building(g, "East Annex 1", 56, 72, 0, 16, G, 1, south: null, north: "WX", west: null, east: "DW", posts: "NE");
        Building(g, "East Annex 2", 40, 56, 0, 16, G, 2, south: null, north: "WD", west: "DW", east: null, posts: "NW");
        Cut(g, "Archway-8m", 56, G, 4.5f, E, 7f); Wall(g, LowerWall(), 56, G, 12, E);   // Annex 2's east face: into annex 1
        Cut(g, "WallWindow-8m", 56, L5, 12.5f, E, 7f);                                   // z 1..9: ramp B arrives (none)
        Post(g, 56, L5, 16, E, N, 4f);
        Tunnel("East annex 2 to 1", new Vector3(53, G, 4.5f), new Vector3(59, G, 4.5f), "run");
        K(g, "Ramp-8x10m", new Vector3(68, G - 1f, 21), 180f);           // A: the yard → annex 1, rising south
        K(g, "Ramp-8x10m", new Vector3(61, L5 - 1f, 5), -90f);           // B: annex 1 → annex 2, rising west (clear of the mesa's wall)
        K(g, "Ramp-8x10m", new Vector3(44, L10 - 1f, 5), 180f);          // C: annex 2 → mesa roof, rising south

        // Hard: step blocks against the west face: step 1 (z −48..−40, 5) with a climb face, step 2 (z −40..−32, 10) with a
        // climb face above step 1's roof, the mesa's climb face above step 2's roof
        Building(g, "East Step 1", 32, 40, -48, -40, G, 1, south: null, north: null, west: "W", east: null, posts: "SW");
        Building(g, "East Step 2", 32, 40, -40, -32, G, 2, south: null, north: null, west: "W", east: null, posts: "NW");
        // Their sides meeting the mesa's face: 7 m
        Cut(g, ClimbWall, 35.5f, G, -48, S, 7f);
        Cut(g, "WallWindow-8m", 35.5f, G, -32, N, 7f);
        Cut(g, UpperWall(), 35.5f, 4f, -32, N, 7f);
        Cut(g, "Archway-8m", 35.5f, G, -40, S, 7f);
        Cut(g, ClimbWall, 35.5f, L5, -40, S, 7f);
        Post(g, 32, L5, -40, W, S, 4f);

        // The East tower (30) on the roof's south-east: hollow, Wall-Tall below and a 5 m band above (15..29); a door
        // west, a staircase of 3 m steps up its south and east walls to a hatch in its roof (27 → 30); a hut on top
        const float tx0 = 56, tx1 = 72, tz0 = -72, tz1 = -56;
        Face(g, "East Tower W", 'W', tx0, tz0, L15, "SSS", "DSS");
        Face(g, "East Tower N", 'N', tz1, tx0, L15, "SWS", "SSS");
        Face(g, "East Tower S", 'S', tz0, tx0, L15, true, "SSS", "SWS");
        Face(g, "East Tower E", 'E', tx1, tz0, L15, true, "SSS", "SWS");
        TallPost(g, tx0, tz1, W, N, L15);
        TallPost(g, tx1, tz0, E, S, L15, true);
        TallPost(g, tx0, tz0, W, S, L15, true);
        TallPost(g, tx1, tz1, E, N, L15, true);
        Roof(g, tx0, tx1, tz0, tz1, L30, new[] { new Vector2(68, -68) });
        Cubby("East tower roof hatch", new Vector3(68, L30, -68), "a drop in, the staircase back up");
        // Three 3 m steps (18, 21, 24): the top one half under the hatch with 5 m of headroom (a 6 m grab out)
        var steps = new[] { new Vector2(58, -70), new Vector2(62, -70), new Vector2(66, -70) };
        for (int i = 0; i < steps.Length; i++)
            for (int h = 0; h <= i; h++)
                K(g, "Block-4x4x4m", new Vector3(steps[i].x, L15 + 3f * h, steps[i].y), 0f, new Vector3(1f, 0.75f, 1f));
        K(g, "Shed-4x4m", new Vector3(62, L30, -64), 90f, Vector3.one * ShedScale);
        Lamp(g, new Vector3(64, L15 + 7f, -64), 14f);
        Lamp(g, new Vector3(56, 7f, -36), 20f);
        Lamp(g, new Vector3(52, L15 + 8f, -30), 22f);
    }

    // ─── High mesa (30) and the Crown (45) ────────────────────────────────────

    private static void BuildHighMesa()
    {
        var g = Group("High Mesa");
        // On the U's north-west corner (x −72..−40, z 40..72, from 15): a hall inside (storeys at 15, 20, 25 open as one
        // tall hall over its east half), the roof at 30. Its own ramp terrace on the U roof to the south
        var r = new Vector4(-72, -40, 40, 72);
        // The west half has a floor at 20 (up stairs); the east half is the hall, open to the roof; no floor at 25
        var wells = new List<Vector2>(Stairs(g, -68, 56, L15, true));
        Storeys(g, r, L15, (c, s) => (s == 1 && (c.x > -56f || wells.Contains(c))) || s == 2, System.Array.Empty<Vector2>());
        Face(g, "High Mesa S", 'S', r.z, r.x, L15, "D4DW", "M4WW", "DMW9", "DMX9");
        Face(g, "High Mesa E", 'E', r.y, r.z, L15, "DWW", "MWP", "QWM", "MWW");
        Face(g, "High Mesa W", 'W', r.x, r.z, L15, true, "PWM", "MWP", "PWW", "MWM");
        Face(g, "High Mesa N", 'N', r.w, r.x, L15, true, "PWM", "MWW", "PWP", "MWM");
        Post(g, r.y, L25 - 1f, r.z, E, S, 5f);   // Above annex 2's east wall
        Post(g, r.x, L20, r.z, W, S, 10f);       // Above annex 1's west wall
        TallPost(g, r.x, r.w, W, N, L15, true); TallPost(g, r.y, r.w, E, N, L15, true);
        Lamp(g, new Vector3(-48, L15 + 7f, 56), 18f);

        // The ramp terrace on the U roof: annex 1 (x −72..−56, z 24..40, roof 20), annex 2 (x −56..−40, roof 25)
        Building(g, "High Annex 1", -72, -56, 24, 40, L15, 1, south: "XD", north: null, west: "WM", east: null, posts: "SW", drop: "W");
        Building(g, "High Annex 2", -56, -40, 24, 40, L15, 2, south: "WD", north: null, west: null, east: "W-", posts: "SE");
        Cut(g, ClimbWall, -40, L15, 35.5f, E, 7f);                                    // Meeting the High mesa's wall: 7 m
        Cut(g, ClimbWall, -40, L20 - 1f, 35.5f, E, 7f);
        Wall(g, "Archway-8m", -56, L15, 28, W); Cut(g, LowerWall(), -56, L15, 35.5f, W, 7f);
        Cut(g, "WallWindow-8m", -56, L20, 27.5f, W, 7f);                              // z 31..39: ramp B arrives
        Post(g, -56, L20, 24, W, S, 4f);
        K(g, "Ramp-8x10m", new Vector3(-68, L15 - 1f, 19), 0f);          // A: U roof → annex 1 (20), rising north
        K(g, "Ramp-8x10m", new Vector3(-61, L20 - 1f, 35), 90f);         // B: annex 1 → annex 2 (25), rising east
        K(g, "Ramp-8x10m", new Vector3(-44, L25 - 1f, 35), 0f);          // C: annex 2 → High mesa roof (30), rising north

        // The Crown (45) on the High mesa's north-west: hollow, a door south, steps up to a hatch, a lookout on top
        const float cx0 = -72, cx1 = -56, cz0 = 56, cz1 = 72;
        Face(g, "Crown S", 'S', cz0, cx0, L30, "SWS", "DSS");
        Face(g, "Crown E", 'E', cx1, cz0, L30, "SSS", "SWS");
        Face(g, "Crown W", 'W', cx0, cz0, L30, true, "SWS", "SSS");
        Face(g, "Crown N", 'N', cz1, cx0, L30, true, "SSS", "SWS");
        TallPost(g, cx1, cz0, E, S, L30); TallPost(g, cx0, cz0, W, S, L30, true);
        TallPost(g, cx0, cz1, W, N, L30, true); TallPost(g, cx1, cz1, E, N, L30, true);
        Roof(g, cx0, cx1, cz0, cz1, L45, new[] { new Vector2(-68, 68) });
        Cubby("Crown roof hatch", new Vector3(-68, L45, 68), "a drop in, the steps back up");
        // Three 3 m steps (33, 36, 39): the top one half under the hatch with 5 m of headroom (a 6 m grab out)
        var steps = new[] { new Vector2(-70, 58), new Vector2(-70, 62), new Vector2(-70, 66) };
        for (int i = 0; i < steps.Length; i++)
            for (int h = 0; h <= i; h++)
                K(g, "Block-4x4x4m", new Vector3(steps[i].x, L30 + 3f * h, steps[i].y), 0f, new Vector3(1f, 0.75f, 1f));
        K(g, "Shed-4x4m", new Vector3(-61, L45, 60), 0f, Vector3.one * ShedScale);
        Lamp(g, new Vector3(-64, L30 + 7f, 64), 14f);
        Lamp(g, new Vector3(-56, L45 + 6f, 64), 20f);
    }

    // ─── Sky Isle (45) ────────────────────────────────────────────────────────

    private static void BuildSky()
    {
        var g = Group("Sky Isle");
        // Two rimmed platforms over the town (x −32..0, z 8..24), propellers underneath; a hut and a raised corner
        foreach (float x in new[] { -24f, -8f })
            PropsUnder(g, K(g, "Platform-16x16m", new Vector3(x, L45 - 1f, 16), 0f), 1.4f,
                       new Vector2(4.5f, 4.5f), new Vector2(-4.5f, 4.5f), new Vector2(4.5f, -4.5f), new Vector2(-4.5f, -4.5f));
        K(g, "Shed-4x4m", new Vector3(-26, L45, 18), 90f, Vector3.one * ShedScale);
        K(g, "Block-4x4x4m", new Vector3(-4, L45, 20), 0f);
        K(g, "CrateStack-2x2-3m", new Vector3(-9, L45, 21), 0f);
        // The Sky lift from the town beside it
        KitMover(g, "Sky Lift", new Vector3(2, G, 20), new Vector3(2, L45 - 1f, 20), 7f);
        // The long catwalk from the Crown: east along z 64, then south along x −17 to the isle (a 3 m hop at the end)
        // (A corner piece spans its pivot −3..+1 along its arms)
        for (float x = -51; x <= -27; x += 8) PropsUnder(g, K(g, "Catwalk-8x2m", new Vector3(x, L45 - 1f, 64), 0f), 0.8f, Vector2.zero);
        K(g, "Catwalk-8x2m", new Vector3(-21.5f, L45 - 1f, 64), 0f, new Vector3(3f / 8f, 1f, 1f));
        K(g, "Catwalk-Corner-4x4m", new Vector3(-17, L45 - 1f, 64), 180f);   // Arms west and south
        for (float z = 57; z >= 33; z -= 8) PropsUnder(g, K(g, "Catwalk-8x2m", new Vector3(-17, L45 - 1f, z), 90f), 0.8f, Vector2.zero);
        gaps.Add(("Sky catwalk hop to the isle", new Vector3(-17, L45, 29), new Vector3(-17, L45, 24.5f)));
        // A zip line from the isle down to the East mesa roof
        ZipLineBetween(g, new Vector3(-3, L45, 10), new Vector3(52, L15, -40));
        Lamp(g, new Vector3(-16, L45 + 6f, 16), 20f);
    }

    // ─── Dock Isle (−5) ───────────────────────────────────────────────────────

    private static void BuildDock()
    {
        var g = Group("Dock Isle");
        var cells = new HashSet<Vector2>();
        for (float x = DockRect.x + 4; x < DockRect.y; x += 8)
            for (float z = DockRect.z + 4; z < DockRect.w; z += 8)
                if (!(x == -12f && z == -108f) && !(x == -4f && z == -108f)) cells.Add(new Vector2(x, z));
        Fill(g, cells, null, U, false, DockRect.x, DockRect.z);
        // A tunnel block (two tunnel tiles), a crawl platform, cover
        foreach (float x in new[] { -12f, -4f }) Tile(g, "Tunnel-8x8m", x, -108, U, 90f);
        Tunnel("Dock tunnel", new Vector3(-18, U, -108), new Vector3(2, U, -108), "run");
        K(g, "Platform-Crawl-8x8m", new Vector3(16, U, -116), 0f);
        Tunnel("Dock crawl", new Vector3(15.9f, U, -111), new Vector3(15.9f, U, -114.8f), "crawl");
        Cubby("Dock crawl platform", new Vector3(16, U, -116), "prone (1.7 m), open at both ends");
        Barrier(g, 4, U, -116, 0f);

        // The pier from the undercroft's cliff mouth (z −80) to the dock (rim at z −95)
        K(g, "Bridge-8x4m", new Vector3(PassageCell.x, U - 1f, -87.5f), 90f, new Vector3(15f / 8f, 1f, 1f));
        // Back up to the town: a long ramp (−5 → 0) from the dock's north rim, then a bridge onto the town's south rim
        var ramp = K(g, "Ramp-16x8m", new Vector3(-12, U - 1f, -88), 0f);
        PropsUnder(g, ramp, 1f, new Vector2(0, -4), new Vector2(0, 4));
        K(g, "Platform-8x4m", new Vector3(-12, G - 1f, -78), 0f);
        K(g, "Platform-8x4m", new Vector3(-12, G - 1f, -74.5f), 0f, new Vector3(1f, 1f, 0.75f));
        Lamp(g, new Vector3(4, U + 7f, -108), 20f);

        // The undercroft's passage leaves the main island through the south cliff here (no skirt panel)
        skirtOpenings.Add((CellOf(PassageCell.x, PassageCell.y), Vector2Int.down));
    }

    // ─── Spawns ───────────────────────────────────────────────────────────────

    private static readonly (string name, float x, float z, float y)[] SpawnSpots =
    {
        ("Town Square",      16,  -8, G),
        ("Town West",       -32, -44, G),
        ("Town East",        28, -46, G),
        ("Town North",        8,  28, G),
        ("SW Yard",         -68, -56, G),
        ("NE Yard",          44,  20, G),
        ("Undercroft South", -20, -60, U),
        ("Undercroft North", -20, 20, U),
        ("Cave",            -60,  -8, G),
        ("West Mesa 5",     -60,  36, L5),
        ("North Mesa 10",    12,  52, L10),
        ("East Mesa 5",      56, -20, L5),
        ("West Roof",       -56,   0, L15),
        ("North Roof",      -20,  56, L15),
        ("East Roof",        60, -36, L15),
        ("Terrace",          56,  60, L10),
        ("High Mesa",       -52,  56, L30),
        ("Dock",              8, -104, U),
        ("Sky Isle",        -12,  12, L45),
    };

    private static void RenderOverviews()
    {
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(6f, true, true);
        Shot("skyreach_top", new Vector3(0, 220, -20), Quaternion.Euler(90, 0, 0), true, 110f);
        Shot("skyreach_sw", new Vector3(-120, 90, -150), Quaternion.LookRotation(new Vector3(120, -80, 150)), false, 0f);
        Shot("skyreach_ne", new Vector3(130, 100, 130), Quaternion.LookRotation(new Vector3(-130, -90, -130)), false, 0f);
        Shot("skyreach_town", new Vector3(10, 12, -66), Quaternion.LookRotation(new Vector3(-10, -2, 60)), false, 0f);
        Shot("skyreach_westface", new Vector3(-4, 14, -40), Quaternion.LookRotation(new Vector3(-34, -2, 30)), false, 0f);
        Shot("skyreach_east", new Vector3(10, 18, 30), Quaternion.LookRotation(new Vector3(40, -4, -30)), false, 0f);
        Shot("skyreach_high", new Vector3(-20, 40, 10), Quaternion.LookRotation(new Vector3(-36, 2, 40)), false, 0f);
        Shot("skyreach_under", new Vector3(-4, U + 2.5f, -60), Quaternion.LookRotation(new Vector3(0, 0, 1)), false, 0f);
        Shot("skyreach_dock", new Vector3(40, 10, -130), Quaternion.LookRotation(new Vector3(-40, -10, 40)), false, 0f);
        Shot("skyreach_cave", new Vector3(-50, 2.5f, -14), Quaternion.LookRotation(new Vector3(-1, 0.6f, 0.3f)), false, 0f);
    }
}
