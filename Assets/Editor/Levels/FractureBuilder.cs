using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using static GreyboxKit;
using static KitLevel;

/// <summary>
/// Builds Fracture (Assets/Scenes/Levels/Fracture.unity): the broken-apart map, built from Justin's
/// modular kit on the kit standard (Docs/KitStandards.md), with chase lines and hides added (CLAUDE.md, "Chase-and-hide
/// design"). Replaced the greybox Fracture (Nov 12 2026).
///
/// The five shards are five floating islands (each its own skirt, dirt and propellers) with 16 m of sky between them
/// (14 m rim to rim), crossed by railed bridges; pits cut through the islands drop to the void, each with a 0.5 m curb.
/// Height on 5 m storeys: ground 0; the Bluff, the Terrace and the West plateau 10; the Mesa, the Watch and the East
/// Block 15; the North tower 25; the Spire 30; the Crown and the Sky deck 35. Solid greybox blocks became buildings
/// you can go inside (halls, mezzanines, an undercroft round a sunken courtyard).
///   Centre (32 × 32): the Spire: a 16 × 16 base (cross doors, an inside climb to a roof hatch), two round towers
///     stacked on it (20, 30), a ledge at 30 with the Spire lift; a zip line from there to the Terrace.
///   South (96 × 48): the Crack (a balance beam across), the Well, the South hall, the Pump house, crawl platforms;
///     the Bluff (10) with its deck (5), a mezzanine inside, and a bridge at 10 to the West plateau.
///   East (48 × 96): the Sinkhole (a shuttle across), the Slot, the Bunker tunnel and a vent run; the Terrace (10) with
///     its deck (5), a mezzanine inside, and the East Block (15) on top with a hatch down into it.
///   West (48 × 64): the plateau (10) over an undercroft wrapped round a sunken courtyard (switchback ramps, a hidden
///     passage under them); a pit through it all; the Watch (15); a catwalk and a long ramp bridge down to the North.
///   North (96 × 48): the Twin pits, the North hall (its roof is where the ramp bridge lands), the Mesa (10, a hall with
///     a mezzanine) with its top (15), the hollow North tower (25), the Crown (35); the Sky deck (35) on its lift, a
///     catwalk to the Crown (one hop) and a zip line back to the West plateau.
///
/// Re-running rebuilds Geometry/Fracture; review output goes to Library/TurboTag/scratch/fracture_report.txt and
/// fracture_*.png. Menu: Turbo Tag/Levels/Rebuild Fracture (editor job: BuildFracture).
/// </summary>
public static class FractureBuilder
{
    private const string ScenePath  = "Assets/Scenes/Levels/Fracture.unity";
    private const string RootName   = "Fracture";
    private const string ReportPath = "Library/TurboTag/scratch/fracture_report.txt";

    private const float G = 0f, L5 = 5f, L10 = 10f, L15 = 15f, L25 = 25f, L30 = 30f, L35 = 35f;
    private static readonly Vector3 N = Vector3.forward, S = Vector3.back, E = Vector3.right, W = Vector3.left;

    [MenuItem("Turbo Tag/Levels/Rebuild Fracture")]
    public static void Build()
    {
        Reset(20261112, "Assets/Environment/Generated/Fracture_Island.asset", "Fracture Island", G);
        // A new scene starts as a copy of Foundry's (the kit level systems); every other root under Geometry is cleared
        if (!System.IO.File.Exists(ScenePath)) AssetDatabase.CopyAsset("Assets/Scenes/Levels/Foundry.unity", ScenePath);
        var scene = Begin(ScenePath, RootName);
        var geometry = root.parent;
        for (int i = geometry.childCount - 1; i >= 0; i--)
            if (geometry.GetChild(i) != root) Object.DestroyImmediate(geometry.GetChild(i).gameObject);

        BuildGround();
        BuildCentre();
        BuildSouth();
        BuildEast();
        BuildWest();
        BuildNorth();
        BuildCrossings();
        BuildIsland();
        CheckAccess();
        report.Add($"cubbies: {cubbies.Count}");
        foreach (var c in cubbies) report.Add("  " + c);
        CheckOverlaps();
        Bounds(Vector3.zero, 420f, -110f, 70f);
        Spawns(SpawnSpots, Vector3.zero);
        CheckSpawnSpacing(SpawnSpots);
        PlaceSpawner(new Vector3(-11, 0.1f, -11), -100f);
        Atmosphere.SunnyDay(root, new Vector3(1f, 0f, 0.35f), new Bounds(new Vector3(0, 15, 0), new Vector3(180, 40, 180)));

        Finish(scene, ReportPath, "Fracture", RenderOverviews);
    }

    // ─── Islands and pits ─────────────────────────────────────────────────────

    // Islands (x0, x1, z0, z1): 16 m of sky between neighbours
    private static readonly Vector4 Centre = new(-16, 16, -16, 16);
    private static readonly Vector4 South  = new(-80, 16, -80, -32);
    private static readonly Vector4 East   = new(32, 80, -80, 16);
    private static readonly Vector4 West   = new(-80, -32, -16, 48);
    private static readonly Vector4 North  = new(-16, 80, 32, 80);

