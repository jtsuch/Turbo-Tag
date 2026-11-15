using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using static GreyboxKit;
using static KitLevel;

/// <summary>
/// Builds Stairfall (Assets/Scenes/Levels/Stairfall.unity): five floating islands, each one storey (5 m) above or
/// below its neighbours, a staircase of islands round the Old Town. Built on the Fracture model (CLAUDE.md, "Fracture is
/// the model map"), and more cluttered with structures: blocks of buildings with 8 m alleys between them, wall-run
/// panels down the long alley faces, rooftops as a second street level.
///
///   Old Town (0, centre): four blocks round a plaza (a pavilion, beams across the alleys at 5), a gate lane, a north
///     row. Underneath, the Sewers (−5): corridors and a hide room, in by the Pump Hall's stairs and a pit with a
///     bounce pad, out south through the cliff onto a pier to the Docks.
///   Docks (−5, south): open quays, the Fish Market and the Net Shed, containers, crawl platforms.
///   Works (+5, west): warehouses (roofs 10 and 15) along a wall-run alley.
///   Terraces (+5, east): halls (roofs 10 and 15) along a wall-run alley, a mezzanine.
///   Heights (+10, north): tall halls (roofs 15 and 20), the hollow Tower (roof 25: zip lines down to the Docks and
///     across to the Works). Underneath, the Vaults (+5), in by the North Hall's stairs and an alley pit, out south
///     through the cliff onto a pier to the Works.
/// Neighbours one storey apart are joined by ramp bridges (a 16 m ramp across each 16 m rift).
///
/// Re-running rebuilds Geometry/Stairfall; review output goes to Library/TurboTag/scratch/stairfall_report.txt and
/// stairfall_*.png. Menu: Turbo Tag/Levels/Rebuild Stairfall (editor job: BuildStairfall).
/// </summary>
public static class StairfallBuilder
{
    private const string ScenePath  = "Assets/Scenes/Levels/Stairfall.unity";
    private const string RootName   = "Stairfall";
    private const string ReportPath = "Library/TurboTag/scratch/stairfall_report.txt";

    private const float D = -5f, G = 0f, L5 = 5f, L10 = 10f, L15 = 15f, L20 = 20f, L25 = 25f;

    // Islands (x0, x1, z0, z1) and their ground heights
    private static readonly Vector4 OldTown  = new(-32, 32, -32, 24);
    private static readonly Vector4 Docks    = new(-40, 40, -80, -48);
    private static readonly Vector4 Works    = new(-88, -48, -40, 24);
    private static readonly Vector4 Terraces = new(48, 88, -32, 24);
    private static readonly Vector4 Heights  = new(-64, 64, 40, 80);

    // Undergrounds
    private static readonly Vector4 Sewers = new(-24, 24, -24, 8);     // −5, under the Old Town
    private static readonly Vector4 Vaults = new(-56, -8, 48, 72);     // +5, under the Heights
    private static readonly Vector2 SewerPassage = new(12, -28), VaultPassage = new(-52, 44);
    // Holes in the ground into them
    private static readonly Vector2 PumpStairs = new(-20, -20), SewerPit = new(12, -12);
    private static readonly Vector2 HallStairs = new(-52, 60), VaultPit = new(-36, 52);
    // The cells beside the stairwells, floored 6 m short so the whole flight sits under the opening (full headroom)
    private static readonly Vector2 PumpLanding = new(-20, -28), HallLanding = new(-52, 68);

