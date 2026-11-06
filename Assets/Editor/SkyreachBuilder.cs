using UnityEditor;
using UnityEngine;
using static GreyboxKit;

/// <summary>
/// Builds the Skyreach level (Assets/Scenes/Levels/Skyreach.unity) from code: a vertical map made of stacked
/// "mainlands". Each tier is a real floor with its own buildings, tunnels and cover, and walls rise to the next
/// floor: 0 (the town, 130 × 130) → 14 (the West and North mesas, one U, and the East mesa) → 28 (the High mesa and
/// the East tower) → 42 (the Crown, bridged to the floating Sky Isle). Under the town lies the undercroft (−8), a
/// grid of 4 m tunnels reached by ramps and drop holes, which leads out to the Dock Isle.
///
/// Every tier has an easy way up (a ramp along the wall, sometimes a lift) and a hard way: a building climb
/// (step 2.5 → a short building's roof 5 → a second building on that roof 9 → a crouch jump / ledge grab of 5 m onto
/// the floor above). No floating stepping stones. Tunnels run through the mesas (a cave with a lift shaft, a den
/// with a crouch tunnel, a hall in the High mesa) and out onto cliff paths around the outside.
///
/// Re-running rebuilds Geometry/Skyreach; review output goes to Library/TurboTag/scratch/skyreach_report.txt and
/// skyreach_*.png. Menu: Turbo Tag/Levels/Rebuild Skyreach (editor job: BuildSkyreach).
/// </summary>
public static class SkyreachBuilder
{
    private const string ScenePath  = "Assets/Scenes/Levels/Skyreach.unity";
    private const string RootName   = "Skyreach";
    private const string ReportPath = "Library/TurboTag/scratch/skyreach_report.txt";

    // Tier floors
    private const float Under = -8f, T1 = 14f, T2 = 28f, T3 = 42f;
    private const float Low = 2.5f;

    [MenuItem("Turbo Tag/Levels/Rebuild Skyreach")]
    public static void Build()
    {
        var scene = Begin(ScenePath, RootName);

        BuildGround();
        BuildUndercroft();
        BuildTown();
        BuildWestMesa();
        BuildNorthMesa();
        BuildEastMesa();
        BuildHighMesa();
        BuildSkyIsle();
        BuildDock();
        Bounds(new Vector3(0, 0, -20), 500f, -60f, 70f);
        Spawns(SpawnSpots, new Vector3(0, 0, -10));
        PlaceSpawner(new Vector3(0, 0.1f, -18), -45f);

        Finish(scene, ReportPath, "Skyreach", RenderOverviews);
    }

    private static (float, float, float, float, float, float) V(float x0, float x1, float y0, float y1, float z0, float z1) =>
        (x0, x1, y0, y1, z0, z1);

    // Holes in the town floor: two ramps down to the undercroft and two drop holes (bounce pads below)
    private static readonly Vector4 RampHoleW = new(-19, -13, -25, -5);
    private static readonly Vector4 RampHoleE = new(13, 19, -5, 15);
    private static readonly Vector4 DropS     = new(-3, 3, -31, -25);
    private static readonly Vector4 DropN     = new(-3, 3, 17, 23);

    // ─── Ground (0) ───────────────────────────────────────────────────────────

    private static void BuildGround()
    {
        var g = Group("Ground");
        Slab(g, "Ground Slab", -65, 65, -4, 0, -65, 65, Kind.Floor, RampHoleW, RampHoleE, DropS, DropN);
        Ramp(g, "Ramp Down West", RampHoleW.x, RampHoleW.y, RampHoleW.z, RampHoleW.w, Under, 0);
        Ramp(g, "Ramp Down East", RampHoleE.x, RampHoleE.y, RampHoleE.z, RampHoleE.w, 0, Under);
        // Bedrock under the mesas (the undercroft only runs under the town) and a dark mass below it all
        Box(g, "Bedrock W", -65, -33, Under - 1, -4, -65, 65, Kind.Dark);
        Box(g, "Bedrock E", 33, 65, Under - 1, -4, -65, 65, Kind.Dark);
        Box(g, "Bedrock N", -33, 33, Under - 1, -4, 33, 65, Kind.Dark);
        Box(g, "Underside", -60, 60, -18, Under - 1, -60, 60, Kind.Dark);
    }

    // ─── Undercroft (−8) ──────────────────────────────────────────────────────

