using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using static GreyboxKit;

/// <summary>
/// Builds the Fracture level (Assets/Scenes/Levels/Fracture.unity) from code: the broken-apart map. The mainland is
/// split into five shards by rifts that drop to the kill plane, and the shards themselves have pits cut straight
/// through them (holes in the middle of the land, not just gaps between lands). Height comes in more steps than
/// Skyreach: 0 (ground) → 8 (the West shard, the South bluff, the East terrace) → 16 (the High mesa, the Watch rock,
/// the East block) → 26 (the North tower) → 30 (the Spire) → 36 (the Crown and the floating Sky deck).
///
/// Danger is placed, not everywhere: every pit inside a shard has a low orange curb (0.5 m: walking never drops you
/// in, a hop does), the bridges over the rifts have curbs, and the buildings, courtyards and tunnels sit away from
/// the edges. The open edges are the rifts themselves, the jump gaps and the curb openings where a crossing is meant
/// (a plank over the Crack, a shuttle over the Sinkhole).
///
///   Centre isle (0): the Spire (a cross-tunnelled base 10, a middle 20, a top 30 with a ledge and a lift); bridges
///     south and east, a flat bridge west into a tunnel through the West shard, and a 6 m jump north.
///   South shard (0): the Well and the Crack (plank across), the South bluff (8) with a ramp and a building climb,
///     a bridge from the bluff to the West shard at 8.
///   East shard (0): the Sinkhole (a shuttle rides across it) and the Slot, a bunker tunnel, the East terrace (8) and
///     the East block (16).
///   West shard (8): a tunnel in from the centre bridge to a sunken cave courtyard with a ramp up, a pit through the
///     whole shard, the Watch rock (16), huts; a long ramp bridge down to the North shard.
///   North shard (0): the Twin pits, the High mesa (16, a tunnel and hall inside), the North tower (26), the Crown (36);
///     the Sky lift from the ground to the Sky deck (36), whose bridge (one hop) reaches the Crown.
///
/// Re-running rebuilds Geometry/Fracture; review output goes to Library/TurboTag/scratch/fracture_report.txt and
/// fracture_*.png. Menu: Turbo Tag/Levels/Rebuild Fracture (editor job: BuildFracture).
/// </summary>
public static class FractureBuilder
{
    private const string ScenePath  = "Assets/Scenes/Levels/Fracture.unity";
    private const string RootName   = "Fracture";
    private const string ReportPath = "Library/TurboTag/scratch/fracture_report.txt";

    private const float T8 = 8f, T16 = 16f, T26 = 26f, T36 = 36f;
    private const float Low = 2.5f, Bottom = -16f;

    [MenuItem("Turbo Tag/Levels/Rebuild Fracture")]
    public static void Build()
    {
        // The level template is gone: a new Fracture scene starts as a copy of Skyreach's (LevelSystems, light, sky),
        // and everything under Geometry except Fracture's own root is cleared
        if (!System.IO.File.Exists(ScenePath)) AssetDatabase.CopyAsset("Assets/Scenes/Levels/Skyreach.unity", ScenePath);
        var scene = Begin(ScenePath, RootName);
        var geometry = root.parent;
        for (int i = geometry.childCount - 1; i >= 0; i--)
            if (geometry.GetChild(i) != root) Object.DestroyImmediate(geometry.GetChild(i).gameObject);

        BuildCentre();
        BuildSouth();
        BuildEast();
        BuildWest();
        BuildNorth();
        BuildBridges();
        Bounds(Vector3.zero, 320f, -40f, 70f);
        Spawns(SpawnSpots, Vector3.zero);
        PlaceSpawner(new Vector3(-9, 0.1f, -9), -30f);

        Finish(scene, ReportPath, "Fracture", RenderOverviews);
    }

    private static (float, float, float, float, float, float) V(float x0, float x1, float y0, float y1, float z0, float z1) =>
        (x0, x1, y0, y1, z0, z1);