    // Pits through the islands (to the void)
    private static readonly Vector4 Crack    = new(-32, -8, -48, -40);
    private static readonly Vector4 Sinkhole = new(40, 56, -40, -24);
    private static readonly Vector4 WestPit  = new(-56, -48, 32, 40);
    private static readonly Vector4 TwinA    = new(8, 16, 40, 48);
    private static readonly Vector4 TwinB    = new(8, 16, 56, 64);
    // Small round-the-hole pits: a Floor-Hole tile (4 m hole) over a shaft
    private static readonly Vector2 Well = new(-20, -68), Slot = new(68, -36);

    // Cells laid by something else (tunnel tiles)
    private static readonly Vector2[] CustomCells = { new(12, -76), new(12, -68), new(44, 4), new(52, 4) };

    private static void BuildGround()
    {
        var g = Group("Ground");
        var holes = new[] { Crack, Sinkhole, WestPit, TwinA, TwinB };
        // One pattern per room, indoors; everything else big plain slabs
        var rooms = new (Vector4 r, string tile)[]
        {
            (new(-80, -64, -56, -32), "Floor-Tiles-8m"),   // The Bluff's hall
            (new(-64, -48, -56, -32), "Floor-Inlay-8m"),   // The room under the Bluff's deck
            (new(64, 80, -80, -56), "Floor-Tiles-8m"),     // The Terrace's hall
            (new(48, 64, -80, -56), "Floor-Inlay-8m"),     // The room under the Terrace's deck
            (new(-72, -40, -16, -8), "Floor-Tiles-8m"),    // The undercroft (round the courtyard)
            (new(-72, -64, -8, 24), "Floor-Tiles-8m"),
            (new(-48, -40, -8, 24), "Floor-Tiles-8m"),
            (new(-72, -40, 24, 48), "Floor-Tiles-8m"),
            (new(40, 80, 48, 80), "Floor-Tiles-8m"),       // The Mesa's hall
            (new(24, 40, 64, 80), "Floor-Inlay-8m"),       // The annex
            (new(-16, 0, 40, 56), "Floor-Inlay-8m"),       // The North hall
        };
        foreach (var island in new[] { Centre, South, East, West, North })
        {
            var plain = new HashSet<Vector2>();
            var patterned = new Dictionary<string, HashSet<Vector2>>();
            for (float x = island.x + 4; x < island.y; x += 8)
                for (float z = island.z + 4; z < island.w; z += 8)
                {
                    var c = new Vector2(x, z);
                    if (System.Array.Exists(holes, h => Inside(h, c)) || System.Array.IndexOf(CustomCells, c) >= 0) continue;
                    if (c == Well || c == Slot) continue;
                    string tile = null;
                    foreach (var (r, t) in rooms) if (Inside(r, c)) { tile = t; break; }
                    if (tile == null) plain.Add(c);
                    else { if (!patterned.TryGetValue(tile, out var set)) patterned[tile] = set = new(); set.Add(c); }
                }
            Fill(g, plain, null, G, false, island.x, island.z);
            foreach (var kv in patterned) Fill(g, kv.Value, kv.Key, G, false, island.x, island.z);
        }
        // The Well and the Slot: a hole tile over its own shaft (not ground the island carries, so the dirt opens under it)
        foreach (var (p, name) in new[] { (Well, "Well"), (Slot, "Slot") })
        {
            K(g, "Floor-Hole-8m", new Vector3(p.x, G - 1f, p.y), 0f);
            Curb(g, name + " Curb", new Vector4(p.x - 2.05f, p.x + 2.05f, p.y - 2.05f, p.y + 2.05f), G);
        }
    }

    // ─── Centre: the Spire ────────────────────────────────────────────────────