    // 10 m blocks with 6 m corridors (4 m clear) under the town, a hall, a crawl and a crouch tunnel, baffles so
    // no corridor runs straight through, windows to the pit along the south edge and the way out to the dock
    private static void BuildUndercroft()
    {
        var g = Group("Undercroft");
        Box(g, "Undercroft Floor", -33, 33, Under - 1, Under, -65, 33, Kind.Floor);
        float[] xs = { -24, -8, 8, 24 };
        float[] zs = { -52, -36, -20, -4, 12, 28 };
        foreach (float cx in xs)
        foreach (float cz in zs)
        {
            if (cz == -4 && (cx == -8 || cx == 8)) continue; // The hall
            string n = $"Undercroft Block {cx},{cz}";
            if (cx == -8 && cz == -36)
            {
                Carve(g, n, cx - 5, cx + 5, Under, -4, cz - 5, cz + 5, Kind.Wall, V(cx - 5, cx + 5, Under, Under + 1.7f, cz - 1.25f, cz + 1.25f));
                Tunnel("Undercroft Crawl", new Vector3(cx - 6, Under, cz), new Vector3(cx + 6, Under, cz), "crawl");
            }
            else if (cx == 8 && cz == 12)
            {
                Carve(g, n, cx - 5, cx + 5, Under, -4, cz - 5, cz + 5, Kind.Wall, V(cx - 1.25f, cx + 1.25f, Under, Under + 2.4f, cz - 5, cz + 5));
                Tunnel("Undercroft Crouch", new Vector3(cx, Under, cz - 6), new Vector3(cx, Under, cz + 6), "crouch");
            }
            else Box(g, n, cx - 5, cx + 5, Under, -4, cz - 5, cz + 5, Kind.Wall);
        }
        Box(g, "Baffle 1", -19, -13, Under, -4, -45, -43, Kind.Wall);
        Box(g, "Baffle 2", 13, 19, Under, -4, -38, -36, Kind.Wall);
        Box(g, "Baffle 3", -25, -23, Under, -4, 1, 7, Kind.Wall);
        Box(g, "Baffle 4", -9, -7, Under, -4, 17, 23, Kind.Wall);
        Box(g, "Baffle 5", 29, 33, Under, -4, -21, -19, Kind.Wall);
        Tunnel("Undercroft Corridor E-W", new Vector3(-31, Under, -12), new Vector3(31, Under, -12), "run");
        Tunnel("Undercroft Ring West", new Vector3(-31, Under, -63), new Vector3(-31, Under, 31), "run");

        // South edge: walls with windows onto the pit and the dock exit (x −3..3)
        Box(g, "Edge S1", -33, -14, Under, -4, -65, -64, Kind.Wall);
        Box(g, "Edge S2", -8, -3, Under, -4, -65, -64, Kind.Wall);
        Box(g, "Edge S3", 3, 20, Under, -4, -65, -64, Kind.Wall);
        Box(g, "Edge S4", 26, 33, Under, -4, -65, -64, Kind.Wall);

        Bounce(g, new Vector3(0, Under, (DropS.z + DropS.w) / 2), 32f);
        Bounce(g, new Vector3(0, Under, (DropN.z + DropN.w) / 2), 32f);
        Prop(g, "Crate-1x1m", new Vector3(-16, Under, -60), 20f, 1.4f);
        Prop(g, "Crate-1x1m", new Vector3(5, Under, -5), -10f, 1.2f);
        foreach (var p in new[] { new Vector3(0, -5, -4), new Vector3(-16, -5, 20), new Vector3(16, -5, -28), new Vector3(-31, -5, -50),
                                  new Vector3(31, -5, 4), new Vector3(-16, -5, -44), new Vector3(16, -5, 20), new Vector3(0, -5, -60),
                                  new Vector3(-31, -5, 10) })
            Lamp(g, p, 18f);
    }

    // ─── Town (0): the mainland ───────────────────────────────────────────────