    [MenuItem("Turbo Tag/Levels/Rebuild Stairfall")]
    public static void Build()
    {
        Reset(20261114, "Assets/Environment/Generated/Stairfall_Island.asset", "Stairfall Island", G);
        if (!System.IO.File.Exists(ScenePath)) AssetDatabase.CopyAsset("Assets/Scenes/Levels/Fracture.unity", ScenePath);
        var scene = Begin(ScenePath, RootName);
        var geometry = root.parent;
        for (int i = geometry.childCount - 1; i >= 0; i--)
            if (geometry.GetChild(i) != root) Object.DestroyImmediate(geometry.GetChild(i).gameObject);

        BuildGround();
        BuildOldTown();
        BuildSewers();
        BuildDocks();
        BuildWorks();
        BuildTerraces();
        BuildHeights();
        BuildVaults();
        BuildCrossings();
        BuildIsland();
        CheckAccess();
        Headroom("Pump Hall stairs", PumpStairs.x, -14f, D, -26f, G);
        Headroom("North Hall stairs", HallStairs.x, 54f, L5, 66f, L10);
        report.Add($"cubbies: {cubbies.Count}");
        foreach (var c in cubbies) report.Add("  " + c);
        CheckOverlaps();
        Bounds(new Vector3(0, 0, 0), 420f, -120f, 80f);
        Spawns(SpawnSpots, new Vector3(0, 0, -4));
        CheckSpawnSpacing(SpawnSpots);
        PlaceSpawner(new Vector3(4, 0.1f, -20), -110f);
        Atmosphere.SunnyDay(root, new Vector3(1f, 0f, 0.35f), new Bounds(new Vector3(0, 10, 0), new Vector3(190, 40, 170)));

        Finish(scene, ReportPath, "Stairfall", RenderOverviews);
    }

    // ─── Ground ───────────────────────────────────────────────────────────────

    private static void BuildGround()
    {
        var g = Group("Ground");
        Ground(g, OldTown, G, c => c == PumpStairs || c == SewerPit || c == PumpLanding);
        K(g, "Platform-8x4m", new Vector3(PumpLanding.x, G - 1f, -29f), 0f, new Vector3(1f, 1f, 1.5f));    // z −32..−26
        K(g, "Floor-Hole-8m", new Vector3(SewerPit.x, G - 1f, SewerPit.y), 0f);
        Ground(g, Docks, D, c => false);
        Ground(g, Works, L5, c => false);
        Ground(g, Terraces, L5, c => false);
        Ground(g, Heights, L10, c => c == HallStairs || c == VaultPit || c == HallLanding);
        K(g, "Platform-8x4m", new Vector3(HallLanding.x, L10 - 1f, 69f), 0f, new Vector3(1f, 1f, 1.5f));    // z 66..72
    }

    /// <summary>An island's ground: big plain slabs at <paramref name="top"/>; every cell (holes into an underground
    /// too) is ground the floating island carries, at that height.</summary>
    private static void Ground(Transform g, Vector4 r, float top, System.Func<Vector2, bool> hole)
    {
        var cells = new HashSet<Vector2>();
        for (float x = r.x + 4; x < r.y; x += 8)
            for (float z = r.z + 4; z < r.w; z += 8)
            {
                var c = new Vector2(x, z);
                RecordGround(CellOf(x, z), top);
                if (!hole(c)) cells.Add(c);
            }
        Fill(g, cells, null, top, false, r.x, r.z);
    }

    // ─── Old Town (0) ─────────────────────────────────────────────────────────