    // Pits through the shards (x0, x1, z0, z1): straight down to the kill plane
    private static readonly Vector4 Well      = new(-40, -32, -52, -44);
    private static readonly Vector4 Crack     = new(-15, 5, -42, -38);
    private static readonly Vector4 Sinkhole  = new(35, 47, -40, -28);
    private static readonly Vector4 Slot      = new(28, 32, -60, -54);
    private static readonly Vector4 TwinA     = new(-14, -6, 36, 44);
    private static readonly Vector4 TwinB     = new(2, 10, 44, 52);
    private static readonly Vector4 WestPit   = new(-60, -54, 22, 28);

    // ─── Centre isle (0) and the Spire ────────────────────────────────────────

    private static void BuildCentre()
    {
        var g = Group("Centre Isle");
        Land(g, "Centre Isle", -12, 12, -12, 12);

        // Spire base (10): a cross of running tunnels through it
        Carve(g, "Spire Base", -6, 6, 0, 10, -6, 6, Kind.Floor, V(-6, 6, 0, 4, -2, 2), V(-2, 2, 0, 4, -6, 6));
        Tunnel("Spire Tunnel E-W", new Vector3(-7, 0, 0), new Vector3(7, 0, 0), "run");
        Tunnel("Spire Tunnel N-S", new Vector3(0, 0, -7), new Vector3(0, 0, 7), "run");
        Lamp(g, new Vector3(0, 3.2f, 0), 10f);
        // Hard way up (NE corner): step 2.5 → block 5.5 → base 10
        Ledge(g, "Spire Step", 9, 11, 3, 6, Low);
        Box(g, "Spire Block", 6, 9, 0, 5.5f, 3, 6, Kind.Floor);
        // Middle (20): step 12.5 → block 15.5 on the base's east edge → 20
        Box(g, "Spire Middle", -4, 4, 10, 20, -4, 4, Kind.Floor);
        Ledge(g, "Spire Mid Step", 4, 6, -6, -4, 10 + Low);
        Box(g, "Spire Mid Block", 4, 6, 10, 15.5f, -4, 0, Kind.Floor);
        // Top (30) with a ledge sticking out east; the Spire lift rides beside it from the isle
        Box(g, "Spire Top", -3, 3, 20, 30, -3, 3, Kind.Floor);
        Box(g, "Spire Crown Ledge", 3, 7, 29, 30, -3, 3, Kind.Floor);
        Mover(g, "Spire Lift", new Vector3(9, -1, 0), new Vector3(9, 29, 0), new Vector3(4, 1, 4), 6f);
        Kiosk(g, "Isle Kiosk SW", -9, -9, 0, 2.5f, 2.5f, 1.2f);
        Prop(g, "HalfWall-2m", new Vector3(-9, 0, 8), 45f);
        Lamp(g, new Vector3(0, 34, 0), 24f);
    }

    // ─── South shard (0) and the South bluff (8) ──────────────────────────────