    private static void BuildTown()
    {
        var g = Group("Town");
        // Long house (5) with a running tunnel along it
        Carve(g, "Long House", -26, -16, 0, 5, -45, -30, Kind.Floor, V(-23, -19, 0, 4, -45, -30));
        Tunnel("Long House Tunnel", new Vector3(-21, 0, -46), new Vector3(-21, 0, -29), "run");
        Ledge(g, "Long House Step", -16, -14, -40, -36, Low);

        // Two-storey house: a hall with four doors, a loft on its roof (reached from the roof: a hiding room)
        Hut(g, "Two-Storey", -8, 8, -5, 10, 0, 4.5f, 'N', 'S', 'E', 'W');
        Hut(g, "Loft", -8, 0, -5, 3, 5, 4f, 'E', 'N');
        Ledge(g, "Two-Storey Step", 8, 10, 4, 8, Low);

        // Crawl shed and market arcade (roof at 5.6 on pillars)
        Carve(g, "Crawl Shed", 14, 24, 0, 3, -40, -32, Kind.Wall, V(14, 24, 0, 1.7f, -37.25f, -34.75f));
        Tunnel("Crawl Shed", new Vector3(13, 0, -36), new Vector3(25, 0, -36), "crawl");
        var arcade = Group("Market Arcade", g);
        foreach (float x in new[] { -28f, -21.5f, -15f })
        foreach (float z in new[] { 10f, 21f })
            Box(arcade, "Arcade Pillar", x, x + 1, 0, 5, z, z + 1, Kind.Wall);
        Box(arcade, "Arcade Roof", -28.5f, -13.5f, 5, 5.6f, 9.5f, 22.5f, Kind.Floor);
        Ledge(arcade, "Arcade Step", -13.5f, -11.5f, 12, 15, Low);

        // Gatehouse on the south (open) border, and cover
        Hut(g, "Gatehouse", 12, 24, -58, -50, 0, 4.5f, 'N', 'S');
        Box(g, "South Parapet W", -65, -40, 0, 1.2f, -65, -64, Kind.Wall);
        Box(g, "South Parapet E", 30, 50, 0, 1.2f, -65, -64, Kind.Wall);
        Prop(g, "HalfWall-2m", new Vector3(-6, 0, -20), 30f);
        Prop(g, "HalfWall-2m", new Vector3(10, 0, -24), 90f);
        Prop(g, "HalfWall-2m", new Vector3(-10, 0, 26), 60f);
        Prop(g, "HalfWall-2m", new Vector3(42, 0, 14), 15f);
        Prop(g, "Crate-1x1m", new Vector3(24, 0, -10), 0f, 1.4f);
        Prop(g, "Crate-1x1m", new Vector3(25.4f, 0, -8.8f), 25f, 0.9f);
        Prop(g, "Crate-1x1m", new Vector3(48, 0, 24), 10f, 1.3f);
        Prop(g, "Fan", new Vector3(21, 0, 29), 0f); // Rides you up beside the North mesa
        Hut(g, "NE Shed", 44, 54, 8, 16, 0, 4.5f, 'S', 'W');

        // NE terrace (7): a low tier in the open corner, with a tunnel out to the east cliff path, its own ramp and
        // building climb, and a ramp on up to the North mesa (14)
        // Its tunnel turns: in from the south face, east out to the cliff path
        Carve(g, "NE Terrace", 45, 65, 0, 7, 35, 65, Kind.Floor, V(49, 65, 0, 4, 48, 52), V(49, 53, 0, 4, 35, 52));
        Tunnel("NE Terrace Tunnel S", new Vector3(51, 0, 34), new Vector3(51, 0, 50), "run");
        Tunnel("NE Terrace Tunnel E", new Vector3(51, 0, 50), new Vector3(66, 0, 50), "run");
        RampWayZ(g, "NE Terrace Ramp", 41, 45, 34, 58, 0, 7, 0, 3);
        Carve(g, "NE Climb House", 55, 63, 0, 5, 29, 35, Kind.Floor, V(55, 63, 0, 4, 30, 34));
        Tunnel("NE Climb House", new Vector3(54, 0, 32), new Vector3(64, 0, 32), "run");
        Ledge(g, "NE Climb Step", 57, 60, 26, 29, Low);
        RampWayX(g, "Terrace To North Mesa", 61, 65, 45, 29, 7, T1, 0, 0); // Arrives on the North ramp's head
        Hut(g, "Terrace Hut", 52, 60, 54, 62, 7, 4.5f, 'W', 'S');
        Lamp(g, new Vector3(55, 14, 45), 20f);

        // Clutter for the open ground: kiosks, a shed, stalls, low walls, crates
        Hut(g, "SW Shed", -60, -52, -62, -54, 0, 4.5f, 'N', 'E');
        Kiosk(g, "Kiosk SW 1", -48, -55, 0, 4, 4, Low);
        Kiosk(g, "Kiosk SW 2", -34, -52, 0, 3, 6, 4f);
        Kiosk(g, "Kiosk SW 3", -56, -45, 0, 3, 3, Low);
        Kiosk(g, "Stall 1", -8, -52, 0, 3, 2, Low);
        Kiosk(g, "Stall 2", -2, -52, 0, 3, 2, Low);
        Kiosk(g, "Stall 3", 4, -52, 0, 3, 2, Low);
        Kiosk(g, "Plaza Wall", 9, -17, 0, 6, 1, 1.2f);
        Kiosk(g, "Plaza Kiosk", 10, -32, 0, 3, 3, Low);
        Kiosk(g, "East Kiosk", 28, -26, 0, 3, 3, Low);
        Kiosk(g, "NE Kiosk 1", 34, 14, 0, 4, 4, 3f);
        Kiosk(g, "NE Kiosk 2", 58, 22, 0, 3, 6, Low);
        Kiosk(g, "NE Kiosk 3", 35, 25, 0, 2.5f, 2.5f, Low);
        Kiosk(g, "South Kiosk", -14, -60, 0, 4, 4, 3f);
        Prop(g, "Crate-1x1m", new Vector3(-45, 0, -60), 20f, 1.3f);
        Prop(g, "Crate-1x1m", new Vector3(-30, 0, -58), -10f, 1.5f);
        Prop(g, "Crate-1x1m", new Vector3(-4, 0, -60), 35f, 1.3f);
        Prop(g, "HalfWall-2m", new Vector3(-55, 0, -48), 70f);
        Prop(g, "HalfWall-2m", new Vector3(0, 0, -44), 0f);
        Prop(g, "HalfWall-2m", new Vector3(28, 0, -58), 45f);
        Prop(g, "HalfWall-2m", new Vector3(62, 0, 28), 90f);

        // Sky lift: ground → the Sky Isle (42), the one easy way to the top
        Mover(g, "Sky Lift", new Vector3(-22, -1, 5), new Vector3(-22, T3 - 1, 5), new Vector3(4, 1, 4), 6f);

        Lamp(g, new Vector3(0, 10, -18), 26f);
        Lamp(g, new Vector3(30, 8, -40), 20f);
        Lamp(g, new Vector3(40, 8, 20), 20f);
    }

