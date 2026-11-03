using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the Greybox test level from primitive blocks: a compact, enclosed 64 × 64 m arena
/// with four height tiers so chases can go up, down and around.
///
///   Tier -6  Sunken Yard (east)      a lower courtyard; falling in costs time, not a life
///   Tier  0  Ground                  spawn plaza, alleys, rooms to hide in
///   Tier  6  Catwalk ring + rooftops runs around the arena against the walls
///   Tier 12  Towers + high bridges   three towers joined by bridges with jump gaps
///
/// The arena is boxed in by 40 m walls and an invisible ceiling, so nobody can climb, grapple
/// or launch out of the map; the kill height only catches glitches.
///
/// Distances use the current movement (gravity 40, jump 16, sprint 12):
///   jump ≈ 3.2 m high · walk jump ≈ 6.4 m · sprint jump ≈ 9.6 m · crouch jump ≈ 8 m high
///   ledge grab reaches tops ≈ 6–7 m above the feet
/// Gaps are labelled EASY (walk jump), SPRINT (running jump) or ABILITY.
/// Everything walkable is on the structure layer (wall-run, climb, ledge grab, grapple, traps).
/// </summary>
public static class GreyboxBuilder
{
    public static readonly Vector3 SpawnCenter = new(0f, 0.5f, -14f);
    public const float KillHeight = -20f;

    // Heights of each tier's walking surface
    private const float Yard     = -6f;
    private const float Upper    = 6f;
    private const float High     = 12f;
    private const float RoomRoof = 6.5f;

    private const float WallTop  = 40f;
    private const float Slab     = 0.5f;

    private const string MaterialRoot = "Assets/Environment/Materials/";

    private static Material floorMat, wallMat, structureMat, coverMat, accentMat, walkwayMat, roomMat;
    private static int structureLayer;

    public static void Build(Transform root)
    {
        LoadMaterials();
        structureLayer = Mathf.Max(0, LayerMask.NameToLayer("structureLayer"));

        BuildShell(Group(root, "Shell"));
        BuildSpawnPlaza(Group(root, "Spawn_Plaza_South"));
        BuildCentralTower(Group(root, "Central_Tower"));
        BuildTowersAndBridges(Group(root, "Towers_And_Bridges"));
        BuildCatwalks(Group(root, "Catwalk_Ring"));
        BuildAlleys(Group(root, "Alleys_West"));
        BuildYard(Group(root, "Sunken_Yard_East"));
        BuildNorthEast(Group(root, "Warehouse_NorthEast"));
    }

    // ─── Shell: floors, boundary, ceiling ─────────────────────────────────────

    private static void BuildShell(Transform p)
    {
        // Ground everywhere except the sunken yard (x 14–32, z -12–14)
        Block(p, "Floor_Main",      -32, 14, -1, 0, -32, 32, floorMat);
        Block(p, "Floor_SouthEast",  14, 32, -1, 0, -32, -12, floorMat);
        Block(p, "Floor_NorthEast",  14, 32, -1, 0,  14, 32, floorMat);
        Block(p, "Floor_Yard",       14, 32, Yard - 1, Yard, -12, 14, floorMat);

        // Yard drop faces (under the ground slab edges)
        Block(p, "Yard_Face_West",  13, 14, Yard, -1, -12, 14, wallMat);
        Block(p, "Yard_Face_South", 14, 32, Yard, -1, -13, -12, wallMat);
        Block(p, "Yard_Face_North", 14, 32, Yard, -1,  14, 15, wallMat);

        // 40 m boundary walls: wall-runnable, far too tall to climb over
        Block(p, "Boundary_North", -33, 33, Yard - 4, WallTop,  32,  33, wallMat);
        Block(p, "Boundary_South", -33, 33, Yard - 4, WallTop, -33, -32, wallMat);
        Block(p, "Boundary_West",  -33, -32, Yard - 4, WallTop, -32, 32, wallMat);
        Block(p, "Boundary_East",   32, 33, Yard - 4, WallTop, -32, 32, wallMat);

        // Invisible ceiling (Default layer: can't be grappled or wall-run)
        var ceiling = Block(p, "Boundary_Ceiling", -33, 33, WallTop, WallTop + 1, -33, 33, null);
        ceiling.layer = 0;
        Object.DestroyImmediate(ceiling.GetComponent<MeshRenderer>());
    }