    private static void BuildSouth()
    {
        var g = Group("South Shard");
        Land(g, "South Shard", -65, 15, -65, -28, Well, Crack);
        Curb(g, "Well Curb", Well, 0);
        // The Crack: a plank across its middle (the curb opens there on both long sides)
        Curb(g, "Crack Curb", Crack, 0, ('S', -6.5f, -3.5f), ('N', -6.5f, -3.5f));
        Box(g, "Crack Plank", -6, -4, -0.4f, 0, Crack.z, Crack.w, Kind.Floor);

        Hut(g, "South Hall", -60, -48, -62, -52, 0, 4.5f, 'N', 'E');
        Carve(g, "Pump House", -12, -2, 0, 5, -62, -50, Kind.Floor, V(-9, -5, 0, 4, -62, -50));
        Tunnel("Pump House Tunnel", new Vector3(-7, 0, -63), new Vector3(-7, 0, -49), "run");
        Ledge(g, "Pump House Step", -2, 0, -58, -55, Low);
        Kiosk(g, "South Kiosk 1", -24, -58, 0, 4, 4, 3f);
        Kiosk(g, "South Kiosk 2", 6, -56, 0, 3, 3, Low);
        Kiosk(g, "South Kiosk 3", -20, -32, 0, 3, 6, Low);
        Prop(g, "HalfWall-2m", new Vector3(-28, 0, -40), 70f);
        Prop(g, "HalfWall-2m", new Vector3(8, 0, -46), 0f);
        Prop(g, "Crate-1x1m", new Vector3(-44, 0, -60), 20f, 1.4f);
        Prop(g, "Crate-1x1m", new Vector3(10, 0, -34), -15f, 1.2f);
        Box(g, "South Parapet", -65, 15, 0, 1.2f, -65, -64, Kind.Wall);

        // South bluff (8): easy ramp up its east side, hard building climb on its south face
        Box(g, "South Bluff", -65, -45, 0, T8, -40, -28, Kind.Floor);
        RampWayX(g, "Bluff Ramp", -38, -34, -28, -45, 0, T8, 0, 0);
        Carve(g, "Bluff Climb House", -60, -52, 0, 5, -46, -40, Kind.Floor, V(-60, -52, 0, 4, -45, -41));
        Tunnel("Bluff Climb House", new Vector3(-61, 0, -43), new Vector3(-51, 0, -43), "run");
        Ledge(g, "Bluff Climb Step", -58, -55, -49, -46, Low);
        Hut(g, "Bluff Hut", -64, -56, -38, -30, T8, 4.5f, 'E', 'N');
        Lamp(g, new Vector3(-30, 9, -50), 24f);
        Lamp(g, new Vector3(-55, T8 + 7, -34), 18f);
    }

    // ─── East shard (0), East terrace (8), East block (16) ────────────────────

    private static void BuildEast()
    {
        var g = Group("East Shard");
        Land(g, "East Shard", 22, 65, -65, 5, Sinkhole, Slot);
        // The Sinkhole: the curb opens east and west where the shuttle docks; the Slot is closed all round
        Curb(g, "Sinkhole Curb", Sinkhole, 0, ('W', -36.5f, -31.5f), ('E', -36.5f, -31.5f));
        Mover(g, "Sinkhole Shuttle", new Vector3(37, -1, -34), new Vector3(45, -1, -34), new Vector3(4, 1, 4), 3f);
        Curb(g, "Slot Curb", Slot, 0);

        Carve(g, "East Bunker", 38, 52, 0, 5, -12, -2, Kind.Floor, V(38, 52, 0, 4, -9, -5));
        Tunnel("East Bunker Tunnel", new Vector3(37, 0, -7), new Vector3(53, 0, -7), "run");
        Ledge(g, "East Bunker Step", 44, 47, -14, -12, Low);
        Hut(g, "East Shed", 24, 32, -20, -12, 0, 4.5f, 'N', 'E');
        Kiosk(g, "East Kiosk 1", 28, -45, 0, 3, 5, Low);
        Kiosk(g, "East Kiosk 2", 58, -20, 0, 4, 4, 3f);
        Kiosk(g, "East Kiosk 3", 40, -58, 0, 3, 3, Low);
        Prop(g, "HalfWall-2m", new Vector3(30, 0, -32), 90f);
        Prop(g, "HalfWall-2m", new Vector3(56, 0, -6), 20f);
        Prop(g, "Crate-1x1m", new Vector3(50, 0, -25), 10f, 1.3f);
        Box(g, "East Parapet", 64, 65, 0, 1.2f, -45, 5, Kind.Wall);

        // East terrace (8): easy ramp along its west face; East block (16) on it with a building climb
        Box(g, "East Terrace", 52, 65, 0, T8, -65, -45, Kind.Floor);
        RampWayZ(g, "Terrace Ramp", 48, 52, -25, -45, 0, T8, 0, 4);
        Carve(g, "Terrace Climb House", 54, 62, 0, 5, -45, -39, Kind.Floor, V(54, 62, 0, 4, -44, -40));
        Tunnel("Terrace Climb House", new Vector3(53, 0, -42), new Vector3(63, 0, -42), "run");
        Ledge(g, "Terrace Climb Step", 56, 59, -39, -37, Low);
        Box(g, "East Block", 58, 65, T8, T16, -65, -55, Kind.Floor);
        Ledge(g, "East Block Step", 55, 58, -53, -50, T8 + Low);
        Box(g, "East Block House", 58, 65, T8, T8 + 5, -55, -50, Kind.Floor);
        Kiosk(g, "East Block Kiosk", 61, -61, T16, 2.5f, 2.5f, 1.2f);
        Lamp(g, new Vector3(42, 9, -30), 24f);
        Lamp(g, new Vector3(58, T8 + 8, -52), 20f);
        Lamp(g, new Vector3(45, 7, -8), 18f);
    }