    // ─── West mesa (14) ───────────────────────────────────────────────────────

    private static void BuildWestMesa()
    {
        var g = Group("West Mesa");
        // A gallery tunnel from the town into a cave; a lift shaft up to the top; a tunnel out to the west cliff path
        Carve(g, "West Mesa", -65, -35, 0, T1, -40, 65, Kind.Floor,
              V(-45, -35, 0, 4, -12, -8),        // Gallery from the town
              V(-58, -45, 0, 7, -18, -2),        // Cave
              V(-55, -50, 7, T1, -12, -7),       // Lift shaft
              V(-65, -58, 0, 4, -12, -8));       // Out to the cliff path
        Tunnel("West Gallery", new Vector3(-34, 0, -10), new Vector3(-46, 0, -10), "run");
        Tunnel("West Cliff Tunnel", new Vector3(-57, 0, -10), new Vector3(-66, 0, -10), "run");
        Mover(g, "Cave Lift", new Vector3(-52.5f, -1, -9.5f), new Vector3(-52.5f, T1 - 1, -9.5f), new Vector3(4, 1, 4), 4f);
        Lamp(g, new Vector3(-51, 5, -10), 14f);

        // Cliff path outside the west and north faces (floor level, no rail)
        Box(g, "West Cliff Path", -69, -65, -1, 0, -45, 69, Kind.Floor);
        Box(g, "North Cliff Path", -65, 20, -1, 0, 65, 69, Kind.Floor);

        // Easy: a ramp up the east face (0 → 14) on an embankment, arched over the gallery, onto a head block.
        // Hard: the building climb on the south face
        RampWayZ(g, "West Ramp", -35, -31, -34, 20, 0, T1, 0, 5, -10);
        Tunnel("West Ramp Arch", new Vector3(-30, 0, -10), new Vector3(-36, 0, -10), "run");
        Carve(g, "W Climb House", -50, -42, 0, 5, -46, -40, Kind.Floor, V(-50, -42, 0, 4, -45, -41));
        Box(g, "W Climb Upper", -50, -42, 5, 9, -43, -40, Kind.Floor);
        Ledge(g, "W Climb Step", -46, -43, -48, -46, Low);
        Tunnel("W Climb House", new Vector3(-51, 0, -43), new Vector3(-41, 0, -43), "run");

        // A second climb against the High mesa's south face (14 → 28)
        Box(g, "HM Block", -50, -44, T1, T1 + 5, 20, 25, Kind.Floor);
        Box(g, "HM Block Upper", -50, -47, T1 + 5, T1 + 9, 22, 25, Kind.Floor);
        Ledge(g, "HM Block Step", -44, -42, 20, 23, T1 + Low);
        Kiosk(g, "West Top Kiosk 1", -60, -15, T1, 4, 4, 3f);
        Kiosk(g, "West Top Kiosk 2", -40, -15, T1, 3, 3, Low);
        Prop(g, "HalfWall-2m", new Vector3(-45, T1, -4), 20f);

        // On top: two huts with a roof bridge, a tunnel block, cover
        Hut(g, "Mesa Hut A", -60, -52, -35, -27, T1, 4.5f, 'E', 'N');
        Hut(g, "Mesa Hut B", -46, -38, -35, -27, T1, 4.5f, 'W', 'N');
        Box(g, "Hut Roof Bridge", -52, -46, T1 + 4.5f, T1 + 5f, -32.5f, -29.5f, Kind.Floor);
        Carve(g, "Mesa Tunnel Block", -62, -50, T1, T1 + 5, 0, 10, Kind.Floor, V(-62, -50, T1, T1 + 4, 3, 7));
        Tunnel("Mesa Tunnel", new Vector3(-63, T1, 5), new Vector3(-49, T1, 5), "run");
        Ledge(g, "Mesa Block Step", -50, -48, 7, 10, T1 + Low);
        Prop(g, "Crate-1x1m", new Vector3(-44, T1, -18), 15f, 1.4f);
        Prop(g, "HalfWall-2m", new Vector3(-48, T1, 14), 0f);
        Lamp(g, new Vector3(-50, T1 + 8, -20), 22f);
    }

