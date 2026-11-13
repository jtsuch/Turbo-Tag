using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static GreyboxKit;
using static KitLevel;

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
/// Walls are built storey by storey on every edge where the occupancy changes; the body stands in the empty cell, or
/// between two rooms in the one with the lower ceiling (so a wall never jumps sides on the way up); corners where wall
/// ends don't meet get 1 × 1 kit posts, so every room is sealed.
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

    private sealed class Room
    {
        public string Name; public int I0, I1, J0, J1, Base, Storeys = 2; public string Tile;
        public string Callout;   // The place name on the minimap (null: none)
        public bool Unlabelled;  // Named, but the name isn't written over it (a main's other rooms share one label)
        public float X0 => OX + 8f * I0; public float X1 => OX + 8f * (I1 + 1);
        public float Z0 => OZ + 8f * J0; public float Z1 => OZ + 8f * (J1 + 1);
        public float BaseY => 5f * Base;
        public int TopStorey => Base + Storeys;
        public float Top => 5f * TopStorey;
    }

    // Ties between two rooms with the same ceiling put the wall in the later one, so the big rooms come first and the
    // upper corridors last
    private static readonly Room[] Rooms =
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
    private static readonly (int ai, int aj, int bi, int bj, char kind, int storey)[] Openings =
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

    private static readonly Dictionary<(Vector2Int, int), int> occ = new();
    private static readonly Dictionary<(Vector2Int, Vector2Int, int), char> openings = new();
    private static readonly HashSet<(Vector2Int, int)> slabs = new();   // (cell, slab bottom y)
    private static float X(int i) => OX + 8f * i + 4f;
    private static float Z(int j) => OZ + 8f * j + 4f;
    private static int Occ(Vector2Int c, int s) => occ.TryGetValue((c, s), out int r) ? r : -1;
    private static (Vector2Int, Vector2Int, int) Key(Vector2Int a, Vector2Int b, int s) =>
        a.x < b.x || (a.x == b.x && a.y < b.y) ? (a, b, s) : (b, a, s);

    [MenuItem("Turbo Tag/Levels/Search & Destroy/Rebuild Facility")]
    public static void Build()
    {
        Reset(20261113, "Assets/Environment/Generated/Facility_Unused.asset", "Facility", G);
        if (!System.IO.File.Exists(ScenePath)) AssetDatabase.CopyAsset("Assets/Scenes/Levels/SearchDestroy/Standoff.unity", ScenePath);
        var scene = Begin(ScenePath, RootName);
        var geometry = root.parent;
        for (int i = geometry.childCount - 1; i >= 0; i--)
            if (geometry.GetChild(i) != root) Object.DestroyImmediate(geometry.GetChild(i).gameObject);

        occ.Clear(); openings.Clear(); slabs.Clear();
        for (int r = 0; r < Rooms.Length; r++)
        {
            var room = Rooms[r];
            for (int i = room.I0; i <= room.I1; i++)
                for (int j = room.J0; j <= room.J1; j++)
                {
                    var c = new Vector2Int(i, j);
                    for (int s = room.Base; s < room.TopStorey; s++)
                    {
                        if (occ.ContainsKey((c, s))) report.Add($"! {room.Name} overlaps {Rooms[occ[(c, s)]].Name} at {c}, storey {s}");
                        occ[(c, s)] = r;
                    }
                    slabs.Add((c, Mathf.RoundToInt(room.Top) - 1));
                    if (room.Base > 0) slabs.Add((c, Mathf.RoundToInt(room.BaseY) - 1));
                }
        }
        foreach (var (ai, aj, bi, bj, kind, s) in Openings)
        {
            var a = new Vector2Int(ai, aj); var b = new Vector2Int(bi, bj);
            int ra = Occ(a, s), rb = Occ(b, s);
            if (ra < 0 || rb < 0 || ra == rb || (a - b).sqrMagnitude != 1) { report.Add($"! opening {a}–{b} (storey {s}) isn't between two rooms"); continue; }
            openings[Key(a, b, s)] = kind;
        }

        BuildFloors();
        BuildWalls();
        BuildCeilings();
        BuildBalconiesAndStairs();
        DressRooms();
        BuildSpawnsAndSites();
        BuildLights();
        CheckAccess();
        Headroom("Defense spawn stairs", new Vector3(-66, G, 0), new Vector3(-56, L5, 0));
        Headroom("Mid balcony stairs", new Vector3(-6, G, -8), new Vector3(-16, L5, -8));
        Headroom("A site balcony stairs", new Vector3(-60, G, 42), new Vector3(-60, L5, 52));
        Headroom("B site ledge stairs", new Vector3(-50, G, -56), new Vector3(-40, L5, -56));
        Headroom("B main stairs", new Vector3(12, G, -46), new Vector3(12, L5, -36));
        report.Add($"cubbies: {cubbies.Count}");
        foreach (var c in cubbies) report.Add("  " + c);
        CheckOverlaps();
        Bounds(Vector3.zero, 400f, -25f, 30f);
        PlaceSpawner(new Vector3(8f, 0.1f, 0f), -20f);
        Atmosphere.SunnyDay(root, new Vector3(1f, 0f, 0.35f), new Bounds(new Vector3(0, 7, -4), new Vector3(170, 14, 140)), windOn: false);

        Finish(scene, ReportPath, "Facility", RenderOverviews);
    }

    // ─── Floors, walls, ceilings ──────────────────────────────────────────────

    private static void BuildFloors()
    {
        var g = Group("Floors");
        var upper = Group("Upper Floors");
        foreach (var room in Rooms)
        {
            if (room.Base > 0) { Roof(upper, room.X0, room.X1, room.Z0, room.Z1, room.BaseY, System.Array.Empty<Vector2>()); continue; }
            var cells = new HashSet<Vector2>();
            for (int i = room.I0; i <= room.I1; i++) for (int j = room.J0; j <= room.J1; j++) cells.Add(new Vector2(X(i), Z(j)));
            Fill(g, cells, room.Tile, G, false, room.X0, room.Z0);
        }
    }

    private sealed class Segment
    {
        public Vector2Int Floor, Body, Dir; public int Storey; public char Opening; public bool CutLo, CutHi;
        public bool Solid => Opening != 'O';
        public bool AlongX => Dir.y != 0;
        public Vector3 Outward => new(Dir.x, 0f, Dir.y);
        // The edge's two corners as grid vertices (vertex (a, b) is at x = OX + 8a, z = OZ + 8b); Lo is the west/south one
        public Vector2Int Lo => AlongX ? new Vector2Int(Floor.x, Mathf.Max(Floor.y, Body.y)) : new Vector2Int(Mathf.Max(Floor.x, Body.x), Floor.y);
        public Vector2Int Hi => Lo + (AlongX ? Vector2Int.right : Vector2Int.up);
    }

    // Storey by storey, every edge between a room's cell and a cell that isn't the same room at that storey gets a wall.
    // Its body stands in the empty cell, or (between two rooms) in the one with the lower ceiling, ties in the later
    // room, so a wall's column keeps to one side all the way up. A band tops each storey unless a slab sits there.
    private static void BuildWalls()
    {
        var g = Group("Walls");
        var segments = new List<Segment>();
        var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.right, Vector2Int.left };
        foreach (var ((c, s), r) in occ)
            foreach (var d in dirs)
            {
                var n = c + d;
                int rn = Occ(n, s);
                if (rn == r) continue;
                if (rn >= 0)
                {
                    int tc = Rooms[r].TopStorey, tn = Rooms[rn].TopStorey;
                    bool bodyInN = tn < tc || (tn == tc && rn > r);
                    if (!bodyInN) continue;   // Built from the other side
                }
                openings.TryGetValue(Key(c, n, s), out char kind);
                segments.Add(new Segment { Floor = c, Body = n, Dir = d, Storey = s, Opening = kind });
            }

        // Where one body cell holds solid walls on two adjacent sides in a storey, the one along x stops 1 m short there
        var sides = new HashSet<(Vector2Int, Vector2Int, int)>();
        foreach (var s in segments) if (s.Solid) sides.Add((s.Body, s.Floor - s.Body, s.Storey));
        foreach (var s in segments)
            if (s.AlongX && s.Solid)
            {
                s.CutLo = sides.Contains((s.Body, Vector2Int.left, s.Storey));
                s.CutHi = sides.Contains((s.Body, Vector2Int.right, s.Storey));
            }

        string[] mix = { "Wall-Mid-8m", "Wall-Vented-8m", "Wall-Grid-8m", "Wall-Mid-8m" };
        int count = 0;
        foreach (var s in segments)
        {
            float ex = X(s.Floor.x) + s.Dir.x * 4f, ez = Z(s.Floor.y) + s.Dir.y * 4f, y = 5f * s.Storey;
            float len = 8f;
            if (s.CutLo) { len -= 1f; ex += 0.5f; }
            if (s.CutHi) { len -= 1f; ex -= 0.5f; }
            string id = s.Opening == 'D' ? "Archway-8m" : s.Opening == 'O' ? null : mix[(s.Floor.x * 7 + s.Floor.y * 3 + s.Storey) % mix.Length];
            float yaw = Facing(s.Outward);
            if (id != null) K(g, id, new Vector3(ex, y, ez), yaw, new Vector3(len / 8f, 1f, 1f));
            if (!slabs.Contains((s.Body, Mathf.RoundToInt(y) + 4)))
                K(g, "Wall-Mid-8m", new Vector3(ex, y + 4f, ez), yaw, new Vector3(len / 8f, 0.25f, 1f));
            if (s.Opening != '\0')
                Tunnel($"{Rooms[Occ(s.Floor, s.Storey)].Name} → {Rooms[Occ(s.Body, s.Storey)].Name}",
                       new Vector3(ex, y, ez) - s.Outward * 2.5f, new Vector3(ex, y, ez) + s.Outward * 3.5f, "run");
            count++;
        }
        report.Add($"walls: {count} segments");
        SealCorners(segments);
    }

    // At every grid vertex a solid wall ends on, fill each 1 × 1 corner square that should be wall (an empty cell's, or
    // one inside a wall's body cell) and that no wall reaching that corner already covers: the holes where wall ends met
    // without overlapping. Kit posts, storey by storey, up to the slab above or with the band's metre.
    private static void SealCorners(List<Segment> segments)
    {
        var g = Group("Corner Posts");
        var touching = new Dictionary<(Vector2Int, int), List<(Segment seg, bool lo)>>();
        foreach (var s in segments)
        {
            if (!s.Solid) continue;
            foreach (var (v, lo) in new[] { (s.Lo, true), (s.Hi, false) })
            {
                if (!touching.TryGetValue((v, s.Storey), out var list)) touching[(v, s.Storey)] = list = new List<(Segment, bool)>();
                list.Add((s, lo));
            }
        }
        int posts = 0;
        foreach (var ((v, st), list) in touching)
        {
            var bodies = new HashSet<Vector2Int>();
            var covered = new HashSet<Vector2Int>();
            foreach (var (seg, lo) in list)
            {
                bodies.Add(seg.Body);
                bool cut = lo ? seg.CutLo : seg.CutHi;
                if (!cut) covered.Add(seg.Body);
            }
            foreach (var q in new[] { v + new Vector2Int(-1, -1), v + new Vector2Int(0, -1), v + new Vector2Int(-1, 0), v })
            {
                if (covered.Contains(q)) continue;
                if (Occ(q, st) >= 0 && !bodies.Contains(q)) continue;   // Open floor of a room
                float vx = OX + 8f * v.x, vz = OZ + 8f * v.y;
                // The post's body lies in its local −x/−z quadrant: pivot at the square's +x/+z corner
                float px = q.x == v.x ? vx + 1f : vx, pz = q.y == v.y ? vz + 1f : vz;
                int y = 5 * st;
                float h = slabs.Contains((q, y + 4)) ? 4f : 5f;
                K(g, "WallCornerPost-1x4m", new Vector3(px, y, pz), 0f, new Vector3(1f, h / 4f, 1f));
                posts++;
            }
        }
        report.Add($"corner posts: {posts}");
    }

    private static void BuildCeilings()
    {
        var g = Group("Ceilings");
        foreach (var room in Rooms) Roof(g, room.X0, room.X1, room.Z0, room.Z1, room.Top, System.Array.Empty<Vector2>());
    }

    // ─── Balconies and stairs (verticality on the edges, outside the plant zones) ─────────────────────

    /// <summary>A balcony (deck at <paramref name="top"/>) over x0..x1 × z0..z1 along a wall, open on the
    /// <paramref name="open"/> side (N/S/E/W), on a beam and pillars along that edge; pillars within 2 m of a
    /// <paramref name="keepClear"/> position along the edge are left out (a stair's landing).</summary>
    private static void Balcony(Transform g, float x0, float x1, float z0, float z1, float top, char open, params float[] keepClear)
    {
        bool alongX = open == 'N' || open == 'S';
        if (alongX)
            for (float x = x0 + 4; x < x1; x += 8)
                for (float z = z0 + 2; z < z1; z += 4) { K(g, "Platform-8x4m", new Vector3(x, top - 1f, z), 0f); Ceiling(g, x, top - 1f, z, 0f, 1f, 0.5f); }
        else
            for (float z = z0 + 4; z < z1; z += 8)
                for (float x = x0 + 2; x < x1; x += 4) { K(g, "Platform-8x4m", new Vector3(x, top - 1f, z), 90f); Ceiling(g, x, top - 1f, z, 90f, 1f, 0.5f); }
        float edge = open switch { 'S' => z0 + 1f, 'N' => z1 - 1f, 'W' => x0 + 1f, _ => x1 - 1f };
        float a0 = alongX ? x0 : z0, a1 = alongX ? x1 : z1;
        for (float a = a0 + 4; a < a1; a += 8)
            K(g, "Beam-8m", alongX ? new Vector3(a, top - 2f, edge) : new Vector3(edge, top - 2f, a), alongX ? 0f : 90f);
        for (float a = a0 + 8; a < a1; a += 8)
        {
            bool clear = true;
            foreach (float k in keepClear) if (Mathf.Abs(a - k) < 6f) clear = false;
            if (clear) K(g, "Pillar-Round-2m", alongX ? new Vector3(a, top - 5f, edge) : new Vector3(edge, top - 5f, a), 0f, new Vector3(1f, 0.75f, 1f));
        }
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

    // ─── Spawns, sites, lights ────────────────────────────────────────────────

    private static void BuildSpawnsAndSites()
    {
        var g = Group("Spawn Points");
        void Spawn(string name, Vector3 at, Vector3 look, SpawnPoint.Side side)
        {
            var go = new GameObject(name);
            go.transform.SetParent(g, false);
            go.transform.SetPositionAndRotation(at + Vector3.up * 0.1f, Quaternion.LookRotation(look - at));
            go.AddComponent<SpawnPoint>().side = side;
        }
        // Defenders round their stairs (x −66..−56, z −4..4); attackers in two rows
        var defend = new[] { new Vector2(-76, -14), new Vector2(-76, 0), new Vector2(-76, 14), new Vector2(-62, -14), new Vector2(-62, 14), new Vector2(-70, -7) };
        for (int k = 0; k < 6; k++)
        {
            float z = -12f + (k % 3) * 12f, x = k < 3 ? 74f : 64f;
            Spawn($"Spawn {k + 1:00} Defend", new Vector3(defend[k].x, G, defend[k].y), new Vector3(0, G, defend[k].y), SpawnPoint.Side.Defend);
            Spawn($"Spawn {k + 7:00} Attack", new Vector3(x, G, z), new Vector3(0, G, z), SpawnPoint.Side.Attack);
        }
        var callouts = Group("Callouts");
        foreach (var room in Rooms)
        {
            if (room.Callout == null) continue;
            var go = new GameObject($"Callout {room.Callout}");
            go.transform.SetParent(callouts, false);
            go.transform.position = new Vector3((room.X0 + room.X1) / 2f, room.BaseY, (room.Z0 + room.Z1) / 2f);
            var callout = go.AddComponent<MapCallout>();
            callout.label = room.Callout;
            callout.showLabel = !room.Unlabelled;
            callout.size = new Vector3(room.X1 - room.X0, room.Top - room.BaseY, room.Z1 - room.Z0);
        }
        var sites = Group("Bomb Sites");
        // Plant zones: the 16 × 16 in each site's middle (balconies, ledges and stairs are outside)
        foreach (var (letter, cx, cz) in new[] { ("A", -48f, 44f), ("B", -48f, -44f) })
        {
            var go = new GameObject($"Site {letter}");
            go.transform.SetParent(sites, false);
            go.transform.position = new Vector3(cx, 0f, cz);
            var site = go.AddComponent<BombSite>();
            site.letter = letter;
            site.size = new Vector3(16f, 8f, 16f);
            report.Add($"Site {letter}: plant zone 16 × 16 m at ({cx:0}, {cz:0})");
        }
        foreach (var room in Rooms)
            report.Add($"{room.Name}: {room.X1 - room.X0:0} × {room.Z1 - room.Z0:0} m, floor {room.BaseY:0}, ceiling {room.Top:0}");
    }

    private static void BuildLights()
    {
        var g = Group("Lights");
        int n = 0;
        foreach (var room in Rooms)
            for (int i = room.I0; i <= room.I1; i += 2)
                for (int j = room.J0; j <= room.J1; j += 2)
                {
                    float ix = i + 1 <= room.I1 ? X(i) + 4f : X(i), jz = j + 1 <= room.J1 ? Z(j) + 4f : Z(j);
                    Lamp(g, new Vector3(ix, room.Top - 2f, jz), room.Storeys == 1 ? 12f : 18f);
                    n++;
                }
        report.Add($"lights: {n}");
    }

    // The least clear height over a flight of stairs, from a line of raycasts up off its steps
    private static void Headroom(string name, Vector3 bottom, Vector3 top)
    {
        Physics.SyncTransforms();
        float least = float.MaxValue;
        for (int i = 0; i <= 12; i++)
        {
            var at = Vector3.Lerp(bottom, top, i / 12f) + Vector3.up * 1.5f;
            if (!Physics.Raycast(at, Vector3.down, out RaycastHit floor, 4f, ~0, QueryTriggerInteraction.Ignore)) continue;
            float room = Physics.Raycast(floor.point + Vector3.up * 0.05f, Vector3.up, out RaycastHit roof, 20f, ~0, QueryTriggerInteraction.Ignore)
                ? roof.distance + 0.05f : 20f;
            least = Mathf.Min(least, room);
        }
        report.Add(least < 3.2f ? $"! {name}: only {least:0.0} m of headroom" : $"{name}: at least {least:0.0} m of headroom");
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