    // ─── West shard (8), the cave courtyard and the Watch rock (16) ───────────

    private static void BuildWest()
    {
        var g = Group("West Shard");
        // One tall block: a running tunnel in from the centre bridge (at 0) to a cave courtyard open to the sky, and
        // a pit through the whole shard
        Carve(g, "West Shard", -65, -25, Bottom, T8, -18, 35, Kind.Floor,
              V(-40, -25, 0, 4.5f, -2.5f, 2.5f),             // Tunnel from the bridge
              V(-52, -40, 0, T8, -12, 12),                   // Cave courtyard (sunk 8 m, open above)
              V(WestPit.x, WestPit.y, Bottom, T8, WestPit.z, WestPit.w));
        Tunnel("West Tunnel", new Vector3(-24, 0, 0), new Vector3(-41, 0, 0), "run");
        Curb(g, "West Pit Curb", WestPit, T8);
        Curb(g, "Courtyard Rim", new Vector4(-52, -40, -12, 12), T8, ('S', -50.5f, -45.5f), ('N', -50.5f, -45.5f));
        // Up out of the courtyard: a ramp (0 → 8) along its west side onto a head at the north end
        RampWayZ(g, "Courtyard Ramp", -51, -47, -10, 8, 0, T8, 0, 4);
        Kiosk(g, "Courtyard Kiosk", -43, -6, 0, 3, 3, Low);
        Prop(g, "Crate-1x1m", new Vector3(-44, 0, 6), 25f, 1.3f);
        Lamp(g, new Vector3(-46, 6, 0), 16f);

        Hut(g, "West Hut", -64, -56, -14, -6, T8, 4.5f, 'E', 'N');
        Carve(g, "West Tunnel Block", -36, -28, T8, T8 + 5, 14, 26, Kind.Floor, V(-36, -28, T8, T8 + 4, 18, 22));
        Tunnel("West Top Tunnel", new Vector3(-37, T8, 20), new Vector3(-27, T8, 20), "run");
        Ledge(g, "West Block Step", -34, -31, 26, 28, T8 + Low);
        Kiosk(g, "West Kiosk 1", -33, -12, T8, 4, 4, 3f);
        Kiosk(g, "West Kiosk 2", -46, 26, T8, 3, 3, Low);
        Prop(g, "HalfWall-2m", new Vector3(-58, T8, 10), 0f);

        // Watch rock (16): a building climb on its east face
        Box(g, "Watch Rock", -65, -57, T8, T16, 0, 14, Kind.Floor);
        Box(g, "Watch Climb House", -57, -52, T8, T8 + 5, 8, 14, Kind.Floor);
        Ledge(g, "Watch Climb Step", -52, -50, 9, 12, T8 + Low);
        Lamp(g, new Vector3(-45, T8 + 8, 24), 20f);
        Lamp(g, new Vector3(-60, T16 + 6, 7), 16f);
    }

    // ─── North shard (0), High mesa (16), North tower (26), Crown (36) ────────