    // ─── North mesa (14) ──────────────────────────────────────────────────────

    private static void BuildNorthMesa()
    {
        var g = Group("North Mesa");
        Carve(g, "North Mesa", -35, 25, 0, T1, 35, 65, Kind.Floor,
              V(-20, -16, 0, 4, 35, 65),         // Running tunnel through to the north cliff path
              V(-20, -16, 4, T1, 48, 52),        // Drop shaft into it from the top
              V(2, 10, 0, 5, 36, 46),            // Den
              V(4, 7, 0, 4, 35, 36),             // Den door (south)
              V(10, 25, 0, 2.4f, 38.75f, 41.25f)); // Crouch tunnel from the den to the east face
        Tunnel("North Tunnel", new Vector3(-18, 0, 34), new Vector3(-18, 0, 66), "run");
        Tunnel("Den Door", new Vector3(5.5f, 0, 34), new Vector3(5.5f, 0, 38), "run");
        Tunnel("Den Crouch", new Vector3(11, 0, 40), new Vector3(26, 0, 40), "crouch");
        Lamp(g, new Vector3(6, 3.5f, 41), 10f);

        // Easy: a ramp up the east face (0 → 14). Hard: the building climb on the south face
        RampWayZ(g, "North Ramp", 25, 29, 18, 60, 0, T1, 0, 5, 40);   // Arched over the den's crouch tunnel
        Tunnel("North Ramp Arch", new Vector3(25.5f, 0, 40), new Vector3(30, 0, 40), "run");

        // More ways up the south face: a lean-to and a two-step block
        Box(g, "Lean-to", -32, -24, 0, 4.5f, 30, 35, Kind.Floor);
        Box(g, "Lean-to Upper", -32, -28, 4.5f, 8.5f, 32, 35, Kind.Floor);
        Ledge(g, "Lean-to Step", -24, -22, 30, 33, Low);
        Box(g, "South Face Block", 10, 18, 0, 5, 29, 35, Kind.Floor);
        Box(g, "South Face Block Upper", 13, 18, 5, 9, 32, 35, Kind.Floor);
        Ledge(g, "South Face Step", 8, 10, 29, 32, Low);
        Kiosk(g, "North Top Kiosk 1", -26, 56, T1, 4, 4, 3f);
        Kiosk(g, "North Top Kiosk 2", 20, 40, T1, 3, 3, Low);
        Carve(g, "N Climb House", -14, -4, 0, 5, 28, 35, Kind.Floor, V(-14, -4, 0, 4, 29.5f, 33.5f));
        Box(g, "N Climb Upper", -14, -9, 5, 9, 31, 35, Kind.Floor);
        Ledge(g, "N Climb Step", -4, -2, 28, 31, Low);
        Tunnel("N Climb House", new Vector3(-15, 0, 31.5f), new Vector3(-3, 0, 31.5f), "run");

        // On top: a watch hut, a tunnel block, a crawl block
        Hut(g, "Watch Hut", -10, -2, 52, 60, T1, 4.5f, 'S', 'E');
        Carve(g, "North Tunnel Block", 6, 18, T1, T1 + 5, 50, 60, Kind.Floor, V(10, 14, T1, T1 + 4, 50, 60));
        Tunnel("North Top Tunnel", new Vector3(12, T1, 49), new Vector3(12, T1, 61), "run");
        Carve(g, "North Crawl Block", -30, -22, T1, T1 + 3, 40, 46, Kind.Wall, V(-30, -22, T1, T1 + 1.7f, 41.75f, 44.25f));
        Tunnel("North Top Crawl", new Vector3(-31, T1, 43), new Vector3(-21, T1, 43), "crawl");
        Prop(g, "Crate-1x1m", new Vector3(-4, T1, 42), 30f, 1.3f);
        Lamp(g, new Vector3(0, T1 + 8, 50), 22f);
    }

    // ─── East mesa (14) and East tower (28) ───────────────────────────────────

