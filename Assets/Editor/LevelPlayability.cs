using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Makes every level as playable as Improvement: 12 spread-out SpawnPoints (if it has none), a DeathZone kill plane
/// under the level (if it has none) and the Spawner's kill height just above it; also reports what each level has
/// (walkable area, colliders off the structure layer, ceiling) to Library/TurboTag/scratch/level_audit.txt.
/// Spawn spots are found the same way donuts find theirs: open, flat structure-layer ground with headroom, away
/// from death zones, spread by farthest-point sampling. Levels in <see cref="Skip"/> are left alone.
/// Menu: Turbo Tag/Levels/Make Levels Playable (editor job: MakeLevelsPlayable). Audit only: job AuditLevels.
/// </summary>
public static class LevelPlayability
{
    private const string LevelsFolder = "Assets/Scenes/Levels";
    private const string AuditPath    = "Library/TurboTag/scratch/level_audit.txt";
    private const int    Structure = 6, WantedSpawns = 12;
    private static readonly HashSet<string> Skip = new();
    // Lowest floor a spawn may use, where the bottom of the level isn't somewhere to start (Pitfall's pit)
    private static readonly Dictionary<string, float> MinSpawnY = new() { ["Pitfall"] = 20f };

    [MenuItem("Turbo Tag/Levels/Make Levels Playable")]
    public static void MakePlayable() => Run(true);

    public static void Audit() => Run(false);

    private static void Run(bool fix)
    {
        var log = new StringBuilder();
        foreach (string file in Directory.GetFiles(LevelsFolder, "*.unity"))
        {
            string map = Path.GetFileNameWithoutExtension(file);
            if (map.StartsWith("_")) continue;
            var scene = EditorSceneManager.OpenScene(file.Replace('\\', '/'), OpenSceneMode.Single);
            Physics.SyncTransforms();
            log.AppendLine($"=== {map} ===");

            var walk = WalkableBounds(out int structureCols, out int otherCols);
            var spawner = Object.FindFirstObjectByType<Spawner>(FindObjectsInactive.Include);
            var spawns = Object.FindObjectsByType<SpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var zones = Object.FindObjectsByType<DeathZone>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            float kill = spawner != null ? new SerializedObject(spawner).FindProperty("killHeight").floatValue : float.NaN;
            log.AppendLine($"  walkable bounds {walk.min} .. {walk.max}; structure colliders {structureCols}, other solid colliders {otherCols}");
            if (!fix)
            {
                foreach (var z in zones) if (z.TryGetComponent(out Collider zc)) log.AppendLine($"  death zone {z.name}: {zc.bounds.min} .. {zc.bounds.max}");
                foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None)
                             .Where(c => !c.isTrigger && c.gameObject.layer == Structure)
                             .OrderByDescending(c => c.bounds.size.x * c.bounds.size.z).Take(6))
                    log.AppendLine($"  big: {c.name} {c.bounds.min} .. {c.bounds.max}");
            }
            log.AppendLine($"  spawner {(spawner != null ? spawner.transform.position.ToString() : "MISSING")}, kill height {kill}, spawn points {spawns.Length}, death zones {zones.Length}");

            if (!fix || Skip.Contains(map)) { log.AppendLine(Skip.Contains(map) ? "  (skipped)" : ""); continue; }

            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Geometry")?.transform;
            if (root == null) root = new GameObject("Geometry").transform;
            bool changed = false;

            // Kill plane: a trigger slab 25 m under the lowest walkable surface, wider than the level
            float floorY = walk.min.y;
            float planeY = floorY - 25f;
            if (!zones.Any(z => z.GetComponent<Collider>() != null && z.GetComponent<Collider>().bounds.max.y < floorY - 1f))
            {
                var kp = new GameObject("Kill Plane (auto)");
                kp.transform.SetParent(root, false);
                kp.transform.position = new Vector3(walk.center.x, planeY, walk.center.z);
                var box = kp.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(walk.size.x + 400f, 4f, walk.size.z + 400f);
                kp.AddComponent<DeathZone>();
                log.AppendLine($"  + kill plane at y {planeY:0.0}");
                changed = true;
            }
            if (spawner != null && (float.IsNaN(kill) || kill < planeY - 5f || kill > floorY - 3f))
            {
                var so = new SerializedObject(spawner);
                so.FindProperty("killHeight").floatValue = floorY - 15f;
                so.ApplyModifiedPropertiesWithoutUndo();
                log.AppendLine($"  ~ kill height {kill} → {floorY - 15f:0.0}");
                changed = true;
            }