    private static void BuildCentre()
    {
        var g = Group("Centre");
        // The base: 16 × 16, 9 m walls, roof 10. Cross doors (x −4 north–south, z 4 east–west) line up with the bridges
        // south and west/east, so one run crosses the whole map. A squeeze in from the north, a niche facing west, a climb
        // face east. Inside: two step columns (3, 6) under a hatch in the roof (a ledge grab out): the easy way up.
        Building(g, "Spire Base", -8, 8, -8, 8, G, 2, south: "DS", north: "DQ", west: "ND", east: "CD",
                 hatches: new[] { new Vector2(4, -4) });
        K(g, "Block-4x4x4m", new Vector3(0, G, -6), 0f, new Vector3(1f, 0.75f, 1f));
        for (int h = 0; h < 2; h++) K(g, "Block-4x4x4m", new Vector3(4, G + 3f * h, -4), 0f, new Vector3(1f, 0.75f, 1f));
        Lamp(g, new Vector3(0, 7f, 0), 14f);

        // Two round towers stacked on the base's north-east roof cell (20, 30); a scaffold on the roof (15) to reach the
        // first; the Spire lift from the isle to a ledge at 30 sticking south from the top
        K(g, "Tower-Round-8m", new Vector3(4, L10, 4), 0f);
        K(g, "Tower-Round-8m", new Vector3(4, L10 + 10f, 4), 0f);
        K(g, "Scaffold-4x4x5m", new Vector3(-2, L10, 4), 0f);
        K(g, "Platform-8x4m", new Vector3(4, L30 - 1f, -4), 90f);
        KitMover(g, "Spire Lift", new Vector3(4, G, -11), new Vector3(4, L30 - 1f, -11), 6f);
        // From the ledge, a zip line down to the Terrace roof on the East island
        ZipLineBetween(g, new Vector3(5f, L30, -1.5f), new Vector3(72, L10, -62));
        Lamp(g, new Vector3(4, L30 + 5, -4), 20f);

        // Cover on the isle: a pinwheel of L-covers in the corners
        for (int i = 0; i < 4; i++)
        {
            float a = (45f + i * 90f) * Mathf.Deg2Rad;
            K(g, "LCover-4m", new Vector3(Mathf.Sin(a) * 17f, G, Mathf.Cos(a) * 17f), i * 90f);
        }
        Lamp(g, new Vector3(-10, 6, -10), 18f);
        Lamp(g, new Vector3(10, 6, 10), 18f);
    }

    // ─── South: the Crack, the Well, the Bluff (10) ───────────────────────────

    private static void BuildSouth()
    {
        var g = Group("South");
        // The Crack: three cells across, a balance beam over its middle (the curb opens either side of it)
        Curb(g, "Crack Curb", Crack, G, ('S', -20f), ('N', -20f));
        K(g, "Beam-8m", new Vector3(-20, G - 1f, -44), 90f);

        // The Bluff: a two-storey hall (roof 10) with a mezzanine (5) along its north wall, and a deck (5) on its east
        // side with a low room under it. Up: a ramp onto the deck, a ramp on the deck onto the roof; the hard way: a
        // climb face onto the deck, another from the deck onto the roof.
        Building(g, "Bluff", -80, -64, -56, -32, G, 2, south: "DN", north: "WM", west: "WPW", east: null,
                 hatches: new[] { new Vector2(-68, -36) }, posts: "SW NW");
        // Its east face: lower panels into the deck room; upper panels standing on the deck (the deck's slab crosses the
        // wall line at 4..5): a climb face, none where the ramp arrives, a door from the mezzanine onto the deck
        Wall(g, "Archway-8m", -64, G, -52, E); Wall(g, LowerWall(), -64, G, -44, E); Wall(g, "Archway-8m", -64, G, -36, E);
        Wall(g, ClimbWall, -64, L5, -52, E); Wall(g, "Archway-8m", -64, L5, -36, E);
        Post(g, -64, L5, -56, E, S, 4f); Post(g, -64, L5, -32, E, N, 4f);
        Tunnel("Bluff hall to deck room", new Vector3(-66, G, -52), new Vector3(-62, G, -52), "run");
        Tunnel("Bluff mezzanine door", new Vector3(-66, L5, -36), new Vector3(-62, L5, -36), "run");
        Mezzanine(g, -80, -64, -40, -32, L5, 'S');
        K(g, "Stairs-8x10m", new Vector3(-76, G - 1f, -45), 0f);
        Lamp(g, new Vector3(-72, 7f, -48), 18f);

        Building(g, "Bluff Deck", -64, -48, -56, -32, G, 1, south: "CX", north: "WP", west: null, east: "DGM", posts: "SE NE");
        K(g, "Ramp-8x10m", new Vector3(-52, G - 1f, -61), 0f);          // Ground → deck, rising north
        K(g, "Ramp-8x10m", new Vector3(-59, L5 - 1f, -44), -90f);       // Deck → Bluff roof, rising west
        K(g, "Shed-4x4m", new Vector3(-72, L10, -48), 0f, Vector3.one * ShedScale);   // The Bluff hut
        Tunnel("Bluff hut door", new Vector3(-72, L10, -54), new Vector3(-72, L10, -49), "run");
        Lamp(g, new Vector3(-56, 3f, -44), 12f);

        // The South hall (one storey, roof 5) on the south edge: in from the north, a niche, windows on the edge
        Building(g, "South Hall", -48, -32, -80, -72, G, 1, south: "WP", north: "DN", west: "C", east: "D");
        Lamp(g, new Vector3(-40, 3f, -76), 10f);
        // The Pump house: two tunnel tiles along z on the east edge (walk through, roof 4); a crate stack to step up
        foreach (float z in new[] { -76f, -68f }) Tile(g, "Tunnel-8x8m", 12, z, G);
        Tunnel("Pump house", new Vector3(12, G, -82), new Vector3(12, G, -62), "run");
        K(g, "CrateStack-2x2-3m", new Vector3(5.5f, G, -64), 0f);
        // Two crawl platforms mirrored about the Well (x −20)
        foreach (float x in new[] { -36f, -4f })
        {
            K(g, "Platform-Crawl-8x8m", new Vector3(x, G, -56), 0f);
            Tunnel($"Crawl platform {x:0} south way in", new Vector3(x - 0.1f, G, -61), new Vector3(x - 0.1f, G, -57.2f), "crawl");
            Tunnel($"Crawl platform {x:0} north way in", new Vector3(x - 0.1f, G, -51), new Vector3(x - 0.1f, G, -54.8f), "crawl");
            Cubby("Crawl platform", new Vector3(x, G, -56), "prone (1.7 m), open at both ends");
        }
        // Cover: barriers either side of the bridge's landing, a container on the yard
        Barrier(g, -14, G, -36, 0f);
        Barrier(g, 6, G, -36, 0f);
        Container(g, -28, G, -68, 0f);
        Lamp(g, new Vector3(-20, 7, -52), 24f);
        Lamp(g, new Vector3(0, 7, -70), 18f);
    }

