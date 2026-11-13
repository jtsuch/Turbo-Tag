using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using static GreyboxKit;

/// <summary>
/// Shared helpers for levels built from Justin's modular kit (Assets/Environment/Prefabs) on the kit standard
/// (Docs/KitStandards.md): placing pieces (K, Tile, Slab, Wall, Post, Facade, RoofCells, Ceiling), props sized to the
/// penguin, zip lines and movers, the hide list (Cubby), the floating island under the ground (BuildIsland: skirt, dirt,
/// propellers) and the self-review (CheckAccess, CheckOverlaps, InsideTunnel). Used by FoundryBuilder and
/// FractureBuilder; call <see cref="Reset"/> at the start of each build.
/// </summary>
public static class KitLevel
{
    private static readonly HashSet<float> groundTops = new();
    private static string islandMeshPath, islandMeshName;
    private static float GroundY;

    /// <summary>Starts a build: the wall mix's seed, where the island's dirt mesh goes, and which floor tops count as
    /// ground the island carries (the first is the main ground level).</summary>
    public static void Reset(int seed, string meshPath, string meshName, params float[] grounds)
    {
        rng = new System.Random(seed);
        cubbies.Clear();
        lastWall = lastUpper = null;
        island.Clear();
        islandTop.Clear();
        skirtOpenings.Clear();
        islandMeshPath = meshPath;
        islandMeshName = meshName;
        groundTops.Clear();
        foreach (var y in grounds) groundTops.Add(y);
        GroundY = grounds[0];
    }

    public const string KitFolder  = "Assets/Environment/Prefabs/";

    public static readonly Vector3 N = Vector3.forward, S = Vector3.back, E = Vector3.right, W = Vector3.left;

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

    public static System.Random rng;
    public static readonly string[] LowerWalls =
        { "Wall-Grid-8m", "Wall-Hazard-8m", "Wall-Mid-8m", "Wall-Vented-8m", "WallRunPanel-8m", "WallWindow-8m", "Wall-Pipes-8m" };
    // Its arrows point up: only directly under a top players can climb onto (a 4 m wall under a deck or roof), never at random
    public const string ClimbWall = "Wall-Climb-8m";
    public static readonly string[] UpperWalls = { "Wall-Upper-8m", "Wall-Grid-8m", "Wall-Mid-8m", "Wall-Vented-8m" };
    public static string lastWall, lastUpper;

    // A random pick from the palette, mostly not the same as the last one (Wall-Pipes may repeat)
    public static string Mix(string[] palette, ref string last)
    {
        string id;
        int tries = 0;
        do id = palette[rng.Next(palette.Length)];
        while (id == last && id != "Wall-Pipes-8m" && ++tries < 20);
        return last = id;
    }

    public static string LowerWall() => Mix(LowerWalls, ref lastWall);
    public static string UpperWall() => Mix(UpperWalls, ref lastUpper);

    /// <summary>
    /// A zip line from <paramref name="topBase"/> (where its high anchor stands) to <paramref name="bottomBase"/> (the low
    /// anchor): two ZipAnchor-6m posts turned to face each other (their arms point along local −X), a cable between the
    /// arm tips, and the ride (`World/ZipLine`) on the high one.
    /// </summary>
    public static void ZipLineBetween(Transform g, Vector3 topBase, Vector3 bottomBase)
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
    public static void RoofCells(Transform g, float x0, float x1, float z0, float z1, float top, Vector2[] hatches, Vector2[] coffers) =>
        RoofCells(g, x0, x1, z0, z1, top, hatches, coffers, null);