            // Spawn points (the auto group is rebuilt each run; hand-placed ones are kept)
            var auto = root.Find("Spawn Points (auto)");
            if (auto != null && spawns.All(sp => sp.transform.IsChildOf(auto)))
            {
                Object.DestroyImmediate(auto.gameObject);
                spawns = new SpawnPoint[0];
                Physics.SyncTransforms();
            }
            if (spawns.Length == 0)
            {
                var spots = FindSpots(walk, spawner != null ? spawner.transform.position : walk.center,
                                      MinSpawnY.TryGetValue(map, out float lowest) ? lowest : float.NegativeInfinity);
                var group = new GameObject("Spawn Points (auto)").transform;
                group.SetParent(root, false);
                for (int i = 0; i < spots.Count; i++)
                {
                    var go = new GameObject($"Spawn {i + 1:00}");
                    go.transform.SetParent(group, false);
                    Vector3 look = walk.center - spots[i]; look.y = 0f;
                    go.transform.SetPositionAndRotation(spots[i] + Vector3.up * 0.1f,
                        Quaternion.LookRotation(look.sqrMagnitude > 1f ? look : Vector3.forward));
                    go.AddComponent<SpawnPoint>();
                }
                log.AppendLine($"  + {spots.Count} spawn points: {string.Join(" ", spots.Select(s => $"({s.x:0},{s.y:0},{s.z:0})"))}");
                changed = true;
            }

            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            log.AppendLine();
        }
        Directory.CreateDirectory(Path.GetDirectoryName(AuditPath));
        File.WriteAllText(AuditPath, log.ToString());
        Debug.Log("[LevelPlayability]\n" + log);
    }

    // Bounds of the solid structure-layer geometry (no death zones, no huge planes)
    private static Bounds WalkableBounds(out int structureCols, out int otherCols)
    {
        structureCols = otherCols = 0;
        Bounds? total = null;
        foreach (var col in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            if (col.isTrigger || !col.enabled) continue;
            if (col.GetComponentInParent<DeathZone>() != null) continue;
            if (col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic) continue;
            if (col.gameObject.layer != Structure) { if (col.gameObject.layer == 0) otherCols++; continue; }
            structureCols++;
            var b = col.bounds;
            if (b.size.x > 1500f || b.size.z > 1500f || b.size.x * b.size.z > 150000f) continue; // Terrain-sized planes
            if (total.HasValue) { var t = total.Value; t.Encapsulate(b); total = t; } else total = b;
        }
        return total ?? new Bounds(Vector3.zero, Vector3.one);
    }

    // Open, flat ground on the structure layer with standing room, spread out (farthest-point sampling)
    private static List<Vector3> FindSpots(Bounds walk, Vector3 seed, float lowest)
    {
        // A height band around where the level starts you: the floors people actually play on, not high roofs
        float minY = Mathf.Max(walk.min.y - 1f, lowest), maxY = seed.y + 15f;
        int mask = 1 << Structure;
        var candidates = new List<Vector3>();
        float step = Mathf.Max(2.5f, Mathf.Max(walk.size.x, walk.size.z) / 60f);
        for (float x = walk.min.x + step / 2; x < walk.max.x; x += step)
        for (float z = walk.min.z + step / 2; z < walk.max.z; z += step)
        {
            // Every floor in this column, top down
            Vector3 from = new(x, walk.max.y + 5f, z);
            var hits = Physics.RaycastAll(from, Vector3.down, walk.size.y + 10f, ~0, QueryTriggerInteraction.Collide)
                              .OrderBy(h => h.distance);
            foreach (var hit in hits)
            {
                if (hit.collider.isTrigger)
                {
                    if (hit.collider.GetComponentInParent<DeathZone>() != null) break; // Anything under a death zone is out
                    continue;
                }
                if (hit.collider.gameObject.layer != Structure || hit.normal.y < 0.95f) continue;
                if (hit.point.y < minY || hit.point.y > maxY) continue;
                string n = hit.collider.name.ToLowerInvariant();
                if (n.Contains("border") || n.Contains("boundary")) continue; // Never on top of the boundary walls
                if (hit.collider.GetComponentInParent<DeathZone>() != null) continue;
                Vector3 p = hit.point;
                if (Physics.CheckCapsule(p + Vector3.up * 0.8f, p + Vector3.up * 2.4f, 0.75f, ~0, QueryTriggerInteraction.Ignore)) continue;
                // Room around (no edge within 1.5 m)
                bool solid = true;
                foreach (var d in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                    if (!Physics.Raycast(p + d * 1.5f + Vector3.up * 0.5f, Vector3.down, 1.2f, mask)) { solid = false; break; }
                if (solid) candidates.Add(p);
            }
        }
        var chosen = new List<Vector3>();
        if (candidates.Count == 0) return chosen;
        // Start nearest the old spawner, then keep taking the candidate farthest from everything chosen
        chosen.Add(candidates.OrderBy(c => (c - seed).sqrMagnitude).First());
        while (chosen.Count < WantedSpawns && chosen.Count < candidates.Count)
        {
            Vector3 best = default; float bestD = -1f;
            foreach (var c in candidates)
            {
                float d = chosen.Min(s => (s - c).sqrMagnitude);
                if (d > bestD) { bestD = d; best = c; }
            }
            chosen.Add(best);
        }
        return chosen;
    }
}