    private static void BuildEastMesa()
    {
        var g = Group("East Mesa");
        Carve(g, "East Mesa", 35, 65, 0, T1, -65, 0, Kind.Floor, V(35, 65, 0, 4, -22, -18), V(38, 42, 0, 4, -18, 0));
        Tunnel("East Tunnel", new Vector3(34, 0, -20), new Vector3(66, 0, -20), "run");
        Tunnel("East Tunnel North", new Vector3(40, 0, 1), new Vector3(40, 0, -17), "run");
        Box(g, "East Cliff Path", 65, 69, -1, 0, -60, 65, Kind.Floor);

        // Easy: a ramp along the north face (0 → 14). Hard: the building climb on the west face
        RampWayX(g, "East Ramp", 0, 4, 22, 60, 0, T1, 0, 5, 40);   // Arched over the tunnel into the mesa
        Tunnel("East Ramp Arch", new Vector3(40, 0, 6), new Vector3(40, 0, -1), "run");
        Box(g, "West Face Block", 30, 35, 0, 4.5f, -12, -4, Kind.Floor);
        Box(g, "West Face Block Upper", 32, 35, 4.5f, 9, -12, -8, Kind.Floor);
        Ledge(g, "West Face Step", 28, 30, -8, -5, Low);
        Kiosk(g, "East Top Kiosk 1", 56, -15, T1, 4, 4, 3f);
        Kiosk(g, "East Top Kiosk 2", 44, -30, T1, 3, 3, Low);
        Kiosk(g, "East Top Kiosk 3", 40, -50, T1, 4, 6, 3f);
        Prop(g, "Crate-1x1m", new Vector3(38, T1, -58), 20f, 1.3f);
        Carve(g, "E Climb House", 29, 35, 0, 5, -50, -40, Kind.Floor, V(30, 34, 0, 4, -50, -40));
        Box(g, "E Climb Upper", 32, 35, 5, 9, -50, -44, Kind.Floor);
        Ledge(g, "E Climb Step", 27, 29, -44, -41, Low);
        Tunnel("E Climb House", new Vector3(32, 0, -51), new Vector3(32, 0, -39), "run");

        Hut(g, "East Hut", 38, 46, -12, -4, T1, 4.5f, 'S', 'W');
        Prop(g, "HalfWall-2m", new Vector3(42, T1, -24), 90f);

        // East tower (28): easy ramp along its west face, hard building climb on its north face
        Box(g, "East Tower", 50, 65, T1, T2, -65, -35, Kind.Floor);
        RampWayZ(g, "Tower Ramp", 46, 50, -28, -62, T1, T2, T1, 3);
        Box(g, "T Climb House", 52, 60, T1, T1 + 5, -35, -29, Kind.Floor);
        Box(g, "T Climb Upper", 52, 56, T1 + 5, T1 + 9, -35, -32, Kind.Floor);
        Ledge(g, "T Climb Step", 57, 60, -29, -27, T1 + Low);
        Hut(g, "Tower Hut", 54, 62, -60, -52, T2, 4.5f, 'N', 'W');
        Lamp(g, new Vector3(56, T2 + 8, -48), 22f);
        Lamp(g, new Vector3(45, T1 + 8, -15), 20f);
    }

    // ─── High mesa (28) and the Crown (42) ────────────────────────────────────

    private static void BuildHighMesa()
    {
        var g = Group("High Mesa");
        // A hall inside (at 14) with a door south and a tunnel east
        Carve(g, "High Mesa", -65, -40, T1, T2, 25, 65, Kind.Floor,
              V(-60, -48, T1, T1 + 6, 30, 40),   // Hall
              V(-56, -52, T1, T1 + 4, 25, 30),   // South door
              V(-48, -40, T1, T1 + 4, 33, 37));  // East tunnel
        Tunnel("High Hall Door", new Vector3(-54, T1, 24), new Vector3(-54, T1, 32), "run");
        Tunnel("High Hall Tunnel", new Vector3(-50, T1, 35), new Vector3(-39, T1, 35), "run");
        Lamp(g, new Vector3(-54, T1 + 4.5f, 35), 12f);

        // Easy: ramp up the east face (14 → 28). Hard: building climb on the south face
        RampWayZ(g, "High Ramp", -40, -36, 20, 60, T1, T2, T1, 5, 35);   // Arched over the hall's east tunnel
        Tunnel("High Ramp Arch", new Vector3(-35, T1, 35), new Vector3(-41, T1, 35), "run");
        Box(g, "H Climb House", -64, -58, T1, T1 + 5, 19, 25, Kind.Floor);
        Box(g, "H Climb Upper", -64, -61, T1 + 5, T1 + 9, 22, 25, Kind.Floor);
        Ledge(g, "H Climb Step", -58, -55, 19, 22, T1 + Low);

        Hut(g, "High Hut", -62, -54, 28, 36, T2, 4.5f, 'N', 'E');
        Prop(g, "Crate-1x1m", new Vector3(-44, T2, 30), 10f, 1.3f);

        // Crown (42): easy ramp up its east face, hard building climb on its south face
        Box(g, "Crown", -65, -48, T2, T3, 45, 65, Kind.Floor);
        RampWayZ(g, "Crown Ramp", -48, -44, 27, 63, T2, T3, T2, 2);
        Kiosk(g, "High Top Kiosk", -50, 40, T2, 3, 3, Low);
        Box(g, "C Climb House", -62, -54, T2, T2 + 5, 39, 45, Kind.Floor);
        Box(g, "C Climb Upper", -62, -58, T2 + 5, T2 + 9, 42, 45, Kind.Floor);
        Ledge(g, "C Climb Step", -54, -51, 40, 43, T2 + Low);
        Hut(g, "Lookout", -62, -54, 52, 60, T3, 4.5f, 'S', 'E');
        Lamp(g, new Vector3(-56, T3 + 8, 50), 22f);
        Lamp(g, new Vector3(-52, T2 + 8, 32), 20f);

        // The long bridge to the Sky Isle: east from the Crown (one 3 m hop), then south
        Box(g, "Sky Bridge W", -48, -30, T3 - 0.5f, T3, 50, 53, Kind.Floor);
        Box(g, "Sky Bridge E", -27, -10, T3 - 0.5f, T3, 50, 53, Kind.Floor);
        Box(g, "Sky Bridge S", -13, -10, T3 - 0.5f, T3, 15, 50, Kind.Floor);
        gaps.Add(("Sky bridge hop", new Vector3(-30, T3, 51.5f), new Vector3(-27, T3, 51.5f)));
    }