    /// <summary>The same, leaving out every cell (by its centre) that <paramref name="skip"/> picks: courtyards, shafts.</summary>
    public static void RoofCells(Transform g, float x0, float x1, float z0, float z1, float top, Vector2[] hatches, Vector2[] coffers,
                                 System.Func<Vector2, bool> skip)
    {
        for (float x = x0 + 4; x < x1; x += 8)
            for (float z = z0 + 4; z < z1; z += 8)
            {
                var cell = new Vector2(x, z);
                if (skip != null && skip(cell)) continue;
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

    public const float CofferHeight = 2.1f;

    // Every hiding spot the build adds, with what fits in it: the report lists them
    public static readonly List<string> cubbies = new();

    public static void Cubby(string what, Vector3 at, string fits) => cubbies.Add($"{what} at ({at.x:0}, {at.y:0}, {at.z:0}): {fits}");

    // ─── Props sized to the penguin (2.9 m tall, 1.4 m wide) ──────────────────

    public const float ContainerScale = 4f / 3f;                    // 8 × 3.5 × 3.3: a penguin fits inside; one grid cell long
    public const float ContainerHeight = 2.6f * ContainerScale;
    public const float BarrierScale = 1.5f;                         // 6 × 1.8 × 1.5: chest-high cover, a plain hop
    public const float CrateSize = 1.5f;                            // Waist high
    public const float ShedScale = 1.5f;                            // Shed-4x4m's door is 2.6 m: ×1.5 → 3.9

    public static GameObject Container(Transform g, float x, float y, float z, float yaw) =>
        K(g, "CargoContainer-6m", new Vector3(x, y, z), yaw, Vector3.one * ContainerScale);

    public static GameObject Barrier(Transform g, float x, float y, float z, float yaw) =>
        K(g, "Barrier-4m", new Vector3(x, y, z), yaw, Vector3.one * BarrierScale);

    /// <summary>A floor slab of any size centred on (x, z) with its top at <paramref name="top"/>; ground ones are recorded
    /// for the island (every 8 m cell they cover).</summary>
    public static GameObject Slab(Transform g, string id, float x, float z, float top, float yaw = 0f)
    {
        var slab = K(g, id, new Vector3(x, top - 1f, z), yaw);
        if (groundTops.Contains(top))
        {
            var rs = slab.GetComponentsInChildren<Renderer>();
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            for (float cx = Mathf.Round(b.min.x) + 4f; cx < b.max.x; cx += 8f)
                for (float cz = Mathf.Round(b.min.z) + 4f; cz < b.max.z; cz += 8f)
                    RecordGround(CellOf(cx, cz), top);
        }
        return slab;
    }

    /// <summary>
    /// A 9 m facade panel on a footprint edge (two storeys: up to the roof slab at 9..10): 'W' window, 'D' arched door,
    /// 'P' pipe run, 'M' mixed, 'S' one tall solid panel; a mixed upper panel above from <paramref name="upperFrom"/> (4:
    /// stretched to 9; 5: standing on a slab that crosses the wall line at 4..5), all from <paramref name="baseY"/>.
    /// </summary>
    public static void Facade(Transform g, float x, float z, Vector3 outward, char kind, float upperFrom = 4f, bool upper = true, float baseY = 0f)
    {
        if (kind == 'S') { Wall(g, "Wall-Tall-8m", x, baseY, z, outward); return; }
        string lower = kind == 'W' ? "WallWindow-8m" : kind == 'P' ? "Wall-Pipes-8m" : kind == 'D' ? "Archway-8m"
                     : kind == 'N' ? "Wall-Niche-8m" : kind == 'Q' ? "Wall-NichePass-8m" : LowerWall();
        Wall(g, lower, x, baseY, z, outward);
        if (upper) Wall(g, UpperWall(), x, baseY + upperFrom, z, outward, (9f - upperFrom) / 4f);
    }

    public static void Link(TeleportPad from, TeleportPad to, float exitYaw)
    {
        var so = new SerializedObject(from);
        so.FindProperty("partner").objectReferenceValue = to;
        so.ApplyModifiedPropertiesWithoutUndo();
        var so2 = new SerializedObject(to);
        so2.FindProperty("exitYaw").floatValue = exitYaw;
        so2.ApplyModifiedPropertiesWithoutUndo();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    // ─── The floating island: a skirt round the edge, dirt underneath, propellers holding it up ──────────

    // Every 8 m ground cell (floor at 0 or the service level), recorded by Tile(); cell k spans 8k..8k + 8
    public static readonly HashSet<Vector2Int> island = new();
    public const float SkirtBottom = -16f;                         // The skirt: 16 m, its top flush with the floor (0)

    // Each ground cell's highest floor top: an island whose ground is lower (a dock at −5) hangs its skirt and dirt
    // that much lower
    public static readonly Dictionary<Vector2Int, float> islandTop = new();
    /// <summary>Edges (cell, outward direction) that get no skirt panel: a passage out through the cliff. The builder
    /// closes round the opening itself.</summary>
    public static readonly HashSet<(Vector2Int, Vector2Int)> skirtOpenings = new();

    // Set while a roof is laid: roof slabs are never ground the island carries, whatever their height
    private static bool layingRoof;

    public static void RecordGround(Vector2Int cell, float top)
    {
        if (layingRoof) return;
        island.Add(cell);
        islandTop[cell] = islandTop.TryGetValue(cell, out float was) ? Mathf.Max(was, top) : top;
    }

    public static float TopOf(Vector2Int cell) => islandTop.TryGetValue(cell, out float top) ? top : 0f;
    public const float DirtStep = 2f, DirtSlope = 1.5f, DirtPlateau = 32f;
    public static Vector2Int CellOf(float x, float z) => new(Mathf.FloorToInt(x / 8f), Mathf.FloorToInt(z / 8f));

    public static void BuildIsland()
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
            BuildDirt(g, IsOuter, pieces[k], k == 0 ? islandMeshPath : islandMeshPath.Replace(".asset", $"_{k}.asset"));
    }

    // Skirt panels (16 m tall flat faces to climb back up) hang round the outer edge just outside it, their tops flush
    // with the floor: a 1 m rim, which the walls on the map's edge stand on. A 1 × 1 post (a panel cut down) fills each
    // convex corner; runs along z stop 1 m short where the edge turns outward, so panels never cross
    public static void BuildSkirt(Transform parent, System.Func<Vector2Int, bool> isOuter, Vector2Int[] dirs)
    {
        var g = Group("Skirt", parent);
        var done = new HashSet<(Vector2Int, Vector2Int)>();
        var cells = new List<Vector2Int>(island);
        cells.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x)); // Runs start at their low end
        float YawOf(Vector2Int d) => d == Vector2Int.up ? 0f : d == Vector2Int.right ? 90f : d == Vector2Int.down ? 180f : 270f;
        Vector3 Centre(Vector2Int c) => new(c.x * 8f + 4f, SkirtBottom + TopOf(c), c.y * 8f + 4f);
        bool Open(Vector2Int c, Vector2Int d) => skirtOpenings.Contains((c, d));
        Vector3 V(Vector2Int v) => new(v.x, 0f, v.y);
        int panels = 0, posts = 0;
        foreach (var c in cells)
            foreach (var d in dirs)
            {
                if (!isOuter(c + d) || Open(c, d) || !done.Add((c, d))) continue;
                var t = d.x == 0 ? Vector2Int.right : Vector2Int.up;
                var end = c;
                while (island.Contains(end + t) && isOuter(end + t + d) && !Open(end + t, d) && TopOf(end + t) == TopOf(c))
                { end += t; done.Add((end, d)); }
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
    public static void BuildDirt(Transform parent, System.Func<Vector2Int, bool> isOuter, HashSet<Vector2Int> cells, string meshPath)
    {
        // A piece whose ground is lower (a dock) hangs lower: everything below is measured from its top
        float pieceTop = float.MinValue;
        foreach (var c in cells) pieceTop = Mathf.Max(pieceTop, TopOf(c));
        float skirtFoot = SkirtBottom + pieceTop;
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
                bottom[key] = skirtFoot - 3f - Mathf.Min(d * DirtSlope, DirtPlateau) + n * (d * DirtSlope < DirtPlateau ? 4f : 1.5f);
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
                lobe = Mathf.Max(lobe, skirtFoot - 3f - dist[key] * 2.4f);
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
                    float top = OuterSub(i + dx, j + dz) ? skirtFoot : pieceTop - 1f;
                    Vector3 a = Under(ax, az), b = Under(bx, bz);
                    Quad(sideTris, new Vector3(a.x, top, a.z), new Vector3(b.x, top, b.z), b, a, new Vector3(dx, 0, dz), true);
                }
            }

        var mesh = new Mesh { name = islandMeshName, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
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
    public static GameObject Propeller(Transform g, string id, Vector3 hubTop, float capHeight, float scale, float degreesPerSecond)
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
    public static void PropsUnder(Transform g, GameObject piece, float scale, params Vector2[] offsets)
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

    public static readonly Dictionary<string, GameObject> kit = new();

    /// <summary>A kit piece with its pivot at <paramref name="pivot"/> (kit pivots: bottom centre), turned by <paramref name="yaw"/>.</summary>
    public static GameObject K(Transform parent, string id, Vector3 pivot, float yaw, Vector3? scale = null)
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
    public static GameObject Tile(Transform g, string id, float x, float z, float top, float yaw = 0f)
    {
        if (groundTops.Contains(top)) RecordGround(CellOf(x, z), top); // The ground the floating island carries
        return K(g, id, new Vector3(x, top - 1f, z), yaw);
    }

    // ─── Walls on the kit standard: pivot on the inner face, the body behind it ─────────────────────────

    // The yaw that turns a wall's face (its −Z side, where the body is) toward outward
    public static float Facing(Vector3 outward) => Mathf.Atan2(-outward.x, -outward.z) * Mathf.Rad2Deg;

    /// <summary>A wall panel on a footprint edge (its middle at x, z), standing just outside it facing outward.</summary>
    public static GameObject Wall(Transform g, string id, float x, float y, float z, Vector3 outward, float yScale = 1f) =>
        K(g, id, new Vector3(x, y, z), Facing(outward), new Vector3(1f, yScale, 1f));

    /// <summary>The 1 × 1 post filling the square outside a convex footprint corner, where two outward walls meet.</summary>
    public static void Post(Transform g, float x, float y, float z, Vector3 outA, Vector3 outB, float height)
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
    public static void Ceiling(Transform g, float x, float slabBottom, float z, float yaw = 0f, float sx = 1f, float sz = 1f)
    {
        bool odd = (Mathf.RoundToInt(x / 4f) + Mathf.RoundToInt(z / 4f)) % 2 != 0;
        var c = K(g, "Floor-Plate-8m", new Vector3(x, slabBottom - (odd ? 0.01f : 0f), z), yaw, new Vector3(sx * 1.08f, -0.02f, sz * 1.08f));
        c.name = "Ceiling";
        foreach (var col in c.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
        foreach (var r in c.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    /// <summary>A walk-through check for a prop with its own raised floor inside (a container): the path starts on the
    /// floor found at the path's middle, and must fit a running penguin.</summary>
    public static void InsideTunnel(string name, Vector3 from, Vector3 to, string kind = "run")
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
    public static void CheckAccess()
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
    public static void CheckOverlaps()
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
    public static void KitMover(Transform parent, string name, Vector3 aPivot, Vector3 bPivot, float speed)
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

    // ─── Buildings, floors, roofs, curbs, bridges, spawn spacing (from FractureBuilder, Nov 13) ────────

    public static bool Inside(Vector4 r, Vector2 c) => c.x > r.x && c.x < r.y && c.y > r.z && c.y < r.w;

    /// <summary>
    /// Lays a set of 8 m cells (floor top <paramref name="top"/>) with the biggest pieces that fit: 32 × 32 blocks lined up
    /// with the area's origin first, then any 32 × 32, 32 × 16 either way, then single cells (plain: a Platform-8x4 pair;
    /// patterned: the room's 8 m tile, or its 32 × 32 slab). <paramref name="ceiling"/>: a light liner under every cell
    /// (roofs seen from inside).
    /// </summary>
    public static void Fill(Transform g, HashSet<Vector2> cells, string tile, float top, bool ceiling, float ox, float oz)
    {
        string big = tile == null ? "Floor-32x32m" : tile == "Floor-Tiles-8m" ? "Floor-32x32m-Tiles" : tile == "Floor-Inlay-8m" ? "Floor-32x32m-Inlay" : null;
        if (ceiling) foreach (var c in cells) Ceiling(g, c.x, top - 1f, c.y);
        var order = new List<Vector2>(cells);
        order.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
        bool Free(Vector2 c, int nx, int nz)
        {
            for (int i = 0; i < nx; i++) for (int j = 0; j < nz; j++) if (!cells.Contains(c + new Vector2(8 * i, 8 * j))) return false;
            return true;
        }
        void Take(Vector2 c, int nx, int nz) { for (int i = 0; i < nx; i++) for (int j = 0; j < nz; j++) cells.Remove(c + new Vector2(8 * i, 8 * j)); }
        bool Aligned(float v, float o) => Mathf.Abs(Mathf.Repeat(v - 4f - o, 32f)) < 0.01f;
        if (big != null)
            foreach (var c in order)
                if (Aligned(c.x, ox) && Aligned(c.y, oz) && Free(c, 4, 4)) { Take(c, 4, 4); Slab(g, big, c.x + 12, c.y + 12, top); }
        foreach (var c in order)
        {
            if (!cells.Contains(c)) continue;
            if (big != null && Free(c, 4, 4)) { Take(c, 4, 4); Slab(g, big, c.x + 12, c.y + 12, top); continue; }
            if (tile == null && Free(c, 4, 2)) { Take(c, 4, 2); Slab(g, "Floor-32x16m", c.x + 12, c.y + 4, top, 0f); continue; }
            if (tile == null && Free(c, 2, 4)) { Take(c, 2, 4); Slab(g, "Floor-32x16m", c.x + 4, c.y + 12, top, 90f); continue; }
            // Strips (a cell wide): the same plain slab cut down, so narrow runs read as one piece too
            if (tile == null)
            {
                int nx = 1, nz = 1;
                while (nx < 4 && cells.Contains(c + new Vector2(8 * nx, 0))) nx++;
                while (nz < 4 && cells.Contains(c + new Vector2(0, 8 * nz))) nz++;
                if (nx >= nz && nx > 1) { Take(c, nx, 1); Strip(g, c.x + 4 * (nx - 1), c.y, nx * 8f, 8f, top, 0f); continue; }
                if (nz > 1) { Take(c, 1, nz); Strip(g, c.x, c.y + 4 * (nz - 1), nz * 8f, 8f, top, 90f); continue; }
            }
            Take(c, 1, 1);
            if (tile == null) foreach (float dz in new[] { -2f, 2f }) Tile(g, "Platform-8x4m", c.x, c.y + dz, top);
            else Tile(g, tile, c.x, c.y, top);
        }
    }

    /// <summary>A plain Floor-32x16m scaled to <paramref name="length"/> × <paramref name="width"/> (along its local x × z).</summary>
    public static void Strip(Transform g, float x, float z, float length, float width, float top, float yaw)
    {
        K(g, "Floor-32x16m", new Vector3(x, top - 1f, z), yaw, new Vector3(length / 32f, 1f, width / 16f));
        if (groundTops.Contains(top))
            for (float a = -length / 2f + 4f; a < length / 2f; a += 8f)
                RecordGround(CellOf(x + (yaw == 0f ? a : 0f), z + (yaw == 0f ? 0f : a)), top);
    }

    /// <summary>A roof over x0..x1 × z0..z1 (top <paramref name="top"/>): big plain slabs where they fit, a Floor-Hole at
    /// each hatch, light liners underneath; <paramref name="skip"/> leaves cells open (courtyards, shafts).</summary>
    public static void Roof(Transform g, float x0, float x1, float z0, float z1, float top, Vector2[] hatches, System.Func<Vector2, bool> skip = null)
    {
        var cells = new HashSet<Vector2>();
        for (float x = x0 + 4; x < x1; x += 8)
            for (float z = z0 + 4; z < z1; z += 8)
            {
                var c = new Vector2(x, z);
                if (skip != null && skip(c)) continue;
                if (System.Array.IndexOf(hatches, c) >= 0) { layingRoof = true; Tile(g, "Floor-Hole-8m", x, z, top); layingRoof = false; }
                else cells.Add(c);
            }
        layingRoof = true;
        try { Fill(g, cells, null, top, true, x0, z0); }
        finally { layingRoof = false; }
    }

    public static void BridgeAlongZ(Transform g, float x, float z0, float z1, float y) =>
        PropsUnder(g, K(g, "Bridge-8x4m", new Vector3(x, y - 1f, (z0 + z1) / 2f), 90f, new Vector3((z1 - z0) / 8f, 1f, 1f)), 1f, Vector2.zero);

    public static void BridgeAlongX(Transform g, float z, float x0, float x1, float y) =>
        PropsUnder(g, K(g, "Bridge-8x4m", new Vector3((x0 + x1) / 2f, y - 1f, z), 0f, new Vector3((x1 - x0) / 8f, 1f, 1f)), 1f, Vector2.zero);

    public static void CheckSpawnSpacing((string name, float x, float z, float y)[] SpawnSpots)
    {
        int close = 0;
        for (int i = 0; i < SpawnSpots.Length; i++)
            for (int j = i + 1; j < SpawnSpots.Length; j++)
            {
                var a = SpawnSpots[i]; var b = SpawnSpots[j];
                float d = Vector3.Distance(new Vector3(a.x, a.y, a.z), new Vector3(b.x, b.y, b.z));
                if (d < 15f) { report.Add($"! spawns {a.name} and {b.name} only {d:0.0} m apart"); close++; }
            }
        report.Add($"spawn spacing: {close} pairs under 15 m");
    }

    /// <summary>
    /// A building on the footprint x0..x1 × z0..z1 standing on <paramref name="baseY"/>: one row of panels (4 m, roof at
    /// base + 5) or two (9 m facades, roof at base + 10; solid 'S' panels are Wall-Tall). Each
    /// side's panels are one letter per 8 m (south/north west → east, west/east south → north): D door, W window, N niche,
    /// Q squeeze, G slide gap, C climb, P pipes, M mixed, S solid tall, X none (something else closes it). Sides listed
    /// in <paramref name="drop"/> start 1 m lower (stretched to fit): they stand on the edge of the slab below. Doors,
    /// squeezes and slide gaps are checked with a running, crouched or prone capsule; niches, squeezes and gaps are listed
    /// as hides.
    /// </summary>
    public static void Building(Transform g, string name, float x0, float x1, float z0, float z1, float baseY, int rows,
                                 string south, string north, string west, string east, Vector2[] hatches = null,
                                 string drop = "", string posts = "SW SE NW NE", bool roof = true)
    {
        var b = Group(name, g);
        float h = rows == 1 ? 4f : 9f;
        void Side(string kinds, char side, Vector3 outward)
        {
            if (string.IsNullOrEmpty(kinds)) return;
            bool alongX = side == 'S' || side == 'N';
            float a0 = alongX ? x0 : z0;
            float at = side == 'S' ? z0 : side == 'N' ? z1 : side == 'W' ? x0 : x1;
            bool dropped = drop.IndexOf(side) >= 0;
            for (int i = 0; i < kinds.Length; i++)
            {
                float a = a0 + 4 + 8 * i;
                float x = alongX ? a : at, z = alongX ? at : a;
                Panel(b, name, x, z, outward, kinds[i], rows, baseY, dropped);
            }
        }
        Side(south, 'S', S); Side(north, 'N', N); Side(west, 'W', W); Side(east, 'E', E);
        void Corner(string code, float x, float z, Vector3 a, Vector3 c, char sa, char sc)
        {
            if (posts.IndexOf(code, System.StringComparison.Ordinal) < 0) return;
            bool dropped = drop.IndexOf(sa) >= 0 || drop.IndexOf(sc) >= 0;
            Post(b, x, dropped ? baseY - 1f : baseY, z, a, c, dropped ? h + 1f : h);
        }
        Corner("SW", x0, z0, W, S, 'W', 'S'); Corner("SE", x1, z0, E, S, 'E', 'S');
        Corner("NW", x0, z1, W, N, 'W', 'N'); Corner("NE", x1, z1, E, N, 'E', 'N');
        if (roof) Roof(b, x0, x1, z0, z1, baseY + 5f * rows, hatches ?? System.Array.Empty<Vector2>());
        if (hatches != null) foreach (var c in hatches) Cubby($"{name} roof hatch", new Vector3(c.x, baseY + 5f * rows, c.y), "a drop in from the roof, a ledge grab out");
    }

    public static void Panel(Transform b, string name, float x, float z, Vector3 outward, char k, int rows, float baseY, bool dropped)
    {
        if (k == 'X' || k == '-') return;
        float y = dropped ? baseY - 1f : baseY;
        if (rows == 1)
        {
            string id = k switch
            {
                'D' => "Archway-8m", 'W' => "WallWindow-8m", 'N' => "Wall-Niche-8m", 'Q' => "Wall-NichePass-8m",
                'G' => "Wall-SlideGap-8m", 'C' => ClimbWall, 'P' => "Wall-Pipes-8m", 'R' => "WallRunPanel-8m", 'V' => "Wall-Vented-8m", 'J' => "Wall-Grid-8m", 'S' => "Wall-Mid-8m", _ => LowerWall(),
            };
            Wall(b, id, x, y, z, outward, dropped ? 5f / 4f : 1f);
        }
        else if (k == 'S') Wall(b, "Wall-Tall-8m", x, y, z, outward, dropped ? 10f / 9f : 1f);
        else
        {
            string lower = k switch
            {
                'D' => "Archway-8m", 'W' => "WallWindow-8m", 'N' => "Wall-Niche-8m", 'Q' => "Wall-NichePass-8m",
                'G' => "Wall-SlideGap-8m", 'C' => ClimbWall, 'P' => "Wall-Pipes-8m", 'R' => "WallRunPanel-8m", 'V' => "Wall-Vented-8m", 'J' => "Wall-Grid-8m", _ => LowerWall(),
            };
            float lowScale = dropped ? 5f / 4f : 1f;
            Wall(b, lower, x, y, z, outward, lowScale);
            Wall(b, k == 'C' ? ClimbWall : UpperWall(), x, y + 4f * lowScale, z, outward, (baseY + 9f - (y + 4f * lowScale)) / 4f);
        }
        // Checks and hides (at the panel's foot, from outside in)
        Vector3 at = new(x, baseY, z), inward = -outward;
        string where = $"{name} {(outward == S ? "south" : outward == N ? "north" : outward == W ? "west" : "east")}";
        switch (k)
        {
            case 'D': Tunnel($"{where} door ({x:0}, {z:0})", at + outward * 2.5f, at + inward * 2.5f, "run"); break;
            case 'Q':
                Tunnel($"{where} squeeze", at + outward * 4f, at + inward * 2f, "crouch");
                Cubby($"{where} squeeze", at + outward * 1.5f, "crouch (2.6 m), a way through as well as a hide");
                break;
            case 'G':
                Tunnel($"{where} slide gap", at + outward * 2f, at + inward * 2f, "crawl");
                Cubby($"{where} slide gap", at, "prone/slide (1.7 m), a way through");
                break;
            case 'N': Cubby($"{where} niche", at + outward * 1.5f, "standing (3.5 m)"); break;
        }
    }

    /// <summary>A mezzanine (top <paramref name="top"/>) over x0..x1 × z0..z1 along a wall, 4 m or 8 m deep, on a line of
    /// beams and pillars along its open edge (on the <paramref name="open"/> side), with railings there.</summary>
    public static void Mezzanine(Transform g, float x0, float x1, float z0, float z1, float top, char open)
    {
        for (float x = x0 + 4; x < x1; x += 8)
            for (float z = z0 + 2; z < z1; z += 4)
            {
                K(g, "Platform-8x4m", new Vector3(x, top - 1f, z), 0f);
                Ceiling(g, x, top - 1f, z, 0f, 1f, 0.5f);
            }
        float edge = open == 'S' ? z0 : z1, inset = open == 'S' ? 1f : -1f;
        for (float x = x0 + 4; x < x1; x += 8) K(g, "Beam-8m", new Vector3(x, top - 2f, edge + inset), 0f);
        for (float x = x0 + 8; x < x1; x += 8) K(g, "Pillar-Round-2m", new Vector3(x, top - 5f, edge + inset), 0f, new Vector3(1f, 0.75f, 1f));
    }

    /// <summary>
    /// A 0.5 m curb round a hole (x0..x1 × z0..z1) on the floor at <paramref name="floor"/>: squashed half walls standing
    /// just outside it, posts on the corners; open (side, at) leaves out the 8 m piece centred there (x for S/N, z for W/E).
    /// Walking never drops you in; a hop does.
    /// </summary>
    public static void Curb(Transform g, string name, Vector4 hole, float floor, params (char side, float at)[] open)
    {
        var c = Group(name, g);
        void Side(char side, Vector3 outward)
        {
            bool alongX = side == 'S' || side == 'N';
            float a0 = alongX ? hole.x : hole.z, a1 = alongX ? hole.y : hole.w;
            float fixedAt = side == 'S' ? hole.z : side == 'N' ? hole.w : side == 'W' ? hole.x : hole.y;
            for (float a = a0; a < a1 - 0.01f; a += 8f)
            {
                float len = Mathf.Min(8f, a1 - a), mid = a + len / 2f;
                if (System.Array.Exists(open, o => o.side == side && Mathf.Abs(o.at - mid) < 0.5f)) continue;
                float x = alongX ? mid : fixedAt, z = alongX ? fixedAt : mid;
                K(c, "Wall-Half-8m", new Vector3(x, floor, z), Mathf.Atan2(-outward.x, -outward.z) * Mathf.Rad2Deg, new Vector3(len / 8f, 0.25f, 1f));
            }
        }
        Side('S', S); Side('N', N); Side('W', W); Side('E', E);
        Post(c, hole.x, floor, hole.z, W, S, 0.5f); Post(c, hole.y, floor, hole.z, E, S, 0.5f);
        Post(c, hole.x, floor, hole.w, W, N, 0.5f); Post(c, hole.y, floor, hole.w, E, N, 0.5f);
    }

    // ─── Multi-storey faces, floors and stairs (from SkyreachBuilder, Nov 13) ───────────────────────────

    // ─── Mesa faces: three storeys in 8 m columns ─────────────────────────────

    /// <summary>
    /// One 8 m column of a three-storey face standing on <paramref name="baseY"/>: storeys at 0..4, 5..9 and 10..14 by
    /// letter (D door, W window, N niche, Q squeeze, G slide gap (ground storey only), C climb, P pipes, M mixed, S solid,
    /// X none), with 1 m bands between them (4..5, 9..10) unless a slab outside meets the wall there (an annex roof):
    /// '4' or '9' after the letters leaves that band out. <paramref name="length"/> below 8 cuts the column short (an
    /// inner corner). Doors, squeezes and gaps are checked with a capsule; niches and the like are listed as hides.
    /// </summary>
    public static void Column(Transform g, string name, float x, float z, Vector3 outward, string spec, float baseY = 0f,
                               float length = 8f, bool bandBelow = false)
    {
        float sx = length / 8f;
        // Standing on a slab's edge (a stack on the island's rim): a band covers the slab below too
        if (bandBelow) K(g, "Wall-Mid-8m", new Vector3(x, baseY - 1f, z), Mathf.Atan2(-outward.x, -outward.z) * Mathf.Rad2Deg, new Vector3(sx, 0.25f, 1f));
        for (int s = 0; s < 3; s++)
        {
            char k = spec[s];
            float y = baseY + 5f * s;
            if (k != 'X')
            {
                string id = k switch
                {
                    'D' => "Archway-8m", 'W' => "WallWindow-8m", 'N' => "Wall-Niche-8m", 'Q' => "Wall-NichePass-8m",
                    'G' => "Wall-SlideGap-8m", 'C' => ClimbWall, 'P' => "Wall-Pipes-8m", 'R' => "WallRunPanel-8m", 'V' => "Wall-Vented-8m", 'J' => "Wall-Grid-8m", 'S' => "Wall-Mid-8m",
                    _ => s == 0 ? LowerWall() : UpperWall(),
                };
                K(g, id, new Vector3(x, y, z), Mathf.Atan2(-outward.x, -outward.z) * Mathf.Rad2Deg, new Vector3(sx, 1f, 1f));
                Vector3 at = new(x, y, z), inward = -outward;
                string where = $"{name} ({x:0}, {y:0}, {z:0})";
                if (k == 'D') Tunnel($"{where} door", at + outward * 2.5f, at + inward * 2.5f, "run");
                if (k == 'Q') { Tunnel($"{where} squeeze", at + outward * 4f, at + inward * 2f, "crouch"); Cubby($"{name} squeeze", at + outward * 1.5f, "crouch (2.6 m), a way through"); }
                if (k == 'G') { Tunnel($"{where} slide gap", at + outward * 2f, at + inward * 2f, "crawl"); Cubby($"{name} slide gap", at, "prone/slide, a way through"); }
                if (k == 'N') Cubby($"{name} niche", at + outward * 1.5f, "standing (3.5 m)");
            }
            if (s == 2) break;
            // The band above this storey
            bool slab = spec.IndexOf(s == 0 ? '4' : '9') >= 0;
            bool both = k == 'X' && spec[s + 1] == 'X';
            if (!slab && !both)
                K(g, "Wall-Mid-8m", new Vector3(x, y + 4f, z), Mathf.Atan2(-outward.x, -outward.z) * Mathf.Rad2Deg, new Vector3(sx, 0.25f, 1f));
        }
    }

    /// <summary>A whole side of columns, one spec per 8 m from <paramref name="from"/> along x (S/N) or z (W/E).</summary>
    public static void Face(Transform g, string name, char side, float at, float from, float baseY, params string[] specs) =>
        Face(g, name, side, at, from, baseY, false, specs);

    public static void Face(Transform g, string name, char side, float at, float from, float baseY, bool bandBelow, params string[] specs)
    {
        Vector3 outward = side == 'S' ? S : side == 'N' ? N : side == 'W' ? W : E;
        for (int i = 0; i < specs.Length; i++)
        {
            if (specs[i] == null) continue;
            float a = from + 4f + 8f * i;
            if (side == 'S' || side == 'N') Column(g, name, a, at, outward, specs[i], baseY, 8f, bandBelow);
            else Column(g, name, at, a, outward, specs[i], baseY, 8f, bandBelow);
        }
    }

    // A corner post as tall as three storeys (0..14)
    public static void TallPost(Transform g, float x, float z, Vector3 a, Vector3 b, float baseY = 0f, bool bandBelow = false) =>
        Post(g, x, bandBelow ? baseY - 1f : baseY, z, a, b, bandBelow ? 15f : 14f);

    // A wall panel cut short (where it meets another wall's body at an inner corner)
    public static void Cut(Transform g, string id, float x, float y, float z, Vector3 outward, float length) =>
        K(g, id, new Vector3(x, y, z), Mathf.Atan2(-outward.x, -outward.z) * Mathf.Rad2Deg, new Vector3(length / 8f, 1f, 1f));

    // Floors at 5 and 10 and the roof at 15 over a rectangle (minus the cells skip picks; hatches in the roof)
    public static void Storeys(Transform g, Vector4 r, float baseY, System.Func<Vector2, int, bool> skip, Vector2[] roofHatches)
    {
        for (int s = 1; s <= 3; s++)
        {
            int level = s;
            Roof(g, r.x, r.y, r.z, r.w, baseY + 5f * s, s == 3 ? roofHatches : System.Array.Empty<Vector2>(), c => skip(c, level));
        }
    }

    /// <summary>
    /// Stairs (Stairs-8x10m) up one storey inside a building: rising toward +z (yaw 0) or −z (180) etc, its top on the
    /// cell edge <paramref name="topEdge"/>; the two cells before the top are left out of the floor above (the
    /// stairwell). Returns those cells.
    /// </summary>
    public static Vector2[] Stairs(Transform g, float x, float topEdge, float floor, bool north)
    {
        float centre = north ? topEdge - 5f : topEdge + 5f;
        K(g, "Stairs-8x10m", new Vector3(x, floor - 1f, centre), north ? 0f : 180f);
        return north ? new[] { new Vector2(x, topEdge - 4f), new Vector2(x, topEdge - 12f) }
                     : new[] { new Vector2(x, topEdge + 4f), new Vector2(x, topEdge + 12f) };
    }
}
