using System.IO;
using UnityEditor;
using UnityEngine;
using static GreyboxKit;

/// <summary>
/// Builds the Search &amp; Destroy greybox maps (Assets/Scenes/Levels/SearchDestroy/{Name}.unity) from
/// Assets/Environment/LevelSpecs/{Name}.json, each traced from one of Justin's plan images by Docs/map_extract.py:
/// Standoff (Docs/map_template.webp, ~180 × 169 m), Switchback (Docs/Example1.webp, ~169 × 175 m) and Citadel
/// (Docs/Example2.webp, ~150 × 170 m). In each: the grey is one flat floor at 0, the white spaces
/// between corridors are buildings (10 m blocks), the area round the outside is a 20 m boundary ring, the small
/// outlined boxes and circles are low platforms (2.5 m), thin lines inside the floor are 4 m thin walls, the green
/// areas are bomb sites A and B, and the defenders' and attackers' spawns (6 points each) are ringed by
/// SpawnBarrier panels (up only in the pre-round).
///
/// Proportions only: Justin sets the building heights and styles by hand afterwards, so **re-running a full build
/// replaces Geometry/{Name} and loses those edits**; the "Rebuild Spawn Barriers" items only replace the barrier rings.
/// Menu: Turbo Tag/Levels/Search &amp; Destroy/... (jobs BuildStandoff, BuildSwitchback, BuildCitadel, Build{Name}Barriers).
/// New map: run map_extract.py on the plan into LevelSpecs/{Name}.json, add menu items/jobs here, build.
/// </summary>
public static class SnDMapBuilder
{
    private static string ScenePath(string name)  => $"Assets/Scenes/Levels/SearchDestroy/{name}.unity";
    private static string DataPath(string name)   => $"Assets/Environment/LevelSpecs/{name}.json";
    private static string ReportPath(string name) => $"Library/TurboTag/scratch/{name.ToLowerInvariant()}_report.txt";

    [MenuItem("Turbo Tag/Levels/Search & Destroy/Rebuild Standoff")]   public static void BuildStandoff()   => Build("Standoff");
    [MenuItem("Turbo Tag/Levels/Search & Destroy/Rebuild Switchback")] public static void BuildSwitchback() => Build("Switchback");
    [MenuItem("Turbo Tag/Levels/Search & Destroy/Rebuild Citadel")]    public static void BuildCitadel()    => Build("Citadel");
    [MenuItem("Turbo Tag/Levels/Search & Destroy/Standoff: Rebuild Spawn Barriers")]   public static void StandoffBarriers()   => BuildBarriersOnly("Standoff");
    [MenuItem("Turbo Tag/Levels/Search & Destroy/Switchback: Rebuild Spawn Barriers")] public static void SwitchbackBarriers() => BuildBarriersOnly("Switchback");
    [MenuItem("Turbo Tag/Levels/Search & Destroy/Citadel: Rebuild Spawn Barriers")]    public static void CitadelBarriers()    => BuildBarriersOnly("Citadel");

    private const float BuildingHeight = 10f;
    private const float BoundaryHeight = 20f;
    private const float PlatformHeight = 2.5f;
    private const float ThinWallHeight = 4f;

    // Filled from the layout JSON by JsonUtility, which the compiler can't see
#pragma warning disable 0649
    [System.Serializable]
    private class Site { public string letter; public float[] rect; }

    [System.Serializable]
    private class Spawn { public string side; public float x, z, faceX, faceZ; }

    [System.Serializable]
    private class Layout
    {
        public float[] size, floor, buildings, boundary, thinWalls, platforms;
        public int[] platformRound;
        public Site[] sites;
        public Spawn[] spawns;
    }
#pragma warning restore 0649

