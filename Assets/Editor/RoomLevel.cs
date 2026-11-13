using System.Collections.Generic;
using UnityEngine;
using static GreyboxKit;
using static KitLevel;

/// <summary>
/// Shared builder for indoor maps made of **rooms** (the Search &amp; Destroy maps: Facility, Cistern). A room is a
/// rectangle of 8 m grid cells with a floor storey (<see cref="Room.Base"/>: −1 underground, 0 ground, 1 upper) and a
/// height in storeys; rooms are joined by openings given per edge and storey. From that it builds:
///   - floors (patterned tiles or plain slabs; upper floors as slabs with optional hatches), ceilings (none where another
///     room's floor already covers the cell);
///   - walls storey by storey on every edge where the occupancy changes: the body stands in the empty cell, or between
///     two rooms in the one with the lower ceiling (ties: the later room), so a wall's column keeps to one side all the
///     way up; a band tops each storey unless a slab sits there; x walls stop 1 m short where the same body cell has a
///     solid z wall that storey;
///   - kit corner posts in every 1 × 1 corner square no wall end covers, so every room is sealed;
///   - balconies, lights, callouts, spawns and bomb sites, and a stairs headroom check.
/// Use: set <see cref="Rooms"/>, <see cref="Openings"/> and the grid origin with <see cref="Setup"/>, then call the parts.
/// </summary>
public static class RoomLevel
{
    public sealed class Room
    {
        public string Name; public int I0, I1, J0, J1, Base, Storeys = 2; public string Tile;
        public string Callout;   // The place name on the minimap (null: none)
        public bool Unlabelled;  // Named, but the name isn't written over it (a main's other rooms share one label)
        public Vector2Int[] Hatches = System.Array.Empty<Vector2Int>(); // Upper floors: cells with a Floor-Hole
        public float X0 => OX + 8f * I0; public float X1 => OX + 8f * (I1 + 1);
        public float Z0 => OZ + 8f * J0; public float Z1 => OZ + 8f * (J1 + 1);
        public float BaseY => 5f * Base;
        public int TopStorey => Base + Storeys;
        public float Top => 5f * TopStorey;
        public bool Has(Vector2Int c) => c.x >= I0 && c.x <= I1 && c.y >= J0 && c.y <= J1;
    }

    public static float OX, OZ;   // Cell (i, j) spans x = OX + 8i .. +8, z = OZ + 8j .. +8
    public static Room[] Rooms;
    private static readonly Dictionary<(Vector2Int, int), int> occ = new();
    private static readonly Dictionary<(Vector2Int, Vector2Int, int), char> openings = new();
    private static readonly HashSet<(Vector2Int, int)> slabs = new();   // (cell, slab bottom y)

    public static float X(int i) => OX + 8f * i + 4f;
    public static float Z(int j) => OZ + 8f * j + 4f;
    public static Vector2Int CellAt(float x, float z) => new(Mathf.FloorToInt((x - OX) / 8f), Mathf.FloorToInt((z - OZ) / 8f));
    public static int Occ(Vector2Int c, int s) => occ.TryGetValue((c, s), out int r) ? r : -1;
    private static (Vector2Int, Vector2Int, int) Key(Vector2Int a, Vector2Int b, int s) =>
        a.x < b.x || (a.x == b.x && a.y < b.y) ? (a, b, s) : (b, a, s);

    /// <summary>Reads the rooms and openings (D archway, O wide opening, per storey) and checks them.</summary>
    public static void Setup(Room[] rooms, (int ai, int aj, int bi, int bj, char kind, int storey)[] list, float ox, float oz)
    {
        OX = ox; OZ = oz; Rooms = rooms;
        occ.Clear(); openings.Clear(); slabs.Clear();
        for (int r = 0; r < rooms.Length; r++)
        {
            var room = rooms[r];
            for (int i = room.I0; i <= room.I1; i++)
                for (int j = room.J0; j <= room.J1; j++)
                {
                    var c = new Vector2Int(i, j);
                    for (int s = room.Base; s < room.TopStorey; s++)
                    {
                        if (occ.ContainsKey((c, s))) report.Add($"! {room.Name} overlaps {rooms[occ[(c, s)]].Name} at {c}, storey {s}");
                        occ[(c, s)] = r;
                    }
                    slabs.Add((c, Mathf.RoundToInt(room.Top) - 1));
                    slabs.Add((c, Mathf.RoundToInt(room.BaseY) - 1));
                }
        }
        foreach (var (ai, aj, bi, bj, kind, s) in list)
        {
            var a = new Vector2Int(ai, aj); var b = new Vector2Int(bi, bj);
            int ra = Occ(a, s), rb = Occ(b, s);
            if (ra < 0 || rb < 0 || ra == rb || (a - b).sqrMagnitude != 1) { report.Add($"! opening {a}–{b} (storey {s}) isn't between two rooms"); continue; }
            openings[Key(a, b, s)] = kind;
        }
    }