    // Four blocks round the plaza (x −8..8, z −8..8), alleys between them (x −16..−8 and 8..16; z −16..−8 and 8..16), a
    // gate lane (x 0..8) from the Docks' ramp, a north row along the rim
    private static void BuildOldTown()
    {
        var g = Group("Old Town");
        // The Pump Hall (roof 10): stairs inside down to the Sewers; wall-run panels down its alley face
        Building(g, "Pump Hall", -32, -16, -32, -16, G, 2, south: "WP", north: "RR", west: "WM", east: "DN");
        // Rising south from the Sewers (z −16, −5) up into the hall (z −26, 0), all under the opening (z −26..−16)
        K(g, "Stairs-8x10m", new Vector3(PumpStairs.x, D - 1f, -21f), 180f);

        Cubby("Pump Hall stairs", new Vector3(PumpStairs.x, D, PumpStairs.y), "down into the Sewers");
        Lamp(g, new Vector3(-24, 6f, -24), 14f);
        // The Chapel (roof 10), mirrored across the town
        Building(g, "Chapel", 16, 32, -32, -16, G, 2, south: "WP", north: "RR", west: "ND", east: "PW");
        Lamp(g, new Vector3(24, 6f, -24), 14f);
        // Row houses (roof 5) north of them, mirrored: wall-run panels on their alley faces, a squeeze each
        Building(g, "Row House W", -32, -16, -8, 8, G, 1, south: "RD", north: "DW", west: "WW", east: "QD");
        Building(g, "Row House E", 16, 32, -8, 8, G, 1, south: "RD", north: "DW", west: "DQ", east: "WW");
        // The gate house beside the gate lane (roof 5)
        Building(g, "Gate House", -8, 0, -32, -24, G, 1, south: "W", north: "D", west: "D", east: "C");
        // The north row (roofs 5 and 10), the north street between them
        Building(g, "North Row W", -32, -8, 16, 24, G, 1, south: "DRW", north: "WPW", west: "W", east: "D");
        Building(g, "North Row E", 8, 32, 16, 24, G, 2, south: "WRD", north: "PWP", west: "D", east: "W");

        // The plaza: a pavilion (deck 5, its ramp down south into the alley), beams across the alleys at 5 from the
        // row houses' roofs to its deck: a rooftop line across the town
        K(g, "Struct-Pavilion-16x16m", new Vector3(0, G, 0), 180f);
        foreach (float x in new[] { -12f, 12f }) K(g, "Beam-8m", new Vector3(x, L5 - 1f, 0), 0f);
        // The pit into the Sewers at the east alley crossing: a curb, a bounce pad below
        Curb(g, "Sewer Pit Curb", new Vector4(SewerPit.x - 2f, SewerPit.x + 2f, SewerPit.y - 2f, SewerPit.y + 2f), G);
        Bounce(g, new Vector3(SewerPit.x, D, SewerPit.y), 34f);
        Cubby("Sewer pit", new Vector3(SewerPit.x, D, SewerPit.y), "a drop into the Sewers; the bounce pad throws you back up");
        // Cover in the lanes: barriers in pairs
        foreach (float x in new[] { -28f, 28f }) Barrier(g, x, G, 12, 0f);
        Lamp(g, new Vector3(0, 9f, -12), 24f);
        Lamp(g, new Vector3(0, 7f, 20), 18f);
    }

    // ─── Sewers (−5) ──────────────────────────────────────────────────────────

    private static void BuildSewers()
    {
        var g = Group("Sewers");
        var cells = new List<Vector2>();
        for (float x = Sewers.x + 4; x < Sewers.y; x += 8)
            for (float z = Sewers.z + 4; z < Sewers.w; z += 8)
                cells.Add(new Vector2(x, z));
        cells.Add(SewerPassage);
        var blockA = new Vector4(-16, -8, -8, 0);   // A hide room: a squeeze in, a slide gap out
        var blockB = new Vector4(8, 16, -8, 0);     // Solid
        foreach (var c in cells)
        {
            Tile(g, "Floor-Grate-8m", c.x, c.y, D);
            if (c != PumpStairs && !Inside(blockB, c)) Ceiling(g, c.x, G - 1f, c.y);
        }
        Building(g, "Sewer Walls", Sewers.x, Sewers.y, Sewers.z, Sewers.w, D, 1,
                 south: "XMSSXS", north: "MPMPMP", west: "PMPM", east: "MPMP", roof: false);
        Cut(g, "Wall-Mid-8m", SewerPassage.x - 4, D, -28.5f, W, 7f);
        Cut(g, "Wall-Mid-8m", SewerPassage.x + 4, D, -28.5f, E, 7f);
        Tunnel("Sewer passage", new Vector3(SewerPassage.x, D, -18), new Vector3(SewerPassage.x, D, -44), "run");
        Building(g, "Sewer Hide Room", blockA.x, blockA.y, blockA.z, blockA.w, D, 1, south: "M", north: "M", west: "G", east: "Q", roof: false);
        Cubby("Sewer hide room", new Vector3(-12, D, -4), "standing, dark, a squeeze in and a slide gap out");
        Building(g, "Sewer Block", blockB.x, blockB.y, blockB.z, blockB.w, D, 1, south: "P", north: "M", west: "M", east: "P", roof: false);
        foreach (var p in new[] { new Vector3(-20, D + 3f, -4), new Vector3(0, D + 3f, -16), new Vector3(20, D + 3f, -16),
                                  new Vector3(0, D + 3f, 4), new Vector3(12, D + 3f, -27), new Vector3(-12, D + 3f, 0) })
            Lamp(g, p, 12f);
    }