    private static void BuildNorth()
    {
        var g = Group("North Shard");
        Land(g, "North Shard", -20, 65, 18, 65, TwinA, TwinB);
        Curb(g, "Twin Pit A Curb", TwinA, 0);
        Curb(g, "Twin Pit B Curb", TwinB, 0);
        Hut(g, "North Hut", -16, -6, 52, 62, 0, 4.5f, 'S', 'E');
        Kiosk(g, "North Kiosk 1", 0, 30, 0, 4, 4, 3f);
        Kiosk(g, "North Kiosk 2", -2, 60, 0, 3, 3, Low);
        Kiosk(g, "North Kiosk 3", 12, 26, 0, 3, 3, Low);
        Prop(g, "HalfWall-2m", new Vector3(-12, 0, 26), 30f);
        Prop(g, "HalfWall-2m", new Vector3(14, 0, 58), 90f);
        Prop(g, "Crate-1x1m", new Vector3(50, 0, 26), 15f, 1.4f);
        Box(g, "North Parapet", -20, 20, 0, 1.2f, 64, 65, Kind.Wall);

        // High mesa (16): a tunnel in from its west face to a hall with a south door
        Carve(g, "High Mesa", 20, 65, 0, T16, 40, 65, Kind.Floor,
              V(20, 36, 0, 4, 47, 51),        // Tunnel from the west face
              V(36, 48, 0, 6, 44, 56),        // Hall
              V(40, 44, 0, 4, 40, 44));       // South door
        Tunnel("Mesa Tunnel", new Vector3(19, 0, 49), new Vector3(37, 0, 49), "run");
        Tunnel("Mesa Hall Door", new Vector3(42, 0, 39), new Vector3(42, 0, 46), "run");
        Lamp(g, new Vector3(42, 4.5f, 50), 12f);
        // Easy: a ramp along the west face (0 → 16), arched over the tunnel mouth. Hard: a climb on the south face
        RampWayZ(g, "Mesa Ramp", 16, 20, 22, 52, 0, T16, 0, 3, 49);
        Tunnel("Mesa Ramp Arch", new Vector3(15, 0, 49), new Vector3(21, 0, 49), "run");
        Box(g, "Mesa Climb House", 24, 32, 0, 5, 34, 40, Kind.Floor);
        Box(g, "Mesa Climb Upper", 24, 28, 5, 10, 37, 40, Kind.Floor);
        Ledge(g, "Mesa Climb Step", 32, 34, 34, 37, Low);
        Kiosk(g, "Mesa Kiosk", 30, 44, T16, 3, 3, Low);
        Prop(g, "HalfWall-2m", new Vector3(36, T16, 60), 0f);

        // North tower (26): a ramp across the mesa top to its SW corner; a building climb on its west face
        Box(g, "North Tower", 44, 65, T16, T26, 54, 65, Kind.Floor);
        RampWayX(g, "Tower Ramp", 50, 54, 24, 44, T16, T26, T16, 4);
        Box(g, "Tower Climb House", 38, 44, T16, T16 + 5, 56, 62, Kind.Floor);
        Box(g, "Tower Climb Upper", 41, 44, T16 + 5, T16 + 9, 56, 62, Kind.Floor);
        Ledge(g, "Tower Climb Step", 36, 38, 58, 61, T16 + Low);
        Hut(g, "Tower Hut", 45, 53, 56, 64, T26, 4.5f, 'S', 'E');

        // Crown (36) on the tower: a building climb on its west face
        Box(g, "Crown", 54, 65, T26, T36, 56, 65, Kind.Floor);
        Box(g, "Crown Climb House", 54, 58, T26, T26 + 5, 54, 56, Kind.Floor); // Against the south face, on the tower
        Box(g, "Crown Climb Upper", 54, 58, T26 + 5, T26 + 9, 55, 56, Kind.Floor);
        Ledge(g, "Crown Climb Step", 58, 61, 54, 56, T26 + Low);
        Hut(g, "Lookout", 56, 64, 57, 64, T36, 4.5f, 'W', 'S');

        // Sky deck (36), floating over the North shard; the Sky lift from the ground; the bridge to the Crown
        Box(g, "Sky Deck", 30, 40, T36 - 4, T36, 24, 34, Kind.Floor);
        Box(g, "Sky Deck Underside", 32, 38, T36 - 8, T36 - 4, 26, 32, Kind.Dark);
        Mover(g, "Sky Lift", new Vector3(28, -1, 29), new Vector3(28, T36 - 1, 29), new Vector3(4, 1, 4), 6f);
        Box(g, "Sky Bridge N", 36, 39, T36 - 0.5f, T36, 34, 61, Kind.Floor);
        Box(g, "Sky Bridge E1", 39, 45, T36 - 0.5f, T36, 58, 61, Kind.Floor);
        Box(g, "Sky Bridge E2", 48, 54, T36 - 0.5f, T36, 58, 61, Kind.Floor);
        gaps.Add(("Sky bridge hop", new Vector3(45, T36, 59.5f), new Vector3(48, T36, 59.5f)));
        Kiosk(g, "Sky Deck Kiosk", 33, 27, T36, 2.5f, 2.5f, 1.2f);

        Lamp(g, new Vector3(0, 9, 40), 26f);
        Lamp(g, new Vector3(40, T16 + 8, 48), 22f);
        Lamp(g, new Vector3(56, T36 + 6, 60), 20f);
        Lamp(g, new Vector3(35, T36 + 6, 29), 18f);
    }