    /// <summary>A slab (bottom at <paramref name="bottomY"/>) over a cell that walls shouldn't put a band into (a balcony, a landing).</summary>
    public static void NoteSlab(Vector2Int cell, float bottomY) => slabs.Add((cell, Mathf.RoundToInt(bottomY)));

    // ─── Floors and ceilings ──────────────────────────────────────────────────

    public static void BuildFloors()
    {
        var g = Group("Floors");
        var upper = Group("Upper Floors");
        foreach (var room in Rooms)
        {
            if (room.Base > 0)
            {
                var hatches = new Vector2[room.Hatches.Length];
                for (int k = 0; k < hatches.Length; k++) hatches[k] = new Vector2(X(room.Hatches[k].x), Z(room.Hatches[k].y));
                Roof(upper, room.X0, room.X1, room.Z0, room.Z1, room.BaseY, hatches);
                continue;
            }
            var cells = new HashSet<Vector2>();
            for (int i = room.I0; i <= room.I1; i++) for (int j = room.J0; j <= room.J1; j++) cells.Add(new Vector2(X(i), Z(j)));
            Fill(g, cells, room.Tile, room.BaseY, false, room.X0, room.Z0);
        }
    }

    // A room's ceiling, except over cells where another room's floor starts at that height (it is the ceiling)
    public static void BuildCeilings()
    {
        var g = Group("Ceilings");
        foreach (var room in Rooms)
        {
            var self = room;
            Roof(g, room.X0, room.X1, room.Z0, room.Z1, room.Top, System.Array.Empty<Vector2>(), c =>
            {
                var cell = CellAt(c.x, c.y);
                foreach (var other in Rooms) if (other != self && other.Base == self.TopStorey && other.Has(cell)) return true;
                return false;
            });
        }
    }

    // ─── Walls ────────────────────────────────────────────────────────────────

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

    public static void BuildWalls()
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
            string id = s.Opening == 'D' ? "Archway-8m" : s.Opening == 'O' ? null
                      : mix[(((s.Floor.x * 7 + s.Floor.y * 3 + s.Storey) % mix.Length) + mix.Length) % mix.Length];
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
    // one inside a wall's body cell) and that no wall reaching that corner already covers. Kit posts, storey by storey,
    // up to the slab above or with the band's metre.
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
                if (!(lo ? seg.CutLo : seg.CutHi)) covered.Add(seg.Body);
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

    // ─── Balconies ────────────────────────────────────────────────────────────

    /// <summary>A balcony or ledge (deck at <paramref name="top"/>) over x0..x1 × z0..z1 along a wall, open on the
    /// <paramref name="open"/> side (N/S/E/W), on a beam and pillars along that edge; pillars within 6 m of a
    /// <paramref name="keepClear"/> position along the edge are left out (a stair's landing). Its cells are noted as
    /// slabs, so walls put no band through it.</summary>
    public static void Balcony(Transform g, float x0, float x1, float z0, float z1, float top, char open, params float[] keepClear) =>
        BalconyOver(g, x0, x1, z0, z1, top, open, top - 5f, keepClear);