    // ─── East: the Sinkhole, the Bunker, the Terrace (10), the East Block (15) ─

    private static void BuildEast()
    {
        var g = Group("East");
        // The Sinkhole: a shuttle rides across its north strip (the curb opens there, west and east)
        Curb(g, "Sinkhole Curb", Sinkhole, G, ('W', -28f), ('E', -28f));
        KitMover(g, "Sinkhole Shuttle", new Vector3(42, G - 1f, -28), new Vector3(54, G - 1f, -28), 3f);

        // The Bunker: two tunnel tiles along x on the z 4 line (the bridge from the Centre runs straight in); a vent run
        // beside it (a crawl between the same two yards)
        foreach (float x in new[] { 44f, 52f }) Tile(g, "Tunnel-8x8m", x, 4, G, 90f);
        Tunnel("Bunker tunnel", new Vector3(34, G, 4), new Vector3(62, G, 4), "run");
        foreach (float x in new[] { 44f, 52f }) K(g, "Vent-Duct-8m", new Vector3(x, G, -6), 90f);
        InsideTunnel("Bunker vent run", new Vector3(39f, 0, -6), new Vector3(57f, 0, -6), "crawl");
        Cubby("Bunker vent run", new Vector3(48, G, -6), "prone (1.7 m), open at both ends");
        K(g, "CrateStack-Stepped-4p5m", new Vector3(48, G, 11), 90f);    // Up onto the bunker's roof (4), rising south
        // Two open-ended containers mirrored about the Sinkhole (z −32), along x
        foreach (float z in new[] { -12f, -52f + 4f })
        {
            K(g, "CargoContainer-Tunnel-8m", new Vector3(68, G, z), 0f);
            InsideTunnel($"East container {z:0}", new Vector3(63, 0, z), new Vector3(73, 0, z));
            Cubby("Container tunnel", new Vector3(68, G, z), "standing, open at both ends");
        }
        K(g, "Shed-4x4m", new Vector3(38, G, -64), -90f, Vector3.one * ShedScale);   // Its door faces east
        Tunnel("East shed door", new Vector3(44, G, -64), new Vector3(39, G, -64), "run");

        // The Terrace (roof 10): a hall with a mezzanine (5) along its south wall; a deck (5) on its west side
        Building(g, "Terrace", 64, 80, -80, -56, G, 2, south: "WM", north: "DN", west: null, east: "WPW",
                 hatches: new[] { new Vector2(68, -76) }, posts: "SE NE");
        Wall(g, "Archway-8m", 64, G, -76, W); Wall(g, LowerWall(), 64, G, -68, W); Wall(g, "Archway-8m", 64, G, -60, W);
        Wall(g, "Archway-8m", 64, L5, -76, W); Wall(g, ClimbWall, 64, L5, -60, W);
        Post(g, 64, L5, -80, W, S, 4f); Post(g, 64, L5, -56, W, N, 4f);
        Tunnel("Terrace hall to deck room", new Vector3(62, G, -60), new Vector3(66, G, -60), "run");
        Tunnel("Terrace mezzanine door", new Vector3(66, L5, -76), new Vector3(62, L5, -76), "run");
        Mezzanine(g, 64, 80, -80, -72, L5, 'N');
        K(g, "Stairs-8x10m", new Vector3(76, G - 1f, -67), 180f);
        Lamp(g, new Vector3(72, 7f, -64), 18f);

        Building(g, "Terrace Deck", 48, 64, -80, -56, G, 1, south: "WP", north: "XC", west: "DGM", east: null, posts: "SW NW");
        K(g, "Ramp-8x10m", new Vector3(52, G - 1f, -51), 180f);         // Ground → deck, rising south
        K(g, "Ramp-8x10m", new Vector3(59, L5 - 1f, -68), 90f);         // Deck → Terrace roof, rising east
        Lamp(g, new Vector3(56, 3f, -68), 12f);

        // The East Block (15) on the Terrace's roof over the mezzanine: its floor is the Terrace roof, so the hatch there
        // drops from inside it onto the mezzanine (and the mezzanine's climb comes up into it)
        // (No post on its north-west corner: the ramp from the deck arrives right beside it)
        Building(g, "East Block", 64, 80, -80, -72, L10, 1, south: "WP", north: "CD", west: "W", east: "M", drop: "SWE",
                 posts: "SW SE NE");
        Lamp(g, new Vector3(72, L10 + 3f, -76), 10f);
        Lamp(g, new Vector3(48, 7, -32), 24f);
        Lamp(g, new Vector3(56, 7, 0), 20f);
    }