    /// <summary>Builds (or rebuilds) one map's scene from its layout file.</summary>
    public static void Build(string name)
    {
        var data = JsonUtility.FromJson<Layout>(File.ReadAllText(DataPath(name)));
        if (!AssetDatabase.IsValidFolder("Assets/Scenes/Levels/SearchDestroy")) AssetDatabase.CreateFolder("Assets/Scenes/Levels", "SearchDestroy");
        var scene = Begin(ScenePath(name), name);

        var f = data.floor;
        Box(root, "Floor", f[0], f[1], -1f, 0f, f[2], f[3], Kind.Floor);

        var buildings = Group("Buildings");
        Blocks(buildings, "Building", data.buildings, BuildingHeight, Kind.Wall);
        var boundary = Group("Boundary");
        Blocks(boundary, "Boundary", data.boundary, BoundaryHeight, Kind.Wall);
        var thin = Group("Thin Walls");
        Blocks(thin, "Thin Wall", data.thinWalls, ThinWallHeight, Kind.Dark);

        var platforms = Group("Platforms");
        for (int i = 0; i * 4 < data.platforms.Length; i++)
        {
            float x0 = data.platforms[i * 4], x1 = data.platforms[i * 4 + 1], z0 = data.platforms[i * 4 + 2], z1 = data.platforms[i * 4 + 3];
            bool round = data.platformRound != null && i < data.platformRound.Length && data.platformRound[i] == 1;
            if (round) Cylinder(platforms, $"Round Platform {i + 1:00}", (x0 + x1) * 0.5f, (z0 + z1) * 0.5f, Mathf.Max(x1 - x0, z1 - z0) * 0.5f, PlatformHeight);
            else Box(platforms, $"Platform {i + 1:00}", x0, x1, 0f, PlatformHeight, z0, z1, Kind.Ledge);
        }

        // Bomb sites
        var sites = Group("Bomb Sites");
        foreach (var s in data.sites)
        {
            var go = new GameObject($"Site {s.letter}");
            go.transform.SetParent(sites, false);
            go.transform.position = new Vector3((s.rect[0] + s.rect[1]) * 0.5f, 0f, (s.rect[2] + s.rect[3]) * 0.5f);
            var site = go.AddComponent<BombSite>();
            site.letter = s.letter;
            site.size = new Vector3(s.rect[1] - s.rect[0], 8f, s.rect[3] - s.rect[2]);
            // An outline painted on the floor so the site reads in the greybox (no colliders)
            const float w = 0.4f;
            float x0 = s.rect[0], x1 = s.rect[1], z0 = s.rect[2], z1 = s.rect[3];
            foreach (var (a, b, c, d) in new[] { (x0, x1, z0, z0 + w), (x0, x1, z1 - w, z1), (x0, x0 + w, z0, z1), (x1 - w, x1, z0, z1) })
                Object.DestroyImmediate(Box(go.transform, $"Site {s.letter} Line", a, b, 0f, 0.02f, c, d, Kind.Ledge).GetComponent<Collider>());
            report.Add($"Site {s.letter}: {site.size.x:0.0} × {site.size.z:0.0} m at ({go.transform.position.x:0.0}, {go.transform.position.z:0.0})");
        }

        // Spawns: tagged for Search & Destroy (other modes use them all)
        var spawnGroup = Group("Spawn Points");
        int n = 0;
        foreach (var sp in data.spawns)
        {
            var go = new GameObject($"Spawn {++n:00} {sp.side}");
            go.transform.SetParent(spawnGroup, false);
            var feet = new Vector3(sp.x, 0.1f, sp.z);
            var look = new Vector3(sp.faceX - sp.x, 0f, sp.faceZ - sp.z);
            go.transform.SetPositionAndRotation(feet, Quaternion.LookRotation(look.sqrMagnitude > 1f ? look : Vector3.forward));
            go.AddComponent<SpawnPoint>().side = sp.side == "Attack" ? SpawnPoint.Side.Attack : SpawnPoint.Side.Defend;
        }

        Physics.SyncTransforms();
        Barriers(data.spawns, "Attack", SpawnPoint.Side.Attack);
        Barriers(data.spawns, "Defend", SpawnPoint.Side.Defend);

        Bounds(Vector3.zero, 400f, -25f, 45f);
        PlaceSpawner(new Vector3(0f, 0.1f, 0f), -20f);
        report.Add($"Map {data.size[0]:0} × {data.size[1]:0} m: {data.buildings.Length / 4} building blocks, {data.boundary.Length / 4} boundary blocks, " +
                   $"{data.thinWalls.Length / 4} thin walls, {data.platforms.Length / 4} platforms, {data.spawns.Length} spawns");

        Finish(scene, ReportPath(name), name, () => RenderOverview(name, data));
    }