    // ─── Spawn plaza (south) ──────────────────────────────────────────────────

    private static void BuildSpawnPlaza(Transform p)
    {
        // Spawn ring is centred at (0, -14) with a 5 m radius — keep it clear

        // Tall posts either side for wall-jumps out of the plaza
        Block(p, "Post_West", -12, -10, 0, 8, -15, -13, structureMat);
        Block(p, "Post_East",  10,  12, 0, 8, -15, -13, structureMat);

        // 2 m crates: jumpable, break line of sight
        Crate(p, "Crate_SW", -9, -23);
        Crate(p, "Crate_SE",  8, -23);
        Crate(p, "Crate_W",  -9,  -5);
        Crate(p, "Crate_E",   8,  -4);

        // Bunker: small room to duck into, roof reachable with a jump from the crate
        HollowRoom(p, "Bunker", 14, 22, -26, -18, 0, 3, doors: "WN");
        Crate(p, "Crate_Bunker", 11, -21);
    }

    // ─── Central tower ────────────────────────────────────────────────────────

    private static void BuildCentralTower(Transform p)
    {
        // Ground room with three exits — the main hiding spot and a chase loop
        HollowRoom(p, "Tower_Room", -4, 4, 2, 10, 0, RoomRoof, doors: "SEW", roof: false);
        Block(p, "Tower_Roof", -4, 4, RoomRoof - Slab, RoomRoof, 2, 12, walkwayMat); // Overhangs north for the ramp

        // Top block: ledge-grab up from the roof to the high bridges
        Block(p, "Tower_Top", -2, 2, RoomRoof, High, 4, 8, structureMat);

        // Ramp up the west side onto the roof
        Ramp(p, "Ramp_TowerRoof", new Vector3(-15, 0, 11), new Vector3(-4, RoomRoof, 11), 2.5f);
    }

    // ─── Outer towers and high bridges ────────────────────────────────────────

    private static void BuildTowersAndBridges(Transform p)
    {
        Tower(p, "Tower_North", -3, 3, 22, 28, doors: "SE");
        Tower(p, "Tower_West", -27, -21, 3, 9, doors: "ES");

        // Central → North: EASY 4 m gap at height 12
        Bridge(p, "Bridge_North_A", -1, 1, 8, 13);
        Bridge(p, "Bridge_North_B", -1, 1, 17, 22);

        // Central → West: EASY 4 m gap
        Bridge(p, "Bridge_West_A", -10, -2, 5, 7);
        Bridge(p, "Bridge_West_B", -21, -14, 5, 7);

        // Central → Yard spire: SPRINT 8 m gap, with a long drop into the yard if you miss
        Bridge(p, "Bridge_East_A", 2, 8, 5, 7);
        Bridge(p, "Bridge_East_B", 16, 18, 5, 7);
        Block(p, "Yard_Spire", 18, 22, Yard, High, 4, 8, structureMat);
    }

    // A hollow room at ground level with a solid block on top up to the high tier
    private static void Tower(Transform p, string name, float x0, float x1, float z0, float z1, string doors)
    {
        var g = Group(p, name);
        HollowRoom(g, "Room", x0, x1, z0, z1, 0, RoomRoof, doors);
        Block(g, "Top", x0, x1, RoomRoof, High, z0, z1, structureMat);
    }

    private static void Bridge(Transform p, string name, float x0, float x1, float z0, float z1) =>
        Block(p, name, x0, x1, High - Slab, High, z0, z1, accentMat);

    // ─── Catwalk ring at height 6 ─────────────────────────────────────────────