    /// <summary>A <see cref="Balcony"/> whose pillars stand on a floor at <paramref name="floorY"/> (not one storey down).</summary>
    public static void BalconyOver(Transform g, float x0, float x1, float z0, float z1, float top, char open, float floorY, params float[] keepClear)
    {
        bool alongX = open == 'N' || open == 'S';
        if (alongX)
            for (float x = x0 + 4; x < x1; x += 8)
                for (float z = z0 + 2; z < z1; z += 4) { K(g, "Platform-8x4m", new Vector3(x, top - 1f, z), 0f); Ceiling(g, x, top - 1f, z, 0f, 1f, 0.5f); }
        else
            for (float z = z0 + 4; z < z1; z += 8)
                for (float x = x0 + 2; x < x1; x += 4) { K(g, "Platform-8x4m", new Vector3(x, top - 1f, z), 90f); Ceiling(g, x, top - 1f, z, 90f, 1f, 0.5f); }
        for (float x = x0 + 0.5f; x < x1; x += 8)
            for (float z = z0 + 0.5f; z < z1; z += 8) NoteSlab(CellAt(x, z), top - 1f);
        float edge = open switch { 'S' => z0 + 1f, 'N' => z1 - 1f, 'W' => x0 + 1f, _ => x1 - 1f };
        float a0 = alongX ? x0 : z0, a1 = alongX ? x1 : z1;
        for (float a = a0 + 4; a < a1; a += 8)
            K(g, "Beam-8m", alongX ? new Vector3(a, top - 2f, edge) : new Vector3(edge, top - 2f, a), alongX ? 0f : 90f);
        for (float a = a0 + 8; a < a1; a += 8)
        {
            bool clear = true;
            foreach (float k in keepClear) if (Mathf.Abs(a - k) < 6f) clear = false;
            // Pillar-Round-2m is 4 tall: up to the beam's underside
            if (clear) K(g, "Pillar-Round-2m", alongX ? new Vector3(a, floorY, edge) : new Vector3(edge, floorY, a), 0f, new Vector3(1f, (top - 2f - floorY) / 4f, 1f));
        }
    }

    // ─── Lights, callouts, spawns, sites ──────────────────────────────────────

    public static void BuildLights()
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

    public static void BuildCallouts()
    {
        var g = Group("Callouts");
        foreach (var room in Rooms)
        {
            if (room.Callout == null) continue;
            var go = new GameObject($"Callout {room.Callout}");
            go.transform.SetParent(g, false);
            go.transform.position = new Vector3((room.X0 + room.X1) / 2f, room.BaseY, (room.Z0 + room.Z1) / 2f);
            var callout = go.AddComponent<MapCallout>();
            callout.label = room.Callout;
            callout.showLabel = !room.Unlabelled;
            callout.size = new Vector3(room.X1 - room.X0, room.Top - room.BaseY, room.Z1 - room.Z0);
        }
        foreach (var room in Rooms)
            report.Add($"{room.Name}: {room.X1 - room.X0:0} × {room.Z1 - room.Z0:0} m, floor {room.BaseY:0}, ceiling {room.Top:0}");
    }

    /// <summary>Spawn points for one side, each looking at <paramref name="look"/>.</summary>
    public static void Spawns(Transform g, SpawnPoint.Side side, Vector3 look, params Vector3[] at)
    {
        for (int k = 0; k < at.Length; k++)
        {
            var go = new GameObject($"Spawn {side} {k + 1:00}");
            go.transform.SetParent(g, false);
            var lookAt = new Vector3(look.x, at[k].y, look.z);
            go.transform.SetPositionAndRotation(at[k] + Vector3.up * 0.1f, Quaternion.LookRotation(lookAt - at[k]));
            go.AddComponent<SpawnPoint>().side = side;
        }
    }

    /// <summary>A plant zone (<paramref name="size"/> × <paramref name="size"/>, 8 tall) whose floor is at <paramref name="centre"/>.y.</summary>
    public static void Site(Transform g, string letter, Vector3 centre, float size)
    {
        var go = new GameObject($"Site {letter}");
        go.transform.SetParent(g, false);
        go.transform.position = centre;
        var site = go.AddComponent<BombSite>();
        site.letter = letter;
        site.size = new Vector3(size, 8f, size);
        report.Add($"Site {letter}: plant zone {size:0} × {size:0} m at ({centre.x:0}, {centre.y:0}, {centre.z:0})");
    }

    /// <summary>The least clear height over a flight of stairs, from a line of raycasts up off its steps.</summary>
    public static void Headroom(string name, Vector3 bottom, Vector3 top)
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
}