    // ─── Docks (−5) ───────────────────────────────────────────────────────────

    private static void BuildDocks()
    {
        var g = Group("Docks");
        Building(g, "Fish Market", -40, -16, -80, -64, D, 1, south: "WPW", north: "DND", west: "WW", east: "DW");
        Building(g, "Net Shed", 24, 40, -80, -64, D, 1, south: "WP", north: "DW", west: "QD", east: "WW");
        Lamp(g, new Vector3(-28, D + 3f, -72), 12f);
        Lamp(g, new Vector3(32, D + 3f, -72), 12f);
        // Crawl platforms mirrored about the middle, containers on the quays, barriers
        foreach (float x in new[] { -4f, 12f })
        {
            K(g, "Platform-Crawl-8x8m", new Vector3(x, D, -72), 0f);
            Tunnel($"Dock crawl {x:0}", new Vector3(x - 0.1f, D, -67), new Vector3(x - 0.1f, D, -70.8f), "crawl");
            Cubby("Crawl platform", new Vector3(x, D, -72), "prone (1.7 m), open at both ends");
        }
        foreach (float x in new[] { -28f, 28f }) Container(g, x, D, -54, 0f);
        foreach (float x in new[] { -28f, 28f }) Container(g, x, D + ContainerHeight, -54, 0f);
        K(g, "CargoContainer-Tunnel-8m", new Vector3(-12, D, -56), 90f);
        InsideTunnel("Dock container", new Vector3(-12, D, -61), new Vector3(-12, D, -51));
        Cubby("Container tunnel", new Vector3(-12, D, -56), "standing, open at both ends");
        Lamp(g, new Vector3(4, D + 8f, -64), 24f);
    }

    // ─── Works (+5) ───────────────────────────────────────────────────────────

    // Warehouses either side of a long alley (x −72..−64), wall-run panels down both its faces; a yard on the east
    // (where the Old Town's ramp arrives) and on the north (the Heights ramp, the Vaults' pier)
    private static void BuildWorks()
    {
        var g = Group("Works");
        Building(g, "Works Hall S", -88, -72, -40, -16, L5, 2, south: "WP", north: "DR", west: "PWP", east: "RRD");
        Building(g, "Works Hall N", -88, -72, -8, 16, L5, 2, south: "DR", north: "DW", west: "WPW", east: "RRD");
        Building(g, "Works Store", -64, -48, -40, -24, L5, 1, south: "WP", north: "DW", west: "RQ", east: "WW");
        Building(g, "Works Shop", -64, -56, -8, 8, L5, 1, south: "D", north: "W", west: "RR", east: "DW");
        // Up the halls: a mezzanine in the north hall, a container under its roof hatch
        Mezzanine(g, -88, -72, 8, 16, L10, 'S');
        K(g, "Stairs-8x10m", new Vector3(-84, L5 - 1f, 3), 0f);     // Rising north onto it (top z 8)
        // Cover in the yards
        Barrier(g, -52, L5, -28, 90f);
        K(g, "Platform-Crawl-8x8m", new Vector3(-52, L5, 16), 0f);
        Tunnel("Works crawl", new Vector3(-52.1f, L5, 11), new Vector3(-52.1f, L5, 14.8f), "crawl");
        Cubby("Works crawl platform", new Vector3(-52, L5, 16), "prone (1.7 m), open at both ends");
        K(g, "Scaffold-4x4x5m", new Vector3(-52, L5, -21), 0f);      // Up onto the store's roof (10)
        Lamp(g, new Vector3(-68, L5 + 8f, -12), 22f);
        Lamp(g, new Vector3(-80, L5 + 6f, 4), 16f);
    }