    private static void BuildCatwalks(Transform p)
    {
        // South + West form one L, reached by the south ramp. The space underneath is a covered alley.
        Catwalk(p, "Catwalk_South",   -32,   0, -32, -29);
        Catwalk(p, "Catwalk_West_A",  -32, -29, -29,  -6);
        Catwalk(p, "Catwalk_West_B",  -32, -29,  -1,  24); // EASY 5 m gap
        Ramp(p, "Ramp_Catwalk_South", new Vector3(12, 0, -30.5f), new Vector3(0, Upper, -30.5f), 3);

        // North + East form the other L, reached by the north ramp
        Catwalk(p, "Catwalk_North",   -20,  32,  29,  32);
        Catwalk(p, "Catwalk_East_A",   29,  32,   4,  29);
        Catwalk(p, "Catwalk_East_B",   29,  32, -32,  -2); // SPRINT 6 m gap over the yard
        Ramp(p, "Ramp_Catwalk_North", new Vector3(-32, 0, 30.5f), new Vector3(-20, Upper, 30.5f), 3);
    }

    private static void Catwalk(Transform p, string name, float x0, float x1, float z0, float z1) =>
        Block(p, name, x0, x1, Upper - Slab, Upper, z0, z1, walkwayMat);

    // ─── West alleys ──────────────────────────────────────────────────────────

    private static void BuildAlleys(Transform p)
    {
        // Two parallel 5 m walls 5.5 m apart: wall-run chains
        Block(p, "Alley_Wall_A",  -22.5f, -22, 0, 5, -24,   0, structureMat);
        Block(p, "Alley_Wall_B1", -16.5f, -16, 0, 5, -20, -12, structureMat);
        Block(p, "Alley_Wall_B2", -16.5f, -16, 0, 5,  -9,   3, structureMat); // Side door at z -12..-9

        // Roofed section turns the alley into a dark tunnel; its roof is a 5.5 m perch
        Block(p, "Alley_Tunnel_Roof", -22, -16.5f, 5, 5.5f, -16, -8, walkwayMat);

        // Cross wall closes the alley off from the west catwalk's undercroft, leaving a gap by the wall
        Block(p, "Alley_Cross", -29, -22.5f, 0, 5, -12.25f, -11.75f, structureMat);

        // Vaultable half wall at the alley's south mouth
        Block(p, "Alley_HalfWall", -16, -10, 0, 1.2f, -22.25f, -21.75f, coverMat);
    }

    // ─── Sunken yard ──────────────────────────────────────────────────────────

    private static void BuildYard(Transform p)
    {
        // Ramps out at both ends
        Ramp(p, "Ramp_Yard_South", new Vector3(26, Yard, -10.5f), new Vector3(14, 0, -10.5f), 3);
        Ramp(p, "Ramp_Yard_North", new Vector3(26, Yard,  12.5f), new Vector3(14, 0,  12.5f), 3);

        // Stepping stones at ground height across the yard: EASY 3 m gaps
        Block(p, "Yard_Stone_1", 17, 19, Yard, 0, -3, -1, accentMat);
        Block(p, "Yard_Stone_2", 22, 24, Yard, 0, -3, -1, accentMat);
        Block(p, "Yard_Stone_3", 27, 29, Yard, 0, -3, -1, accentMat); // Ledge-grab the east catwalk from here

        // Shipping container to hide in
        HollowRoom(p, "Yard_Container", 23, 30, -8.5f, -4, Yard, 4, doors: "W");
    }

    // ─── North-east warehouse ─────────────────────────────────────────────────

    private static void BuildNorthEast(Transform p)
    {
        // Big room with two entrances; its roof is EASY 3 m from the north catwalk
        HollowRoom(p, "Warehouse", 8, 20, 18, 26, 0, RoomRoof, doors: "SW");

        // Interior shelving to weave around
        Block(p, "Warehouse_Shelf_A", 11, 12, 0, 3, 19.5f, 24.5f, coverMat);
        Block(p, "Warehouse_Shelf_B", 15, 16, 0, 3, 19.5f, 24.5f, coverMat);

        Crate(p, "Crate_NE_1",  8, 15);
        Crate(p, "Crate_NE_2", -9, 16);
    }

    // ─── Building blocks ──────────────────────────────────────────────────────

