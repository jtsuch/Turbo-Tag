using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ProBuilder;
using Object = UnityEngine.Object;

/// <summary>
/// Shared greybox toolkit for the code-built levels (PinnacleBuilder; the kit levels use KitLevel): plain ProBuilder boxes in
/// three tones plus orange-topped climbing ledges, ramps along Z or X, slabs with holes, huts with doors, plain
/// moving platforms (no trim), kit props, bounce pads and lamps; and the self-review every build runs (capsule
/// clearance checks through tunnels and doors, spawn points, gap lengths) plus overview pictures.
/// Use with <c>using static GreyboxKit;</c>: Begin → build → Finish.
/// </summary>
public static class GreyboxKit
{
    public const int   Structure = 6;
    public const float Radius = 0.7f, Stand = 2.9f, Crouch = 2.03f;
    private const string TemplatePath = "Assets/Scenes/Levels/_LevelTemplate.unity";

    public static Material light, mid, dark, ledge;
    public static Transform root;
    public static readonly List<string> report = new();
    public static readonly List<(string name, Vector3 from, Vector3 to, string kind)> tunnels = new();
    public static readonly List<(string name, Vector3 a, Vector3 b)> gaps = new();

    // ─── Begin / Finish ─────────────────────────────────────────────────────

    /// <summary>Opens (or creates from the template) the scene and gives a fresh root under Geometry.</summary>
    public static UnityEngine.SceneManagement.Scene Begin(string scenePath, string rootName)
    {
        report.Clear();
        tunnels.Clear();
        gaps.Clear();
        if (!File.Exists(scenePath)) AssetDatabase.CopyAsset(TemplatePath, scenePath);
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        var geometry = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Geometry");
        if (geometry == null) geometry = new GameObject("Geometry");
        var old = geometry.transform.Find(rootName);
        if (old != null) Object.DestroyImmediate(old.gameObject);
        LoadMaterials();
        root = new GameObject(rootName).transform;
        root.SetParent(geometry.transform, false);
        return scene;
    }