    // ─── Bridges over the rifts ───────────────────────────────────────────────

    private static void BuildBridges()
    {
        var g = Group("Bridges");
        CurbedBridgeZ(g, "South Bridge", -3, 3, -28, -12, 0);     // South shard ↔ Centre
        CurbedBridgeX(g, "East Bridge", 12, 22, -3, 3, 0);        // Centre ↔ East shard
        CurbedBridgeX(g, "West Bridge", -25, -12, -3, 3, 0);      // Centre ↔ the West shard's tunnel
        CurbedBridgeX(g, "South-East Bridge", 15, 22, -50, -44, 0);
        CurbedBridgeZ(g, "North-East Bridge", 30, 36, 5, 18, 0);
        CurbedBridgeZ(g, "Bluff Bridge", -60, -55, -28, -18, T8);  // South bluff ↔ West shard, at 8
        // West shard (8) ↔ North shard (0): a long ramp bridge
        RampWayX(g, "West Ramp Bridge", 26, 30, -8, -25, 0, T8, 0, 0);

        // Jumps: the short way across a rift, for the brave
        gaps.Add(("Centre to North shard", new Vector3(0, 0, 12), new Vector3(0, 0, 18)));
        gaps.Add(("South to East shard", new Vector3(15, 0, -32), new Vector3(22, 0, -32)));
    }

    // ─── Spawns ───────────────────────────────────────────────────────────────

    private static readonly (string name, float x, float z, float y)[] SpawnSpots =
    {
        ("Centre Isle SW",   -9,  -6, 0),
        ("Centre Isle NE",    7,   9, 0),
        ("South Yard",      -20, -50, 0),
        ("South East",        6, -48, 0),
        ("South Bluff",     -50, -34, T8),
        ("East Yard",        40, -50, 0),
        ("East Bunker",      30,  -4, 0),
        ("East Terrace",     55, -60, T8),
        ("West Shard",      -40,  26, T8),
        ("West Hut",        -60,  -2, T8),
        ("North Yard",        0,  58, 0),
        ("North East",       52,  30, 0),
        ("High Mesa",        30,  58, T16),
    };

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>A shard of mainland at 0: a 4 m slab (with pits) and a dark underside below it (with the same pits).</summary>
    private static void Land(Transform g, string name, float x0, float x1, float z0, float z1, params Vector4[] pits)
    {
        Slab(g, name, x0, x1, -4, 0, z0, z1, Kind.Floor, pits);
        var under = new List<Vector4>();
        foreach (var p in pits) under.Add(new Vector4(p.x - 0.5f, p.y + 0.5f, p.z - 0.5f, p.w + 0.5f));
        Slab(g, name + " Underside", x0 + 2, x1 - 2, Bottom, -4, z0 + 2, z1 - 2, Kind.Dark, under.ToArray());
    }