    // ─── Terraces (+5) ────────────────────────────────────────────────────────

    private static void BuildTerraces()
    {
        var g = Group("Terraces");
        Building(g, "Terrace Hall S", 72, 88, -32, -8, L5, 2, south: "WP", north: "DW", west: "RRD", east: "PWP");
        Building(g, "Terrace Hall N", 72, 88, 0, 24, L5, 2, south: "DW", north: "WP", west: "RRD", east: "PWP");
        Building(g, "Terrace Store", 48, 64, -32, -16, L5, 1, south: "WP", north: "WD", west: "WW", east: "RQ");
        Building(g, "Terrace Shop", 48, 64, 0, 16, L5, 1, south: "DW", north: "WD", west: "WW", east: "RR");
        // A mezzanine along the south hall's south wall, stairs up to it
        Mezzanine(g, 72, 88, -32, -24, L10, 'N');
        K(g, "Stairs-8x10m", new Vector3(84, L5 - 1f, -19), 180f);
        K(g, "Scaffold-4x4x5m", new Vector3(52, L5, -13), 0f);       // Up onto the store's roof (10)
        K(g, "Platform-Crawl-8x8m", new Vector3(60, L5, -8), 0f);
        Tunnel("Terrace crawl", new Vector3(59.9f, L5, -13), new Vector3(59.9f, L5, -9.2f), "crawl");
        Cubby("Terrace crawl platform", new Vector3(60, L5, -8), "prone (1.7 m), open at both ends");
        Lamp(g, new Vector3(68, L5 + 8f, -4), 22f);
        Lamp(g, new Vector3(80, L5 + 6f, -20), 16f);
    }

    // ─── Heights (+10) ────────────────────────────────────────────────────────