    // ─── West: the plateau (10) over the undercroft, the courtyard, the Watch (15) ─

    // The plateau x −72..−40, z −16..48; the courtyard (open to the sky, ground 0) x −64..−48, z −8..24
    private static readonly Vector4 Plateau = new(-72, -40, -16, 48), Courtyard = new(-64, -48, -8, 24);

    private static void BuildWest()
    {
        var g = Group("West");
        // The plateau: one big two-storey building; its ground floor is the undercroft, round the courtyard
        Building(g, "Plateau", Plateau.x, Plateau.y, Plateau.z, Plateau.w, G, 2,
                 south: "WMWP", north: "WPWM", west: "DCWNDWCQ", east: "WCDNWDCM", roof: false);
        Roof(g, Plateau.x, Plateau.y, Plateau.z, Plateau.w, L10, System.Array.Empty<Vector2>(), c => Inside(Courtyard, c) || Inside(WestPit, c));
        // The courtyard's walls stand in the undercroft, facing in: doors all round, a squeeze, a slide gap
        Building(g, "Courtyard", Courtyard.x, Courtyard.y, Courtyard.z, Courtyard.w, G, 2,
                 south: "DN", north: "DD", west: "QMMD", east: "WDGD", roof: false);
        // Up out of it: a switchback. A rises north (0 → 5) along the west wall onto a landing across the north end, B rises
        // south (5 → 10) along the east wall onto a landing joined to the plateau roof. Under B and the landings runs a
        // passage (the slide gap and the east door come into it; the north doors and a niche open off it)
        K(g, "Ramp-8x10m", new Vector3(-60, G - 1f, 9), 0f);
        foreach (float x in new[] { -60f, -52f }) foreach (float z in new[] { 16f, 20f }) K(g, "Platform-8x4m", new Vector3(x, L5 - 1f, z), 0f);
        foreach (float x in new[] { -60f, -52f }) K(g, "Platform-8x4m", new Vector3(x, L5 - 1f, 23f), 0f, new Vector3(1f, 1f, 0.5f));
        foreach (float x in new[] { -60f, -52f }) foreach (float z in new[] { 16f, 20f }) Ceiling(g, x, L5 - 1f, z, 0f, 1f, 0.5f);
        K(g, "Ramp-8x10m", new Vector3(-52, L5 - 1f, 9), 180f);
        K(g, "Platform-8x4m", new Vector3(-52, L10 - 1f, 2), 0f);
        Tunnel("Courtyard passage (under the landing)", new Vector3(-60, G, 26), new Vector3(-56, G, 15), "run");
        Tunnel("Courtyard passage (under B)", new Vector3(-52, G, 16), new Vector3(-52, G, 1), "run");
        Cubby("Under the courtyard landing", new Vector3(-56, G, 19), "standing (4 m), between the courtyard's north doors and the passage under B");
        // Curbs round the courtyard's rim up on the plateau (open where landing B joins the roof) and round the pit
        Curb(g, "Courtyard Rim", Courtyard, L10, ('E', 4f));
        Curb(g, "West Pit Curb", WestPit, G);
        Curb(g, "West Pit Rim", WestPit, L10);

        // In the undercroft: a vent run in the south corridor, a crawl platform and containers in the north hall, pillars
        foreach (float x in new[] { -60f, -52f }) K(g, "Vent-Duct-8m", new Vector3(x, G, 46), 90f);
        InsideTunnel("Undercroft vent run", new Vector3(-65f, 0, 46), new Vector3(-47f, 0, 46), "crawl");
        Cubby("Undercroft vent run", new Vector3(-56, G, 46), "prone (1.7 m), along the north wall of the dim hall");
        K(g, "Platform-Crawl-8x8m", new Vector3(-66, G, 38), 0f);
        Tunnel("Undercroft crawl south", new Vector3(-66.1f, G, 33), new Vector3(-66.1f, G, 36.8f), "crawl");
        Tunnel("Undercroft crawl north", new Vector3(-66.1f, G, 43), new Vector3(-66.1f, G, 39.2f), "crawl");
        Cubby("Undercroft crawl platform", new Vector3(-66, G, 38), "prone (1.7 m), in the dim north hall");
        Container(g, -44, G, 40, 90f);
        foreach (var p in new[] { new Vector3(-56, 0, 28), new Vector3(-66, 0, -12), new Vector3(-44, 0, -12) })
            foreach (float y in new[] { 0f, 4.5f }) K(g, "Pillar-Round-2m", p + Vector3.up * y, 0f, new Vector3(1f, 1.125f, 1f));
        Lamp(g, new Vector3(-56, 7f, -12), 16f);
        Lamp(g, new Vector3(-56, 7f, 36), 18f);
        Lamp(g, new Vector3(-68, 7f, 8), 14f);
        Lamp(g, new Vector3(-44, 7f, 8), 14f);
        Lamp(g, new Vector3(-56, 8f, 8), 20f);

        // The Watch (15): a hut-sized block on the plateau's north-west corner, a climb face and a door east
        Building(g, "Watch", -72, -64, 32, 48, L10, 1, south: "W", north: "P", west: "WP", east: "CD", drop: "WN");
        K(g, "Shed-4x4m", new Vector3(-44, L10, -12), 90f, Vector3.one * ShedScale);  // The West hut, its door west
        Tunnel("West hut door", new Vector3(-50, L10, -12), new Vector3(-45, L10, -12), "run");

        // The alley along the map edge (x −80..−72): a container stack to climb; the east yard (x −40..−32): the bridge
        // from the Centre lands at z 4; barriers
        Container(g, -76, G, 32, 90f);
        Container(g, -76, G + ContainerHeight, 32, 90f);
        Barrier(g, -36, G, 20, 90f);
        Barrier(g, -36, G, -12, 90f);

        // To the North: a deck at 10 off the plateau's north-east corner, then a long ramp down across the sky (10 → 5)
        // onto the North hall's roof
        foreach (float z in new[] { 42f, 46f }) K(g, "Platform-8x4m", new Vector3(-36, L10 - 1f, z), 0f);
        foreach (float y in new[] { 0f, 4.5f }) K(g, "Pillar-Round-2m", new Vector3(-36, y, 44), 0f, new Vector3(1f, 1.125f, 1f));
        var bridge = K(g, "Ramp-16x8m", new Vector3(-24, L5 - 1f, 44), -90f);
        PropsUnder(g, bridge, 1f, new Vector2(-3, 0), new Vector2(3, 0));
        Lamp(g, new Vector3(-36, 6, 4), 18f);
    }