    /// <summary>Layers, review, save, Build Settings, pictures and the report file.</summary>
    public static void Finish(UnityEngine.SceneManagement.Scene scene, string reportPath, string label, Action render)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.gameObject.layer == 0 && t.GetComponent<Light>() == null && !t.name.StartsWith("Invisible") && !t.name.StartsWith("Kill")
                && t.GetComponentInParent<SpawnBarrier>(true) == null)   // Pre-round walls stay Default (not in the door checks)
                t.gameObject.layer = Structure;
        Physics.SyncTransforms();
        Review();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        LevelTools.SyncBuildSettings();
        render?.Invoke();
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
        File.WriteAllLines(reportPath, report);
        Debug.Log($"[{label}] Built. {report.Count(r => r.StartsWith("!"))} problem(s). Report: {reportPath}");
    }

    /// <summary>A DeathZone trigger below everything and an invisible Default-layer ceiling above.</summary>
    public static void Bounds(Vector3 centre, float size, float killY, float ceilingY)
    {
        var g = Group("Bounds");
        var kill = new GameObject("Kill Plane");
        kill.transform.SetParent(g, false);
        kill.transform.position = new Vector3(centre.x, killY, centre.z);
        var box = kill.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(size, 4, size);
        kill.AddComponent<DeathZone>();
        var ceiling = new GameObject("Invisible Ceiling");
        ceiling.transform.SetParent(g, false);
        ceiling.transform.position = new Vector3(centre.x, ceilingY, centre.z);
        ceiling.AddComponent<BoxCollider>().size = new Vector3(size, 2, size);
    }

    /// <summary>Spawn points at (x, floor y, z), each facing <paramref name="lookAt"/>.</summary>
    public static void Spawns((string name, float x, float z, float y)[] spots, Vector3 lookAt)
    {
        var g = Group("Spawn Points");
        int i = 0;
        foreach (var (name, x, z, y) in spots)
        {
            var go = new GameObject($"Spawn {++i:00} {name}");
            go.transform.SetParent(g, false);
            var feet = new Vector3(x, y + 0.1f, z);
            Vector3 look = new Vector3(lookAt.x, feet.y, lookAt.z) - feet;
            go.transform.SetPositionAndRotation(feet, Quaternion.LookRotation(look.sqrMagnitude > 1f ? look : Vector3.forward));
            go.AddComponent<SpawnPoint>();
        }
    }

    public static void PlaceSpawner(Vector3 at, float killHeight)
    {
        var spawner = Object.FindFirstObjectByType<Spawner>(FindObjectsInactive.Include);
        if (spawner == null) { report.Add("! no Spawner in the scene"); return; }
        spawner.transform.position = at;
        var so = new SerializedObject(spawner);
        so.FindProperty("killHeight").floatValue = killHeight;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>A slab x0..x1 × z0..z1 with rectangular holes given as (x0, x1, z0, z1), built from strips.</summary>
    public static void Slab(Transform parent, string name, float x0, float x1, float y0, float y1, float z0, float z1, Kind kind, params Vector4[] holes)
    {
        var xs = new SortedSet<float> { x0, x1 };
        foreach (var h in holes) { xs.Add(Mathf.Clamp(h.x, x0, x1)); xs.Add(Mathf.Clamp(h.y, x0, x1)); }
        var cuts = xs.ToList();
        int n = 0;
        for (int i = 0; i < cuts.Count - 1; i++)
        {
            float a = cuts[i], b = cuts[i + 1], mx = (a + b) * 0.5f;
            var blocked = holes.Where(h => h.x < mx && mx < h.y).Select(h => (h.z, h.w)).OrderBy(h => h.z).ToList();
            float z = z0;
            foreach (var (hz0, hz1) in blocked)
            {
                if (hz0 > z) Box(parent, $"{name} {++n}", a, b, y0, y1, z, hz0, kind);
                z = Mathf.Max(z, hz1);
            }
            if (z < z1) Box(parent, $"{name} {++n}", a, b, y0, y1, z, z1, kind);
        }
    }

    /// <summary>
    /// A solid block with box-shaped voids cut out of it (tunnels, rooms, shafts), each void given as
    /// (x0, x1, y0, y1, z0, z1). Built from the fewest simple boxes a grid split allows.
    /// </summary>
    public static void Carve(Transform parent, string name, float x0, float x1, float y0, float y1, float z0, float z1, Kind kind,
                             params (float x0, float x1, float y0, float y1, float z0, float z1)[] voids)
    {
        static List<float> Cuts(float a, float b, IEnumerable<float> extra)
        {
            var set = new SortedSet<float> { a, b };
            foreach (var e in extra) if (e > a && e < b) set.Add(e);
            return set.ToList();
        }
        var xs = Cuts(x0, x1, voids.SelectMany(v => new[] { v.x0, v.x1 }));
        var ys = Cuts(y0, y1, voids.SelectMany(v => new[] { v.y0, v.y1 }));
        var zs = Cuts(z0, z1, voids.SelectMany(v => new[] { v.z0, v.z1 }));
        bool Solid(float x, float y, float z) =>
            !voids.Any(v => x > v.x0 && x < v.x1 && y > v.y0 && y < v.y1 && z > v.z0 && z < v.z1);

        // Solid vertical runs per (x, z) column
        List<(float, float)> Column(int i, int k)
        {
            var runs = new List<(float, float)>();
            float mx = (xs[i] + xs[i + 1]) * 0.5f, mz = (zs[k] + zs[k + 1]) * 0.5f;
            for (int j = 0; j < ys.Count - 1; j++)
            {
                if (!Solid(mx, (ys[j] + ys[j + 1]) * 0.5f, mz)) continue;
                if (runs.Count > 0 && Mathf.Approximately(runs[^1].Item2, ys[j])) runs[^1] = (runs[^1].Item1, ys[j + 1]);
                else runs.Add((ys[j], ys[j + 1]));
            }
            return runs;
        }

        int n = 0;
        for (int i = 0; i < xs.Count - 1; i++)
        {
            int k = 0;
            while (k < zs.Count - 1)
            {
                var runs = Column(i, k);
                int end = k + 1;
                while (end < zs.Count - 1 && Column(i, end).SequenceEqual(runs)) end++; // Merge identical columns along z
                foreach (var (ya, yb) in runs)
                    Box(parent, $"{name} {++n}", xs[i], xs[i + 1], ya, yb, zs[k], zs[end], kind);
                k = end;
            }
        }
    }

    /// <summary>A ramp across z0..z1 rising along +X from y0 at x0 to y1 at x1 (y1 may be lower).</summary>
    public static void RampX(Transform parent, string name, float x0, float x1, float z0, float z1, float y0, float y1)
    {
        float run = x1 - x0, rise = y1 - y0;
        float length = Mathf.Sqrt(run * run + rise * rise);
        var pb = ShapeGenerator.GenerateCube(PivotLocation.Center, new Vector3(length, 0.6f, z1 - z0));
        pb.name = name;
        pb.transform.SetParent(parent, false);
        float angle = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
        pb.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        Vector3 topMid = new((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
        pb.transform.position = topMid - pb.transform.up * 0.3f;
        foreach (var face in pb.faces)
        {
            var uv = face.uv; uv.useWorldSpace = true; face.uv = uv;
            pb.SetMaterial(new[] { face }, light);
        }
        pb.ToMesh();
        pb.Refresh();
        pb.gameObject.AddComponent<BoxCollider>().size = new Vector3(length, 0.6f, z1 - z0);
        GameObjectUtility.SetStaticEditorFlags(pb.gameObject, (StaticEditorFlags)~0);
        angle = Mathf.Abs(angle);
        report.Add(angle > 45f ? $"! ramp {name} is {angle:0}° (too steep to walk)" : $"ramp {name}: {angle:0}°");
    }

    // ─── Composite pieces ─────────────────────────────────────────────────────

    /// <summary>A hollow hut: 1 m walls, a 1 m roof, a 3 m × 3.8 m door on each side listed.</summary>
    public static void Hut(Transform parent, string name, float x0, float x1, float z0, float z1, float floor, float height, params char[] doors)
    {
        var g = Group(name, parent);
        const float t = 1f, doorW = 3f, doorH = 3.8f;
        float top = floor + height;
        void Side(char side, float a0, float a1, float fixedA, float fixedB, bool alongX)
        {
            bool door = doors.Contains(side);
            float c = (a0 + a1) * 0.5f;
            if (!door)
            {
                if (alongX) Box(g, $"{name} {side}", a0, a1, floor, top, fixedA, fixedB, Kind.Wall);
                else Box(g, $"{name} {side}", fixedA, fixedB, floor, top, a0, a1, Kind.Wall);
                return;
            }
            if (alongX)
            {
                Box(g, $"{name} {side}1", a0, c - doorW / 2, floor, top, fixedA, fixedB, Kind.Wall);
                Box(g, $"{name} {side}2", c + doorW / 2, a1, floor, top, fixedA, fixedB, Kind.Wall);
                Box(g, $"{name} {side} Lintel", c - doorW / 2, c + doorW / 2, floor + doorH, top, fixedA, fixedB, Kind.Wall);
                Tunnel($"{name} door {side}", new Vector3(c, floor, fixedA - 1), new Vector3(c, floor, fixedB + 1), "run");
            }
            else
            {
                Box(g, $"{name} {side}1", fixedA, fixedB, floor, top, a0, c - doorW / 2, Kind.Wall);
                Box(g, $"{name} {side}2", fixedA, fixedB, floor, top, c + doorW / 2, a1, Kind.Wall);
                Box(g, $"{name} {side} Lintel", fixedA, fixedB, floor + doorH, top, c - doorW / 2, c + doorW / 2, Kind.Wall);
                Tunnel($"{name} door {side}", new Vector3(fixedA - 1, floor, c), new Vector3(fixedB + 1, floor, c), "run");
            }
        }
        Side('S', x0, x1, z0, z0 + t, true);
        Side('N', x0, x1, z1 - t, z1, true);
        Side('W', z0 + t, z1 - t, x0, x0 + t, false);
        Side('E', z0 + t, z1 - t, x1 - t, x1, false);
        Box(g, $"{name} Roof", x0, x1, top, top + 0.5f, z0, z1, Kind.Floor);
    }

    // ─── Building blocks ──────────────────────────────────────────────────────

    public enum Kind { Floor, Wall, Ledge, Dark }

    public static Transform Group(string name, Transform parent = null)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent != null ? parent : root, false);
        return t;
    }

    /// <summary>A box from its world-space extents. Floors: light top; walls: mid all over; ledges: orange top; dark: supports.</summary>
    public static GameObject Box(Transform parent, string name, float x0, float x1, float y0, float y1, float z0, float z1, Kind kind)
    {
        var size = new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0));
        var pb = ShapeGenerator.GenerateCube(PivotLocation.Center, size);
        pb.name = name;
        pb.transform.SetParent(parent, false);
        pb.transform.position = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
        Material sides = kind == Kind.Dark ? dark : mid;
        Material top   = kind switch { Kind.Ledge => ledge, Kind.Dark => dark, Kind.Wall => mid, _ => light };
        foreach (var face in pb.faces)
        {
            var uv = face.uv;
            uv.useWorldSpace = true;
            face.uv = uv;
            pb.SetMaterial(new[] { face }, UnityEngine.ProBuilder.Math.Normal(pb, face).y > 0.9f ? top : sides);
        }
        pb.ToMesh();
        pb.Refresh();
        var col = pb.gameObject.AddComponent<BoxCollider>(); // Boxes: exact, cheap, no seams
        col.size = size;
        GameObjectUtility.SetStaticEditorFlags(pb.gameObject, (StaticEditorFlags)~0);
        return pb.gameObject;
    }

    /// <summary>A ramp along +Z (x0..x1 wide) from height y0 at z0 to y1 at z1 (walkable up to ~50°).</summary>
    public static void Ramp(Transform parent, string name, float x0, float x1, float z0, float z1, float y0, float y1)
    {
        float run = z1 - z0, rise = y1 - y0;
        float length = Mathf.Sqrt(run * run + rise * rise);
        var pb = ShapeGenerator.GenerateCube(PivotLocation.Center, new Vector3(x1 - x0, 0.6f, length));
        pb.name = name;
        pb.transform.SetParent(parent, false);
        float angle = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
        pb.transform.rotation = Quaternion.Euler(-angle, 0f, 0f);
        // Its top surface runs from (z0, y0) to (z1, y1)
        Vector3 topMid = new((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
        pb.transform.position = topMid - pb.transform.up * 0.3f;
        foreach (var face in pb.faces)
        {
            var uv = face.uv; uv.useWorldSpace = true; face.uv = uv;
            pb.SetMaterial(new[] { face }, light);
        }
        pb.ToMesh();
        pb.Refresh();
        pb.gameObject.AddComponent<BoxCollider>().size = new Vector3(x1 - x0, 0.6f, length);
        GameObjectUtility.SetStaticEditorFlags(pb.gameObject, (StaticEditorFlags)~0);
        report.Add(Mathf.Abs(angle) > 45f ? $"! ramp {name} is {Mathf.Abs(angle):0}° (too steep to walk)" : $"ramp {name}: {Mathf.Abs(angle):0}°");
    }

    /// <summary>A plain moving platform (no trim: Justin dislikes the strips) between two base points.</summary>
    public static void Mover(Transform parent, string name, Vector3 aBase, Vector3 bBase, Vector3 size, float speed)
    {
        var g = Group(name, parent);
        var a = new GameObject("Point A").transform; a.SetParent(g, false); a.position = aBase + Vector3.up * (size.y * 0.5f);
        var b = new GameObject("Point B").transform; b.SetParent(g, false); b.position = bBase + Vector3.up * (size.y * 0.5f);
        var platform = Box(g, "Platform", aBase.x - size.x / 2, aBase.x + size.x / 2, aBase.y, aBase.y + size.y,
                           aBase.z - size.z / 2, aBase.z + size.z / 2, Kind.Floor);
        GameObjectUtility.SetStaticEditorFlags(platform, 0);
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

    public static GameObject Prop(Transform parent, string prefab, Vector3 basePoint, float yaw, float scale = 1f)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Environment/Prefabs/{prefab}.prefab");
        if (asset == null) { report.Add($"! missing prefab {prefab}"); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * asset.transform.rotation;
        go.transform.localScale = asset.transform.localScale * scale;
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length > 0)
        {
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            go.transform.position += basePoint - new Vector3(b.center.x, b.min.y, b.center.z);
        }
        else go.transform.position = basePoint;
        return go;
    }

    public static void Bounce(Transform parent, Vector3 basePoint, float force)
    {
        var pad = Prop(parent, "BouncePadv2", basePoint, 0f);
        if (pad == null) return;
        var bounce = pad.GetComponentInChildren<BouncePad>();
        if (bounce == null) return;
        var so = new SerializedObject(bounce);
        so.FindProperty("bounceForce").floatValue = force;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    public static void Lamp(Transform parent, Vector3 pos, float range)
    {
        var go = new GameObject("Lamp");
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.85f, 0.65f);
        l.range = range;
        l.intensity = 2.5f;
        l.shadows = LightShadows.None;
    }

    public static void Tunnel(string name, Vector3 from, Vector3 to, string kind) => tunnels.Add((name, from, to, kind));

    // ─── Review ───────────────────────────────────────────────────────────────

    // Checks clearances along every tunnel and door, that every spawn point is clear, and measures every gap
    public static void Review()
    {
        report.Add("=== Review ===");
        int mask = 1 << Structure;
        foreach (var (name, from, to, kind) in tunnels)
        {
            int samples = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(from, to) / 1.5f));
            int standBlocked = 0, crouchBlocked = 0, proneBlocked = 0;
            for (int i = 1; i < samples; i++)
            {
                Vector3 p = Vector3.Lerp(from, to, i / (float)samples) + Vector3.up * 0.05f;
                if (Blocked(p, Stand, mask)) standBlocked++;
                if (Blocked(p, Crouch, mask)) crouchBlocked++;
                if (Blocked(p, Radius * 2f + 0.02f, mask)) proneBlocked++;
            }
            string verdict = kind switch
            {
                "run"    => standBlocked == 0 ? "ok" : $"! a standing player is blocked at {standBlocked}/{samples - 1} points",
                "crouch" => crouchBlocked == 0 && standBlocked > 0 ? "ok" : crouchBlocked > 0 ? "! too low even crouched" : "! not low enough (can stand)",
                "crawl"  => proneBlocked == 0 && crouchBlocked > 0 ? "ok" : proneBlocked > 0 ? "! too low even prone" : "! not low enough (can crouch)",
                _ => "?",
            };
            report.Add($"{(verdict.StartsWith("!") ? "" : "  ")}{verdict.Replace("ok", $"ok   {kind,-6} {name}")}{(verdict.StartsWith("!") ? $"  [{kind} {name}]" : "")}");
        }

        foreach (var sp in Object.FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None))
        {
            Vector3 p = sp.transform.position;
            bool blocked = Blocked(p + Vector3.up * 0.1f, Stand, mask);
            bool floor = Physics.Raycast(p + Vector3.up, Vector3.down, 2f, mask);
            report.Add($"{(blocked || !floor ? "! " : "  ")}spawn {sp.name}: {(blocked ? "BLOCKED " : "")}{(floor ? "" : "NO FLOOR")}{(!blocked && floor ? "ok" : "")}");
        }

        foreach (var (name, a, b) in gaps)
        {
            float flat = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
            float rise = b.y - a.y;
            string note = flat > 9f ? "! longer than a sprint jump" : flat > 7.5f ? "risky (over 7.5 m)" : "ok";
            report.Add($"{(note.StartsWith("!") ? "" : "  ")}gap {name}: {flat:0.0} m across, {rise:+0.0;-0.0;0} m up  {note}");
        }
    }

    // A capsule of this height standing at p (feet) overlaps the level?
    public static bool Blocked(Vector3 feet, float height, int mask)
    {
        Vector3 bottom = feet + Vector3.up * Radius;
        Vector3 top = feet + Vector3.up * Mathf.Max(Radius, height - Radius);
        return Physics.CheckCapsule(bottom, top, Radius * 0.95f, mask, QueryTriggerInteraction.Ignore);
    }

    // ─── Pictures ───────────────────────────────────────────────────────────

    public static void Shot(string file, Vector3 pos, Quaternion rot, bool ortho, float size)
    {
        var go = new GameObject("Review Camera");
        var cam = go.AddComponent<Camera>();
        go.transform.SetPositionAndRotation(pos, rot);
        cam.orthographic = ortho;
        if (ortho) cam.orthographicSize = size;
        cam.fieldOfView = 60f;
        cam.farClipPlane = 1000f;
        cam.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.12f, 0.16f);
        var rt = new RenderTexture(1600, ortho ? 1600 : 900, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes($"Library/TurboTag/scratch/{file}.png", tex.EncodeToPNG());
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(go);
    }

    public static void LoadMaterials()
    {
        light = Mat("Assets/Environment/Materials/Large/WallLight.mat");
        mid   = Mat("Assets/Environment/Materials/Large/WallOfflight.mat");
        dark  = Mat("Assets/Environment/Materials/Large/WallDark.mat");
        ledge = Mat("Assets/Environment/Materials/Concrete/AccentOrange.mat");
    }

    public static Material Mat(string path)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) report.Add($"! missing material {path}");
        return m;
    }
}