    private static void BuildHeights()
    {
        var g = Group("Heights");
        // The North Hall (roof 20): stairs inside down to the Vaults
        Building(g, "North Hall", -64, -40, 56, 80, L10, 2, south: "DRW", north: "PWP", west: "WPW", east: "DRW");
        // Rising north from the Vaults (z 56, 5) up into the hall (z 66, 10), all under the opening (z 56..66)
        K(g, "Stairs-8x10m", new Vector3(HallStairs.x, L5 - 1f, 61f), 0f);

        Cubby("North Hall stairs", new Vector3(HallStairs.x, L5, HallStairs.y), "down into the Vaults");
        Building(g, "East Hall", 40, 64, 56, 80, L10, 2, south: "WRD", north: "PWP", west: "DRW", east: "WPW");
        Building(g, "Watch House W", -32, -16, 48, 64, L10, 1, south: "DW", north: "WD", west: "RR", east: "DW");
        Building(g, "Watch House E", 16, 32, 48, 64, L10, 1, south: "WD", north: "DW", west: "WD", east: "RR");
        Lamp(g, new Vector3(-52, L10 + 6f, 68), 16f);
        Lamp(g, new Vector3(52, L10 + 6f, 68), 16f);

        // The Tower: 16 × 16, three storeys (10..24, Wall-Mid and bands), a door south, a staircase of 3 m steps up to a
        // hatch in its roof (25); from the roof, zip lines down to the Docks and across to the Works
        const float tx0 = -8, tx1 = 8, tz0 = 56, tz1 = 72;
        Face(g, "Tower S", 'S', tz0, tx0, L10, "DSS", "SWS");
        Face(g, "Tower N", 'N', tz1, tx0, L10, "SWS", "SSS");
        Face(g, "Tower W", 'W', tx0, tz0, L10, "SSS", "SWS");
        Face(g, "Tower E", 'E', tx1, tz0, L10, "SWS", "SSS");
        TallPost(g, tx0, tz0, W, S, L10); TallPost(g, tx1, tz0, E, S, L10);
        TallPost(g, tx0, tz1, W, N, L10); TallPost(g, tx1, tz1, E, N, L10);
        Roof(g, tx0, tx1, tz0, tz1, L25, new[] { new Vector2(-4, 68) });
        Cubby("Tower roof hatch", new Vector3(-4, L25, 68), "a drop in, the staircase back up");
        var steps = new[] { new Vector2(6, 62), new Vector2(6, 66), new Vector2(2, 70) };
        for (int i = 0; i < steps.Length; i++)
            for (int h = 0; h <= i; h++)
                K(g, "Block-4x4x4m", new Vector3(steps[i].x, L10 + 3f * h, steps[i].y), 0f, new Vector3(1f, 0.75f, 1f));
        Tunnel("Tower top step (standing)", new Vector3(2.5f, L10 + 9f, 70), new Vector3(1.5f, L10 + 9f, 70), "run");
        ZipLineBetween(g, new Vector3(4, L25, 58.5f), new Vector3(4, D, -62));
        ZipLineBetween(g, new Vector3(-6, L25, 62), new Vector3(-80, L15, 0));
        Lamp(g, new Vector3(0, L10 + 7f, 64), 14f);

        // The pit into the Vaults in the alley between the North Hall and the west watch house
        // (a hole tile: the watch house's wall stands along the cell's east edge)
        K(g, "Floor-Hole-8m", new Vector3(VaultPit.x, L10 - 1f, VaultPit.y), 0f);
        Curb(g, "Vault Pit Curb", new Vector4(VaultPit.x - 2.05f, VaultPit.x + 2.05f, VaultPit.y - 2.05f, VaultPit.y + 2.05f), L10);
        Bounce(g, new Vector3(VaultPit.x, L5, VaultPit.y), 34f);
        Cubby("Vault pit", new Vector3(VaultPit.x, L5, VaultPit.y), "a drop into the Vaults; the bounce pad throws you back up");
        foreach (float x in new[] { -28f, 28f }) Barrier(g, x, L10, 72, 0f);
        foreach (float side in new[] { -1f, 1f })
        {
            foreach (float x in new[] { 24f, 32f }) K(g, "Vent-Duct-8m", new Vector3(side * x, L10, 44), 90f);
            InsideTunnel($"Heights vent run {side:0}", new Vector3(side * 19f, L10, 44), new Vector3(side * 37f, L10, 44), "crawl");
            Cubby("Vent run", new Vector3(side * 28, L10, 44), "prone (1.7 m), open at both ends");
        }
        // The overpass along the south strip (deck 15, ramps down at both ends): a raised street in front of the Tower
        K(g, "Struct-Overpass-32x8m", new Vector3(0, L10, 44), 0f, new Vector3(30f / 32f, 1f, 1f));   // Clear of the watch houses' corners
        Lamp(g, new Vector3(0, L10 + 8f, 48), 24f);
    }

    // ─── Vaults (+5) ──────────────────────────────────────────────────────────

    private static void BuildVaults()
    {
        var g = Group("Vaults");
        var cells = new List<Vector2>();
        for (float x = Vaults.x + 4; x < Vaults.y; x += 8)
            for (float z = Vaults.z + 4; z < Vaults.w; z += 8)
                cells.Add(new Vector2(x, z));
        cells.Add(VaultPassage);
        var room = new Vector4(-32, -16, 56, 64);   // A hide room
        foreach (var c in cells)
        {
            Tile(g, "Floor-Grate-8m", c.x, c.y, L5);
            if (c != HallStairs && c != HallLanding) Ceiling(g, c.x, L10 - 1f, c.y);
        }
        Building(g, "Vault Walls", Vaults.x, Vaults.y, Vaults.z, Vaults.w, L5, 1,
                 south: "XSMPMP", north: "PMPMPM", west: "MPM", east: "PMP", roof: false);
        Cut(g, "Wall-Mid-8m", VaultPassage.x - 4, L5, 43.5f, W, 7f);
        Cut(g, "Wall-Mid-8m", VaultPassage.x + 4, L5, 43.5f, E, 7f);
        Tunnel("Vault passage", new Vector3(VaultPassage.x, L5, 54), new Vector3(VaultPassage.x, L5, 28), "run");
        Building(g, "Vault Hide Room", room.x, room.y, room.z, room.w, L5, 1, south: "QM", north: "MG", west: "M", east: "M", roof: false);
        Cubby("Vault hide room", new Vector3(-24, L5, 60), "standing, dark, a squeeze in and a slide gap out");
        foreach (var p in new[] { new Vector3(-44, L5 + 3f, 52), new Vector3(-28, L5 + 3f, 52), new Vector3(-12, L5 + 3f, 64),
                                  new Vector3(-48, L5 + 3f, 68), new Vector3(-24, L5 + 3f, 64), new Vector3(-52, L5 + 3f, 44) })
            Lamp(g, p, 12f);
    }