    // ─── North: the North hall, the Mesa (10/15), the tower (25), the Crown (35), the Sky deck ─

    private static void BuildNorth()
    {
        var g = Group("North");
        Curb(g, "Twin Pit A Curb", TwinA, G);
        Curb(g, "Twin Pit B Curb", TwinB, G);

        // The North hall (roof 5): the ramp bridge from the West lands on its roof; a ramp down off its north side
        Building(g, "North Hall", -16, 0, 40, 56, G, 1, south: "DN", north: "XG", west: "WP", east: "DW");
        K(g, "Ramp-8x10m", new Vector3(-12, G - 1f, 61), 180f);          // Ground → roof, rising south
        Lamp(g, new Vector3(-8, 3f, 48), 12f);

        // The Mesa: a two-storey hall (roof 10) with a mezzanine (5) along its north wall and a hatch over it; on its roof
        // the Mesa top (15, one storey); on that the hollow North tower (25); on that the Crown (35).
        // Up: a hollow ramp onto the annex (5) (its room opens into the annex), a ramp on the annex onto the Mesa roof, a
        // ramp on the Mesa roof onto the Mesa top; inside the tower a staircase to its roof hatch; the Crown from a
        // scaffold, or its own inside climb to its hatch
        Building(g, "Mesa", 40, 80, 48, 80, G, 2, south: "WDNDC", north: "WPMWP", west: "DW", east: "PWMW",
                 hatches: new[] { new Vector2(52, 76) }, posts: "SW SE NE");
        // West face beside the annex: lower panels between the annex and the hall, upper panels on the annex roof (none
        // where the ramp from the annex roof arrives)
        Wall(g, "Archway-8m", 40, G, 68, W); Wall(g, "Wall-SlideGap-8m", 40, G, 76, W);
        Wall(g, UpperWall(), 40, L5, 68, W);
        Tunnel("Annex to Mesa hall", new Vector3(38, G, 68), new Vector3(42, G, 68), "run");
        Tunnel("Annex slide gap", new Vector3(38, G, 76), new Vector3(42, G, 76), "crawl");
        Cubby("Annex slide gap", new Vector3(40, G, 76), "prone/slide, the annex into the Mesa hall");
        Mezzanine(g, 40, 80, 76, 80, L5, 'S');
        K(g, "Stairs-8x10m", new Vector3(60, G - 1f, 71), 0f);
        Container(g, 44, L5, 78, 0f);                                     // On the mezzanine: a jump to the hatch's edge
        Lamp(g, new Vector3(60, 7f, 60), 22f);

        Building(g, "Annex", 24, 40, 64, 80, G, 1, south: "XX", north: "WP", west: "DN", east: null, posts: "SW NW");
        // Its south face: a window panel cut to 7 m, then the hollow ramp's mouth (the ramp stands clear of the Mesa's wall)
        K(g, "WallWindow-8m", new Vector3(27.5f, G, 64), 180f, new Vector3(7f / 8f, 1f, 1f));
        K(g, "Ramp-Hollow-8x10m", new Vector3(35, G, 59), 180f);           // Ground → annex roof, rising north
        InsideTunnel("Under the annex ramp", new Vector3(35, 0, 59.5f), new Vector3(35, 0, 66f));
        Cubby("Under the annex ramp", new Vector3(35, G, 62), "standing near its mouth (3.3–4.3 m), opens into the annex");
        K(g, "Ramp-8x10m", new Vector3(35, L5 - 1f, 76), 90f);             // Annex roof → Mesa roof, rising east
        Lamp(g, new Vector3(32, 3f, 72), 12f);

        Building(g, "Mesa Top", 56, 80, 56, 80, L10, 1, south: "WDC", north: "WPW", west: "DXN", east: "PWP", drop: "NE");
        K(g, "Ramp-8x10m", new Vector3(51, L10 - 1f, 68), 90f);            // Mesa roof → Mesa top, rising east
        Lamp(g, new Vector3(64, L10 + 3f, 64), 14f);

        // The North tower: 16 × 16 on the Mesa top, one row of tall walls (15..24) with a door west; roof 25 with a hatch;
        // inside, two 3 m steps under it
        Building(g, "North Tower", 64, 80, 64, 80, L15, 2, south: "SW", north: "SS", west: "DS", east: "WS",
                 hatches: new[] { new Vector2(68, 76) }, drop: "NE");
        K(g, "Block-4x4x4m", new Vector3(74, L15, 78), 0f, new Vector3(1f, 0.75f, 1f));
        for (int h = 0; h < 2; h++) K(g, "Block-4x4x4m", new Vector3(68, L15 + 3f * h, 78), 0f, new Vector3(1f, 0.75f, 1f));
        Lamp(g, new Vector3(72, L15 + 6f, 72), 14f);

        // The Crown (35): on the tower's east half; a door from the tower roof, inside two steps to its own hatch; a
        // lookout hut on top
        Building(g, "Crown", 72, 80, 64, 80, L25, 2, south: "W", north: "S", west: "DS", east: "WS",
                 hatches: new[] { new Vector2(76, 76) }, drop: "SNE");
        K(g, "Block-4x4x4m", new Vector3(78, L25, 66), 0f, new Vector3(1f, 0.75f, 1f));
        for (int h = 0; h < 2; h++) K(g, "Block-4x4x4m", new Vector3(76, L25 + 3f * h, 74), 0f, new Vector3(1f, 0.75f, 1f));
        K(g, "Shed-4x4m", new Vector3(76, L35, 68), 180f, Vector3.one * ShedScale);   // The Lookout, its door north
        Lamp(g, new Vector3(76, L35 + 6f, 72), 18f);

        // The Sky deck (35), floating south of the Mesa; the Sky lift from the ground; a catwalk round to the Crown with
        // one hop at the end; a zip line from the deck back across to the West plateau
        var sky = K(g, "Platform-16x16m", new Vector3(32, L35 - 1f, 44), 0f);
        PropsUnder(g, sky, 1.4f, new Vector2(4.5f, 4.5f), new Vector2(-4.5f, 4.5f), new Vector2(4.5f, -4.5f), new Vector2(-4.5f, -4.5f));
        KitMover(g, "Sky Lift", new Vector3(22, G, 44), new Vector3(22, L35 - 1f, 44), 7f);
        // (A corner piece spans its pivot −3..+1 along its arms: the corner square is the 2 × 2 round the pivot)
        foreach (float x in new[] { 44f, 52f }) PropsUnder(g, K(g, "Catwalk-8x2m", new Vector3(x, L35 - 1f, 44), 0f), 0.8f, Vector2.zero);
        K(g, "Catwalk-Corner-4x4m", new Vector3(59, L35 - 1f, 44), -90f);   // Arms west and north
        foreach (float z in new[] { 51f, 59f, 67f }) PropsUnder(g, K(g, "Catwalk-8x2m", new Vector3(59, L35 - 1f, z), 90f), 0.8f, Vector2.zero);
        K(g, "Catwalk-Corner-4x4m", new Vector3(59, L35 - 1f, 74), 90f);    // Arms south and east
        K(g, "Catwalk-8x2m", new Vector3(64.5f, L35 - 1f, 74), 0f, new Vector3(5f / 8f, 1f, 1f));
        gaps.Add(("Sky bridge hop to the Crown", new Vector3(67, L35, 74), new Vector3(71.5f, L35, 74)));
        ZipLineBetween(g, new Vector3(27, L35, 49), new Vector3(-58, L10, 45));
        Lamp(g, new Vector3(32, L35 + 5f, 44), 18f);

        // The open yard: two crawl platforms on the north-west, a container tunnel by the lift
        foreach (float x in new[] { 0f, 16f })
        {
            K(g, "Platform-Crawl-8x8m", new Vector3(x, G, 72), 0f);
            Tunnel($"North crawl {x:0} south way in", new Vector3(x - 0.1f, G, 67), new Vector3(x - 0.1f, G, 70.8f), "crawl");
            Cubby("Crawl platform", new Vector3(x, G, 72), "prone (1.7 m), open at both ends");
        }
        K(g, "CargoContainer-Tunnel-8m", new Vector3(32, G, 38), 0f);
        InsideTunnel("North container", new Vector3(27, 0, 38), new Vector3(37, 0, 38));
        Barrier(g, -6, G, 66, 90f);
        Lamp(g, new Vector3(8, 7, 56), 24f);
        Lamp(g, new Vector3(60, 7, 40), 20f);
    }