    /// <summary>
    /// Adds (or replaces) only the spawn barrier rings in a saved map scene, leaving the rest of it (and any hand edits)
    /// alone.
    /// </summary>
    public static void BuildBarriersOnly(string name)
    {
        var data = JsonUtility.FromJson<Layout>(File.ReadAllText(DataPath(name)));
        var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath(name), UnityEditor.SceneManagement.OpenSceneMode.Single);
        GameObject geometry = null;
        foreach (var g in scene.GetRootGameObjects()) if (g.name == "Geometry") geometry = g;
        var standoff = geometry != null ? geometry.transform.Find(name) : null;
        if (standoff == null) { Debug.LogError($"[{name}] No Geometry/{name} in the scene"); return; }
        for (int i = standoff.childCount - 1; i >= 0; i--)
            if (standoff.GetChild(i).name.StartsWith("Spawn Barriers")) Object.DestroyImmediate(standoff.GetChild(i).gameObject);
        root = standoff;
        report.Clear();
        Physics.SyncTransforms();
        Barriers(data.spawns, "Attack", SpawnPoint.Side.Attack);
        Barriers(data.spawns, "Defend", SpawnPoint.Side.Defend);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        Debug.Log($"[{name}] " + string.Join(" | ", report));
    }

    private const string BarrierMaterial = "Assets/Materials/Effects/SpawnBarrier.mat";
    private const int BarrierSides = 20;
    private const float BarrierHeight = 7f;

    // Pre-round walls: a ring of SpawnBarrier panels round one team's spawn points (7 m beyond the furthest), leaving
    // out panels buried in buildings. Justin moves, adds or deletes them by hand from here.
    private static void Barriers(Spawn[] spawns, string side, SpawnPoint.Side tag)
    {
        Vector3 centre = Vector3.zero;
        int count = 0;
        foreach (var sp in spawns) if (sp.side == side) { centre += new Vector3(sp.x, 0f, sp.z); count++; }
        if (count == 0) return;
        centre /= count;
        float radius = 0f;
        foreach (var sp in spawns) if (sp.side == side) radius = Mathf.Max(radius, Vector3.Distance(centre, new Vector3(sp.x, 0f, sp.z)));
        radius = Mathf.Max(12f, radius + 7f);

        var mat = AssetDatabase.LoadAssetAtPath<Material>(BarrierMaterial);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.25f, 0.62f, 1f, 0.28f) };
            AssetDatabase.CreateAsset(mat, BarrierMaterial);
        }

        var group = Group($"Spawn Barriers {side}");
        float side_ = 2f * radius * Mathf.Sin(Mathf.PI / BarrierSides) + 0.4f; // Overlapping a little: no cracks
        int made = 0;
        for (int i = 0; i < BarrierSides; i++)
        {
            float a0 = i * Mathf.PI * 2f / BarrierSides, a1 = (i + 1) * Mathf.PI * 2f / BarrierSides;
            Vector3 p0 = centre + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius;
            Vector3 p1 = centre + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
            if (Buried(p0) && Buried((p0 + p1) * 0.5f) && Buried(p1)) continue; // All inside buildings: not needed

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Spawn Barrier {side} {++made:00}";
            go.transform.SetParent(group, false);
            go.transform.position = (p0 + p1) * 0.5f + Vector3.up * BarrierHeight * 0.5f;
            go.transform.rotation = Quaternion.LookRotation(p1 - p0);
            go.transform.localScale = new Vector3(0.3f, BarrierHeight, side_);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.AddComponent<SpawnBarrier>().side = tag;
        }
        report.Add($"Spawn barriers {side}: {made} panels, ring {radius:0.0} m round ({centre.x:0.0}, {centre.z:0.0})");
    }

    // Inside a building or the boundary (the floor there is covered by something taller than a low platform)
    private static bool Buried(Vector3 p) =>
        Physics.Raycast(p + Vector3.up * 40f, Vector3.down, out RaycastHit hit, 60f, ~0, QueryTriggerInteraction.Ignore) && hit.point.y > 3f;

    private static void Blocks(Transform parent, string name, float[] r, float height, Kind kind)
    {
        for (int i = 0; i * 4 < r.Length; i++)
            Box(parent, $"{name} {i + 1:000}", r[i * 4], r[i * 4 + 1], 0f, height, r[i * 4 + 2], r[i * 4 + 3], kind);
    }

    // A round low platform (the circles on the plan)
    private static void Cylinder(Transform parent, string name, float x, float z, float radius, float height)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(x, height * 0.5f, z);
        go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
        go.GetComponent<Renderer>().sharedMaterial = ledge;
        GameObjectUtility.SetStaticEditorFlags(go, (StaticEditorFlags)~0);
    }

    private static void RenderOverview(string name, Layout data)
    {
        string key = name.ToLowerInvariant();
        float half = Mathf.Max(data.size[0], data.size[1]) * 0.5f + 8f;
        Shot($"{key}_top", new Vector3(0f, 150f, 0f), Quaternion.Euler(90f, 0f, 0f), true, half);
        Shot($"{key}_view", new Vector3(110f, 90f, -110f), Quaternion.Euler(35f, -45f, 0f), false, 0f);
    }
}