    /// <summary>
    /// The clear height above a flight of stairs (a capsule test drags on the slope): at points from (z0, y0) to (z1, y1)
    /// the stair surface is found below the line and the first thing above it measured. Anything under 3.2 m (a 2.9 m
    /// penguin with a little room) is reported.
    /// </summary>
    private static void Headroom(string name, float x, float z0, float y0, float z1, float y1)
    {
        Physics.SyncTransforms();
        float least = float.MaxValue;
        for (int i = 0; i <= 12; i++)
        {
            float t = i / 12f;
            var at = new Vector3(x, Mathf.Lerp(y0, y1, t) + 1.5f, Mathf.Lerp(z0, z1, t));
            if (!Physics.Raycast(at, Vector3.down, out RaycastHit floor, 4f, ~0, QueryTriggerInteraction.Ignore)) continue;
            float room = Physics.Raycast(floor.point + Vector3.up * 0.05f, Vector3.up, out RaycastHit roof, 20f, ~0, QueryTriggerInteraction.Ignore)
                ? roof.distance + 0.05f : 20f;
            least = Mathf.Min(least, room);
        }
        report.Add(least < 3.2f ? $"! {name}: only {least:0.0} m of headroom" : $"{name}: at least {least:0.0} m of headroom");
    }

    // ─── Crossings ────────────────────────────────────────────────────────────

    // A ramp bridge across a 16 m rift (14 m rim to rim), rising one storey from the lower island to the higher
    private static void RampBridge(Transform g, Vector3 lowRim, Vector3 highRim, float lowTop)
    {
        Vector3 mid = (lowRim + highRim) / 2f;
        Vector3 d = highRim - lowRim;
        float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        var ramp = K(g, "Ramp-16x8m", new Vector3(mid.x, lowTop - 1f, mid.z), yaw, new Vector3(1f, 1f, new Vector2(d.x, d.z).magnitude / 16f));
        PropsUnder(g, ramp, 1f, new Vector2(0, 0));
    }