    // ─── Bridges, the jumps, the high bridge ──────────────────────────────────

    private static void BuildCrossings()
    {
        var g = Group("Crossings");
        // Rim to rim (14 m): one railed bridge stretched to fit
        BridgeAlongZ(g, -4, -31, -17, G);    // South ↔ Centre (the x −4 line through the Spire)
        BridgeAlongX(g, 4, 17, 31, G);       // Centre ↔ East (the z 4 line through the Spire and the Bunker)
        BridgeAlongX(g, 4, -31, -17, G);     // Centre ↔ West (into the plateau's east door)
        BridgeAlongZ(g, 56, 17, 31, G);      // East ↔ North
        BridgeAlongX(g, -60, 17, 31, G);     // South ↔ East
        // South Bluff roof ↔ West plateau roof, at 10 (wall top to wall top)
        foreach (float z in new[] { -28f, -20f }) PropsUnder(g, K(g, "Bridge-8x4m", new Vector3(-68, L10 - 1f, z), 90f), 1f, Vector2.zero);
        // The jumps: a platform jutting from one side leaves 6 m to the other rim
        PropsUnder(g, K(g, "Platform-8x4m", new Vector3(-4, G - 1f, 21), 90f), 1f, Vector2.zero);
        gaps.Add(("Centre to North (jump)", new Vector3(-4, G, 25), new Vector3(-4, G, 31)));
        PropsUnder(g, K(g, "Platform-8x4m", new Vector3(21, G - 1f, -40), 0f), 1f, Vector2.zero);
        gaps.Add(("South to East (jump)", new Vector3(25, G, -40), new Vector3(31, G, -40)));
    }

