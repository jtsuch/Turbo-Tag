using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Level workflow tools ("Turbo Tag/Levels" menu) plus automatic Build Settings sync:
/// every scene in Assets/Scenes/Levels (except names starting with "_") is kept in Build
/// Settings, which is what the lobby's map list reads at runtime. Drop a scene in that folder
/// and it becomes a selectable map.
/// </summary>
[InitializeOnLoad]
public class LevelTools : AssetPostprocessor
{
    private const string LevelFolder    = "Assets/Scenes/Levels";
    private const string TemplateScene  = LevelFolder + "/_LevelTemplate.unity";
    private const string SystemsPrefab  = "Assets/Prefabs/Level/LevelSystems.prefab";
    private const string GeometryRoot   = "Geometry";

    static LevelTools() => EditorApplication.delayCall += SyncBuildSettings;

    // ─── Build Settings sync ──────────────────────────────────────────────────

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        bool touchesLevels = imported.Concat(deleted).Concat(moved).Concat(movedFrom)
            .Any(p => p.EndsWith(".unity") && p.StartsWith(LevelFolder));
        if (touchesLevels) EditorApplication.delayCall += SyncBuildSettings;
    }

    [MenuItem("Turbo Tag/Levels/Sync Build Settings")]
    public static void SyncBuildSettings()
    {
        // Multiplayer Play Mode clones share the main project's settings; only the main editor edits them.
        if (IsPlayModeClone) return;

        var current = EditorBuildSettings.scenes;

        // Menu scenes keep their order; missing ones are dropped. Existence is checked through the
        // AssetDatabase, not File.Exists: a relative file path depends on the working directory, which
        // made a clone (running from Library/VP) drop every menu scene from the list.
        var result = current
            .Where(s => SceneExists(s.path) && !s.path.StartsWith(LevelFolder + "/"))
            .ToList();

        var levels = AssetDatabase.FindAssets("t:Scene", new[] { LevelFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => !Path.GetFileName(p).StartsWith("_"))
            .OrderBy(p => p, System.StringComparer.Ordinal);

        foreach (string path in levels)
        {
            var existing = current.FirstOrDefault(s => s.path == path);
            result.Add(existing ?? new EditorBuildSettingsScene(path, true));
        }

        bool changed = result.Count != current.Length
                    || result.Where((s, i) => s.path != current[i].path).Any();
        if (!changed) return;

        EditorBuildSettings.scenes = result.ToArray();
        Debug.Log($"[LevelTools] Build Settings synced: {result.Count(s => s.path.StartsWith(LevelFolder))} maps.");
    }

    private static bool SceneExists(string path) =>
        !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets));

    /// <summary>True inside a Multiplayer Play Mode virtual player (its project lives under Library/VP).</summary>
    private static bool IsPlayModeClone => Application.dataPath.Replace('\\', '/').Contains("/Library/VP/");

    // ─── New level ────────────────────────────────────────────────────────────

    [MenuItem("Turbo Tag/Levels/New Level From Template")]
    private static void NewLevelFromTemplate()
    {
        if (!RequireAsset(TemplateScene) || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        string path = EditorUtility.SaveFilePanelInProject("New Level", "NewLevel", "unity",
            "Name the level — this is also its map name in the lobby.", LevelFolder);
        if (string.IsNullOrEmpty(path)) return;

        AssetDatabase.CopyAsset(TemplateScene, path);
        EditorSceneManager.OpenScene(path);
    }

    // ─── Upgrade old levels ───────────────────────────────────────────────────

    /// <summary>
    /// Gives every level the shared LevelSystems prefab, replacing older per-scene copies of the
    /// HUD canvas, EventSystem, GameModeManager, Spawner and debug bootstrapper. The old Spawner's
    /// position is kept. Stray scene cameras are disabled (the player brings its own).
    /// </summary>
    [MenuItem("Turbo Tag/Levels/Upgrade All Levels To LevelSystems")]
    private static void UpgradeAllLevelsMenu()
    {
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) UpgradeAllLevels();
    }

    /// <summary>Unattended version (no prompts) — also run by <see cref="EditorJobQueue"/>.</summary>
    public static void UpgradeAllLevels()
    {
        if (!RequireAsset(SystemsPrefab)) return;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SystemsPrefab);

        var report = new List<string>();
        foreach (string path in LevelScenePaths())
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();

            if (roots.Any(r => PrefabUtility.GetCorrespondingObjectFromSource(r) == prefab))
            {
                report.Add($"{scene.name}: already up to date");
                continue;
            }

            Vector3? spawnPosition = null;
            var removed = new List<string>();
            foreach (var root in roots)
            {
                if (!IsOldSystemsRoot(root)) continue;

                var spawner = root.GetComponentInChildren<Spawner>(true);
                if (spawner != null) spawnPosition = spawner.transform.position;

                removed.Add(root.name);
                Object.DestroyImmediate(root);
            }

            var systems = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            systems.transform.SetAsFirstSibling();
            var newSpawner = systems.GetComponentInChildren<Spawner>(true);
            if (newSpawner != null) newSpawner.transform.position = spawnPosition ?? new Vector3(0f, 0.5f, 0f);

            int cameras = DisableStrayCameras(scene, systems);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            report.Add($"{scene.name}: added LevelSystems" +
                       (removed.Count > 0 ? $", replaced [{string.Join(", ", removed)}]" : "") +
                       (spawnPosition == null ? ", spawner placed at origin — move it" : "") +
                       (cameras > 0 ? $", disabled {cameras} scene camera(s)" : ""));
        }

        Debug.Log("[LevelTools] Upgrade complete:\n" + string.Join("\n", report));
        AssignSceneViewIds();
    }

    // ─── Stray spawners ───────────────────────────────────────────────────────

    [MenuItem("Turbo Tag/Levels/Remove Stray Spawners")]
    private static void RemoveStraySpawnersMenu()
    {
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) RemoveStraySpawners();
    }

    /// <summary>
    /// Keeps only the LevelSystems Spawner in each level. A leftover Spawner outside the prefab spawned a
    /// second local player. If the LevelSystems Spawner still sits at the default spot, it takes the
    /// leftover's position (that one was placed by hand).
    /// </summary>
    public static void RemoveStraySpawners()
    {
        if (!RequireAsset(SystemsPrefab)) return;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SystemsPrefab);

        var report = new List<string>();
        foreach (string path in LevelScenePaths())
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var systems = scene.GetRootGameObjects().FirstOrDefault(r => PrefabUtility.GetCorrespondingObjectFromSource(r) == prefab);
            var kept = systems != null ? systems.GetComponentInChildren<Spawner>(true) : null;
            var strays = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Spawner>(true))
                .Where(s => s != kept)
                .ToList();
            if (kept == null || strays.Count == 0) continue;

            if (Vector3.Distance(kept.transform.position, new Vector3(0f, 0.5f, 0f)) < 0.01f)
                kept.transform.position = strays[0].transform.position;

            foreach (var stray in strays) Object.DestroyImmediate(stray);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            report.Add($"{scene.name}: removed {strays.Count} stray Spawner(s); LevelSystems Spawner at {kept.transform.position}");
        }

        Debug.Log("[LevelTools] Stray spawners:\n" + (report.Count > 0 ? string.Join("\n", report) : "none"));
    }

    // ─── Scene PhotonView ids ─────────────────────────────────────────────────

    [MenuItem("Turbo Tag/Levels/Assign Scene PhotonView IDs")]
    private static void AssignSceneViewIdsMenu()
    {
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) AssignSceneViewIds();
    }

    /// <summary>
    /// Gives every PhotonView placed in a level a unique scene view id. A scene view left at 0 is
    /// invalid at runtime ("Illegal view ID:0" on RPCs, e.g. GameModeManager.RPC_RequestTag). PUN
    /// assigns ids when a scene is edited by hand, but not for objects added by scripts such as
    /// <see cref="UpgradeAllLevels"/>. Ids already in use are kept; zeros and duplicates get new ones.
    /// </summary>
    public static void AssignSceneViewIds()
    {
        var report = new List<string>();
        foreach (string path in LevelScenePaths())
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var views = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Photon.Pun.PhotonView>(true))
                .ToList();

            var used  = new HashSet<int>();
            var toFix = new List<Photon.Pun.PhotonView>();
            foreach (var view in views)
            {
                int id = view.sceneViewId;
                if (id <= 0 || !used.Add(id)) toFix.Add(view);
            }

            int next = 1;
            foreach (var view in toFix)
            {
                while (used.Contains(next)) next++;
                var so = new SerializedObject(view);
                so.FindProperty("sceneViewId").intValue = next;
                so.ApplyModifiedPropertiesWithoutUndo();
                used.Add(next);
            }

            if (toFix.Count > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            report.Add($"{scene.name}: {views.Count} scene views, {toFix.Count} given new ids");
        }

        Debug.Log("[LevelTools] Scene PhotonView ids:\n" + string.Join("\n", report));
    }

    // A root counts as old plumbing only if it holds systems components and no level geometry
    private static bool IsOldSystemsRoot(GameObject root)
    {
        bool isSystems = root.GetComponentInChildren<GameModeManager>(true) != null
                      || root.GetComponentInChildren<Spawner>(true) != null
                      || root.GetComponentInChildren<HUDManager>(true) != null
                      || root.GetComponentInChildren<PauseMenuManager>(true) != null
                      || root.GetComponent<EventSystem>() != null
                      || root.GetComponentInChildren<DebugSceneBootstrapper>(true) != null;
        bool hasGeometry = root.GetComponentsInChildren<MeshRenderer>(true).Any()
                        || root.GetComponentsInChildren<Terrain>(true).Any();
        return isSystems && !hasGeometry;
    }

    private static int DisableStrayCameras(UnityEngine.SceneManagement.Scene scene, GameObject systems)
    {
        int count = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root == systems) continue;
            foreach (var cam in root.GetComponentsInChildren<Camera>(true))
            {
                if (!cam.gameObject.activeSelf) continue;
                cam.gameObject.SetActive(false);
                count++;
            }
        }
        return count;
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static IEnumerable<string> LevelScenePaths() =>
        AssetDatabase.FindAssets("t:Scene", new[] { LevelFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => !Path.GetFileName(p).StartsWith("_"))
            .OrderBy(p => p);

    private static bool RequireAsset(string path)
    {
        if (File.Exists(path)) return true;
        Debug.LogError($"[LevelTools] Missing {path}.");
        return false;
    }
}