    private static void BuildCrossings()
    {
        var g = Group("Crossings");
        RampBridge(g, new Vector3(-33, 0, -12), new Vector3(-47, 0, -12), G);    // Old Town → Works
        RampBridge(g, new Vector3(33, 0, -12), new Vector3(47, 0, -12), G);      // Old Town → Terraces
        RampBridge(g, new Vector3(4, 0, -47), new Vector3(4, 0, -33), D);        // Docks → Old Town
        RampBridge(g, new Vector3(-60, 0, 25), new Vector3(-60, 0, 39), L5);     // Works → Heights
        RampBridge(g, new Vector3(60, 0, 25), new Vector3(60, 0, 39), L5);       // Terraces → Heights

        // The Sewers' passage out through the Old Town's south cliff onto a pier to the Docks
        K(g, "Tunnel-8x8m", new Vector3(SewerPassage.x, D - 1f, -36), 0f);
        K(g, "Skirt-Wall-8m", new Vector3(SewerPassage.x, SkirtBottom, -32.5f), 180f, new Vector3(1f, (D - 1f - SkirtBottom) / 16f, 1f));
        PropsUnder(g, K(g, "Bridge-8x4m", new Vector3(SewerPassage.x, D - 1f, -43.5f), 90f, new Vector3(7f / 8f, 1f, 1f)), 1f, Vector2.zero);
        skirtOpenings.Add((CellOf(SewerPassage.x, SewerPassage.y), Vector2Int.down));
        // The Vaults' passage out through the Heights' south cliff onto a pier to the Works
        K(g, "Tunnel-8x8m", new Vector3(VaultPassage.x, L5 - 1f, 36), 0f);
        K(g, "Skirt-Wall-8m", new Vector3(VaultPassage.x, L10 + SkirtBottom, 39.5f), 180f, new Vector3(1f, (L5 - 1f - (L10 + SkirtBottom)) / 16f, 1f));
        PropsUnder(g, K(g, "Bridge-8x4m", new Vector3(VaultPassage.x, L5 - 1f, 28.5f), 90f, new Vector3(7f / 8f, 1f, 1f)), 1f, Vector2.zero);
        skirtOpenings.Add((CellOf(VaultPassage.x, VaultPassage.y), Vector2Int.down));
    }

    // ─── Spawns ───────────────────────────────────────────────────────────────

    private static readonly (string name, float x, float z, float y)[] SpawnSpots =
    {
        ("Gate Lane",          4, -20, G),
        ("North Street",       0,  20, G),
        ("Old Town West",    -20,  12, G),
        ("Old Town East",     20,  12, G),
        ("Pump Hall",        -28, -28, G),
        ("Sewers West",      -20,  -4, D),
        ("Sewers East",       20,  -4, D),
        ("Docks West",       -20, -58, D),
        ("Docks East",        24, -58, D),
        ("Docks Middle",       4, -72, D),
        ("Works Yard",       -52,  -4, L5),
        ("Works Alley",      -68, -28, L5),
        ("Works North",      -60,  14, L5),
        ("Terraces Yard",     52,  -3, L5),
        ("Terraces Alley",    68,  12, L5),
        ("Terrace Mezzanine", 80, -28, L10),
        ("Heights West",     -28,  76, L10),
        ("Heights East",      28,  76, L10),
        ("Heights Middle",     0,  52, L10),
        ("Vaults",           -44,  64, L5),
    };

    private static void RenderOverviews()
    {
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(6f, true, true);
        Shot("stairfall_top", new Vector3(0, 220, 0), Quaternion.Euler(90, 0, 0), true, 100f);
        Shot("stairfall_sw", new Vector3(-130, 80, -140), Quaternion.LookRotation(new Vector3(130, -75, 140)), false, 0f);
        Shot("stairfall_ne", new Vector3(130, 90, 130), Quaternion.LookRotation(new Vector3(-130, -80, -130)), false, 0f);
        Shot("stairfall_town", new Vector3(4, 4f, -30), Quaternion.LookRotation(new Vector3(0, 0.05f, 1)), false, 0f);
        Shot("stairfall_alley", new Vector3(-28, 2.5f, -12), Quaternion.LookRotation(new Vector3(1, 0.05f, 0)), false, 0f);
        Shot("stairfall_works", new Vector3(-68, 7.5f, -38), Quaternion.LookRotation(new Vector3(0, 0.05f, 1)), false, 0f);
        Shot("stairfall_heights", new Vector3(0, 20, 20), Quaternion.LookRotation(new Vector3(0, -4, 30)), false, 0f);
        Shot("stairfall_sewers", new Vector3(0, D + 2.5f, -20), Quaternion.LookRotation(new Vector3(0, 0, 1)), false, 0f);
        Shot("stairfall_docks", new Vector3(50, 10, -100), Quaternion.LookRotation(new Vector3(-50, -12, 40)), false, 0f);
        Shot("stairfall_terraces", new Vector3(30, 14, -40), Quaternion.LookRotation(new Vector3(40, -4, 30)), false, 0f);
    }
}