    private const float CurbH = 0.5f, CurbW = 0.6f;

    /// <summary>
    /// A low orange curb round a pit (0.5 m: walking can't fall in, a hop can), with openings given as
    /// (side, from, to) along that side (x for N/S, z for E/W) where a crossing is meant.
    /// </summary>
    private static void Curb(Transform g, string name, Vector4 hole, float floor, params (char side, float a, float b)[] open)
    {
        var c = Group(name, g);
        void Side(char side, float a0, float a1, float f0, float f1, bool alongX)
        {
            var cuts = new List<(float a, float b)>();
            foreach (var o in open) if (o.side == side) cuts.Add((o.a, o.b));
            cuts.Sort((p, q) => p.a.CompareTo(q.a));
            float s = a0;
            int n = 0;
            void Piece(float from, float to)
            {
                if (to - from < 0.2f) return;
                if (alongX) Box(c, $"{name} {side}{++n}", from, to, floor, floor + CurbH, f0, f1, Kind.Ledge);
                else Box(c, $"{name} {side}{++n}", f0, f1, floor, floor + CurbH, from, to, Kind.Ledge);
            }
            foreach (var (a, b) in cuts) { Piece(s, Mathf.Min(a, a1)); s = Mathf.Max(s, b); }
            Piece(s, a1);
        }
        Side('S', hole.x - CurbW, hole.y + CurbW, hole.z - CurbW, hole.z, true);
        Side('N', hole.x - CurbW, hole.y + CurbW, hole.w, hole.w + CurbW, true);
        Side('W', hole.z, hole.w, hole.x - CurbW, hole.x, false);
        Side('E', hole.z, hole.w, hole.y, hole.y + CurbW, false);
    }

    // Bridges: a 1 m deck at the given height with a curb along each side
    private static void CurbedBridgeZ(Transform g, string name, float x0, float x1, float z0, float z1, float y)
    {
        var b = Group(name, g);
        Box(b, name, x0, x1, y - 1, y, z0, z1, Kind.Floor);
        Box(b, name + " Curb W", x0, x0 + CurbW, y, y + CurbH, z0, z1, Kind.Ledge);
        Box(b, name + " Curb E", x1 - CurbW, x1, y, y + CurbH, z0, z1, Kind.Ledge);
        Box(b, name + " Truss", x0 + 1, x1 - 1, y - 3, y - 1, z0, z1, Kind.Dark);
    }

    private static void CurbedBridgeX(Transform g, string name, float x0, float x1, float z0, float z1, float y)
    {
        var b = Group(name, g);
        Box(b, name, x0, x1, y - 1, y, z0, z1, Kind.Floor);
        Box(b, name + " Curb S", x0, x1, y, y + CurbH, z0, z0 + CurbW, Kind.Ledge);
        Box(b, name + " Curb N", x0, x1, y, y + CurbH, z1 - CurbW, z1, Kind.Ledge);
        Box(b, name + " Truss", x0, x1, y - 3, y - 1, z0 + 1, z1 - 1, Kind.Dark);
    }

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
    /// at the given z positions), and a head block continues past the top for <paramref name="head"/> m.
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
        Shot("fracture_top", new Vector3(0, 170, 0), Quaternion.Euler(90, 0, 0), true, 80f);
        Shot("fracture_sw", new Vector3(-110, 80, -120), Quaternion.LookRotation(new Vector3(110, -70, 120)), false, 0f);
        Shot("fracture_ne", new Vector3(115, 90, 110), Quaternion.LookRotation(new Vector3(-115, -80, -110)), false, 0f);
        Shot("fracture_pits", new Vector3(-10, 14, -70), Quaternion.LookRotation(new Vector3(-5, -10, 30)), false, 0f);
        Shot("fracture_centre", new Vector3(30, 22, -30), Quaternion.LookRotation(new Vector3(-30, -8, 30)), false, 0f);
    }
}