    /// <summary>
    /// A room with 0.5 m walls. <paramref name="doors"/> lists sides ("N", "S", "E", "W") that get a
    /// centred 2.5 × 3 m doorway.
    /// </summary>
    private static void HollowRoom(Transform p, string name, float x0, float x1, float z0, float z1,
                                   float y0, float height, string doors, bool roof = true)
    {
        const float t = 0.5f;
        var g = Group(p, name);
        float y1 = y0 + height;

        RoomWall(g, "Wall_S", x0, x1, z0, z0 + t, y0, y1, alongX: true,  door: doors.Contains("S"));
        RoomWall(g, "Wall_N", x0, x1, z1 - t, z1, y0, y1, alongX: true,  door: doors.Contains("N"));
        RoomWall(g, "Wall_W", x0, x0 + t, z0 + t, z1 - t, y0, y1, alongX: false, door: doors.Contains("W"));
        RoomWall(g, "Wall_E", x1 - t, x1, z0 + t, z1 - t, y0, y1, alongX: false, door: doors.Contains("E"));

        if (roof) Block(g, "Roof", x0, x1, y1 - Slab, y1, z0, z1, walkwayMat);
    }

    private static void RoomWall(Transform p, string name, float x0, float x1, float z0, float z1,
                                 float y0, float y1, bool alongX, bool door)
    {
        if (!door)
        {
            Block(p, name, x0, x1, y0, y1, z0, z1, roomMat);
            return;
        }

        const float doorWidth = 2.5f, doorHeight = 3f;
        float a0 = alongX ? x0 : z0, a1 = alongX ? x1 : z1;
        float mid = (a0 + a1) / 2f, d0 = mid - doorWidth / 2f, d1 = mid + doorWidth / 2f;

        void Piece(string suffix, float from, float to, float bottom, float top)
        {
            if (alongX) Block(p, name + suffix, from, to, bottom, top, z0, z1, roomMat);
            else        Block(p, name + suffix, x0, x1, bottom, top, from, to, roomMat);
        }

        Piece("_Left",   a0, d0, y0, y1);
        Piece("_Right",  d1, a1, y0, y1);
        if (y1 - y0 > doorHeight) Piece("_Lintel", d0, d1, y0 + doorHeight, y1);
    }

    private static void Crate(Transform p, string name, float x, float z) =>
        Block(p, name, x - 1, x + 1, 0, 2, z - 1, z + 1, coverMat);

    private static void LoadMaterials()
    {
        floorMat     = LoadMat("Large/Floor");
        wallMat      = LoadMat("Large/WallLight");
        structureMat = LoadMat("Large/WallOffdark");
        roomMat      = LoadMat("Large/WallDark");
        walkwayMat   = LoadMat("Large/WallOfflight");
        coverMat     = LoadMat("Concrete/LightBase");
        accentMat    = LoadMat("Concrete/AccentOrange");
    }

    private static Material LoadMat(string name) =>
        AssetDatabase.LoadAssetAtPath<Material>(MaterialRoot + name + ".mat");

    private static Transform Group(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    /// <summary>An axis-aligned box spanning the given world-space extents.</summary>
    private static GameObject Block(Transform parent, string name,
                                    float x0, float x1, float y0, float y1, float z0, float z1, Material mat)
    {
        var center = new Vector3((x0 + x1) / 2f, (y0 + y1) / 2f, (z0 + z1) / 2f);
        var size   = new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0));
        return Cube(parent, name, center, Quaternion.identity, size, mat);
    }

    /// <summary>A ramp whose walkable top surface runs from <paramref name="low"/> to <paramref name="high"/>.</summary>
    private static GameObject Ramp(Transform parent, string name, Vector3 low, Vector3 high, float width)
    {
        const float thickness = 0.5f;
        Vector3    slope    = high - low;
        Quaternion rotation = Quaternion.LookRotation(slope.normalized, Vector3.up);
        Vector3    center   = (low + high) / 2f - rotation * Vector3.up * (thickness / 2f);
        var size = new Vector3(width, thickness, slope.magnitude + 0.5f); // Slight overlap at both ends
        return Cube(parent, name, center, rotation, size, walkwayMat);
    }

    private static GameObject Cube(Transform parent, string name, Vector3 center, Quaternion rotation, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name  = name;
        go.layer = structureLayer;
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(center, rotation);
        go.transform.localScale = size;
        if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
        return go;
    }
}