    // ─── Spawns ───────────────────────────────────────────────────────────────

    private static readonly (string name, float x, float z, float y)[] SpawnSpots =
    {
        ("Centre South-West", -12, -12, G),
        ("Centre North-East",  12,  12, G),
        ("South Yard",        -28, -60, G),
        ("South East",          2, -76, G),
        ("Bluff Mezzanine",   -72, -36, L5),
        ("Bluff Deck",        -52, -40, L5),
        ("East Yard",          40, -12, G),
        ("East Containers",    62, -32, G),
        ("Terrace Roof",       77, -58, L10),
        ("Terrace Deck",       56, -76, L5),
        ("Courtyard",         -56,  -4, G),
        ("Undercroft North",  -60,  30, G),
        ("Plateau Roof",      -44,  28, L10),
        ("West Alley",        -76,   4, G),
        ("North Yard",          0,  60, G),
        ("North Lift",         30,  44, G),
        ("Mesa Hall",          70,  60, G),
        ("Mesa Roof",          48,  52, L10),
        ("Annex",              30,  72, G),
        ("Sky Deck",           32,  40, L35),
    };

    private static void RenderOverviews()
    {
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(6f, true, true);
        Shot("fracture_top", new Vector3(0, 200, 0), Quaternion.Euler(90, 0, 0), true, 92f);
        Shot("fracture_sw", new Vector3(-120, 80, -130), Quaternion.LookRotation(new Vector3(120, -75, 130)), false, 0f);
        Shot("fracture_ne", new Vector3(125, 90, 120), Quaternion.LookRotation(new Vector3(-125, -85, -120)), false, 0f);
        Shot("fracture_spire", new Vector3(-24, 12, -30), Quaternion.LookRotation(new Vector3(24, 6, 30)), false, 0f);
        Shot("fracture_south", new Vector3(10, 18, -20), Quaternion.LookRotation(new Vector3(-40, -14, -40)), false, 0f);
        Shot("fracture_courtyard", new Vector3(-56, 18, -14), Quaternion.LookRotation(new Vector3(0, -12, 22)), false, 0f);
        Shot("fracture_west", new Vector3(-10, 22, -6), Quaternion.LookRotation(new Vector3(-40, -12, 20)), false, 0f);
        Shot("fracture_mesa", new Vector3(20, 22, 20), Quaternion.LookRotation(new Vector3(40, 4, 40)), false, 0f);
        Shot("fracture_east", new Vector3(20, 22, 0), Quaternion.LookRotation(new Vector3(30, -12, -50)), false, 0f);
        Shot("fracture_under", new Vector3(140, -110, 120), Quaternion.LookRotation(new Vector3(-130, 90, -110)), false, 0f);
    }
}