    // Floating at 42 over the town: a hut and a raised corner
    private static void BuildSkyIsle()
    {
        var g = Group("Sky Isle");
        Box(g, "Sky Isle", -20, 0, T3 - 4, T3, -5, 15, Kind.Floor);
        Box(g, "Sky Isle Underside", -17, -3, T3 - 8, T3 - 4, -2, 12, Kind.Dark);
        Hut(g, "Sky Hut", -18, -10, -3, 5, T3, 4.5f, 'E', 'N');
        Box(g, "Sky Rise", -6, 0, T3, T3 + 4, 9, 15, Kind.Floor);
        Ledge(g, "Sky Rise Step", -8, -6, 10, 13, T3 + Low);
        Prop(g, "Crate-1x1m", new Vector3(-4, T3, 0), 30f, 1.2f);
        Lamp(g, new Vector3(-10, T3 + 8, 5), 20f);
    }

    // ─── Dock isle (−8), out of the undercroft's south side ───────────────────

    private static void BuildDock()
    {
        var g = Group("Dock Isle");
        Box(g, "Dock Walkway", -2, 2, Under - 1, Under, -85, -65, Kind.Floor);
        Box(g, "Dock Isle", -18, 18, Under - 4, Under, -105, -85, Kind.Floor);
        Box(g, "Dock Underside", -14, 14, Under - 9, Under - 4, -101, -89, Kind.Dark);
        Carve(g, "Dock Block", -14, -2, Under, Under + 6, -100, -90, Kind.Floor, V(-14, -2, Under, Under + 4, -97, -93));
        Tunnel("Dock Tunnel", new Vector3(-15, Under, -95), new Vector3(-1, Under, -95), "run");
        Ledge(g, "Dock Step", -2, 0, -93, -90, Under + Low);
        Carve(g, "Dock Crawl", 4, 12, Under, Under + 3, -100, -94, Kind.Wall, V(4, 12, Under, Under + 1.7f, -98.25f, -95.75f));
        Tunnel("Dock Crawl", new Vector3(3, Under, -97), new Vector3(13, Under, -97), "crawl");
        // Back up to the town: the shuttle
        Mover(g, "Dock Shuttle", new Vector3(12, Under - 1, -83), new Vector3(12, -1, -67), new Vector3(4, 1, 4), 3f);
        Lamp(g, new Vector3(0, Under + 8, -95), 20f);
    }

    // ─── Spawns ───────────────────────────────────────────────────────────────

    private static readonly (string name, float x, float z, float y)[] SpawnSpots =
    {
        ("Town Square",       0, -18, 0),
        ("Town West",       -10, -40, 0),
        ("Town East",        22, -15, 0),
        ("NE Yard",          38,  24, 0),
        ("Gatehouse",        18, -62, 0),
        ("Undercroft Hall",   0,  -4, Under),
        ("Undercroft West", -31, -40, Under),
        ("West Mesa",       -45, -15, T1),
        ("North Mesa",        0,  45, T1),
        ("East Mesa",        40, -22, T1),
        ("High Mesa",       -51,  30, T2),
        ("Dock Isle",         0, -88, Under),
    };

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>A step block (orange top) 2.5 m tall standing on the floor, its top at <paramref name="top"/>.</summary>
    private static void Ledge(Transform g, string name, float x0, float x1, float z0, float z1, float top) =>
        Box(g, name, x0, x1, top - Low, top, z0, z1, Kind.Ledge);

    /// <summary>A plain block (light top) centred on (x, z), standing on <paramref name="floor"/>: clutter, cover, a step.</summary>
    private static void Kiosk(Transform g, string name, float x, float z, float floor, float w, float d, float h) =>
        Box(g, name, x - w / 2, x + w / 2, floor, floor + h, z - d / 2, z + d / 2, Kind.Floor);

    private const float ArchClear = 4f, ArchHalf = 2.25f, Step = 1.5f;

    /// <summary>
    /// A ramp along Z (x0..x1 wide) from <paramref name="zLow"/> (height yLow) to <paramref name="zHigh"/> (yHigh), built
    /// like a structure: a stepped embankment fills the space under it down to <paramref name="floor"/> (with 4 m arches
    /// at the given z positions, where tunnels pass under), and a head block of the upper height continues past the
    /// top for <paramref name="head"/> m so the ramp lands on solid floor joined to the level above.
    /// </summary>
    private static void RampWayZ(Transform g, string name, float x0, float x1, float zLow, float zHigh, float yLow, float yHigh,
                                 float floor, float head, params float[] arches)
    {
        var w = Group(name, g);
        if (zLow < zHigh) Ramp(w, name, x0, x1, zLow, zHigh, yLow, yHigh);
        else Ramp(w, name, x0, x1, zHigh, zLow, yHigh, yLow);
        float dir = Mathf.Sign(zHigh - zLow), length = Mathf.Abs(zHigh - zLow);
        for (float d = 0; d < length - 0.01f; d += Step)
        {
            float e = Mathf.Min(d + Step, length);
            float under = yLow + (yHigh - yLow) * d / length - 0.7f;
            float za = zLow + dir * d, zb = zLow + dir * e;
            Embank(w, $"{name} Embankment", x0, x1, Mathf.Min(za, zb), Mathf.Max(za, zb), floor, under, arches, true);
        }
        if (head > 0)
        {
            float h0 = zHigh, h1 = zHigh + dir * head;
            Box(w, $"{name} Head", x0, x1, floor, yHigh, Mathf.Min(h0, h1), Mathf.Max(h0, h1), Kind.Floor);
        }
    }

    /// <summary>The same along X (z0..z1 wide).</summary>
    private static void RampWayX(Transform g, string name, float z0, float z1, float xLow, float xHigh, float yLow, float yHigh,
                                 float floor, float head, params float[] arches)
    {
        var w = Group(name, g);
        if (xLow < xHigh) RampX(w, name, xLow, xHigh, z0, z1, yLow, yHigh);
        else RampX(w, name, xHigh, xLow, z0, z1, yHigh, yLow);
        float dir = Mathf.Sign(xHigh - xLow), length = Mathf.Abs(xHigh - xLow);
        for (float d = 0; d < length - 0.01f; d += Step)
        {
            float e = Mathf.Min(d + Step, length);
            float under = yLow + (yHigh - yLow) * d / length - 0.7f;
            float xa = xLow + dir * d, xb = xLow + dir * e;
            Embank(w, $"{name} Embankment", Mathf.Min(xa, xb), Mathf.Max(xa, xb), z0, z1, floor, under, arches, false);
        }
        if (head > 0)
        {
            float h0 = xHigh, h1 = xHigh + dir * head;
            Box(w, $"{name} Head", Mathf.Min(h0, h1), Mathf.Max(h0, h1), floor, yHigh, z0, z1, Kind.Floor);
        }
    }

    // One embankment slice under a ramp; along Z: (a0, a1) is x, (b0, b1) is z; along X: (a0, a1) is x, (b0, b1) is z too
    private static void Embank(Transform w, string name, float a0, float a1, float b0, float b1, float floor, float under,
                               float[] arches, bool alongZ)
    {
        if (under - floor < 0.5f) return;
        float s0 = alongZ ? b0 : a0, s1 = alongZ ? b1 : a1;
        bool inArch = false;
        foreach (float a in arches) if (s1 > a - ArchHalf && s0 < a + ArchHalf) inArch = true;
        float bottom = floor;
        if (inArch)
        {
            if (under < floor + ArchClear + 0.3f) return;
            bottom = floor + ArchClear;
        }
        Box(w, name, a0, a1, bottom, under, b0, b1, Kind.Wall);
    }

    private static void RenderOverviews()
    {
        Shot("skyreach_top", new Vector3(0, 170, -20), Quaternion.Euler(90, 0, 0), true, 95f);
        Shot("skyreach_sw", new Vector3(-120, 90, -150), Quaternion.LookRotation(new Vector3(115, -75, 150)), false, 0f);
        Shot("skyreach_ne", new Vector3(130, 95, 120), Quaternion.LookRotation(new Vector3(-130, -85, -125)), false, 0f);
        Shot("skyreach_under", new Vector3(-31, -5.5f, -60), Quaternion.LookRotation(new Vector3(0.35f, -0.03f, 1)), false, 0f);
        Shot("skyreach_climb", new Vector3(5, 22, -5), Quaternion.LookRotation(new Vector3(-40, -10, 35)), false, 0f);
    }
}
