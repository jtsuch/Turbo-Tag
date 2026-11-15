using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns level-kit models into ready-to-use prefabs (the asset pipeline in CLAUDE.md).
/// For every FBX in Assets/Environment/Meshes without a prefab, makes a prefab variant in
/// Assets/Environment/Prefabs with the same name:
///   - everything on the structure layer (6), so wall-run, climb, ledges, grapples and traps see it
///   - colliders by convention, per mesh:
///       child named *_col or UCX_*  → that mesh is the collider (renderer hidden)
///       child named *_NoCol         → no collider (decoration)
///       child named *_Trigger       → its collider is a trigger
///       box-shaped mesh (fills ≥ 85% of its bounds) → BoxCollider, otherwise an exact MeshCollider
///   - marked Static (pieces with moving parts get their behaviour below and stay non-static)
///   - known interactables get their script: BouncePadv2 (BouncePad), Fan (Fan)
/// Existing prefabs are never changed, so it's safe to run again after adding models.
/// Menu: Turbo Tag/Process Meshes (editor job: ProcessMeshes).
/// Re-exported models: Turbo Tag/Refresh Kit Prefabs (job: RefreshKitPrefabs) rebuilds each prefab whose FBX is newer,
/// in place (same path, so the GUID and every reference stay), except the legacy pieces (Docs/KitStandards.md).
/// </summary>
public static class MeshPrefabBuilder
{
    private const string MeshFolder   = "Assets/Environment/Meshes";
    private const string PrefabFolder = "Assets/Environment/Prefabs";
    private const int    StructureLayer = 6;
    private const float  BoxFillRatio   = 0.85f;

    // Hand-placed in existing maps with overrides on their prefabs: never rebuilt (Docs/KitStandards.md)
    private static readonly HashSet<string> Legacy = new()
    {
        "Crate-1x1m", "HalfWall-2m", "Fan", "BouncePad", "BouncePadv2", "Railing-2m", "Railing-Left", "Railing-Right", "Vent",
        "Vent 1", "TeleportPad", "TallStructure1", "LowStruture1", "EdgeTrim-2m", "Checkpoint", "Donut", "Spinner", "Lever",
        "Button", "Door", "QuestionMark", "Pillar-2m", "Floor-4x4m", "Wall-4m", "Wall-8m", "BoostPad-2x2",
    };

    [MenuItem("Turbo Tag/Refresh Kit Prefabs")]
    public static void RefreshKitPrefabs() => Run(refresh: true);

    [MenuItem("Turbo Tag/Process Meshes")]
    public static void ProcessMeshes() => Run(refresh: false);

    private static void Run(bool refresh)
    {
        var log = new StringBuilder("[Process Meshes]\n");
        int made = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { MeshFolder }))
        {
            string modelPath  = AssetDatabase.GUIDToAssetPath(guid);
            if (modelPath.StartsWith("Assets/Resources/Cosmetics/")) continue; // Cosmetics never get colliders, layers or Static
            string name       = Path.GetFileNameWithoutExtension(modelPath);
            string prefabPath = $"{PrefabFolder}/{name}.prefab";
            if (File.Exists(prefabPath))
            {
                if (!refresh || Legacy.Contains(name) || File.GetLastWriteTimeUtc(modelPath) <= File.GetLastWriteTimeUtc(prefabPath)) continue;
                log.AppendLine($"  ~ {name}: model re-exported, rebuilding");
            }

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null) { log.AppendLine($"  ! {name}: could not load"); continue; }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                string colliders = AddColliders(instance);
                string behaviour = AddBehaviour(name, instance);
                bool   moving    = behaviour != null;

                foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                {
                    t.gameObject.layer = StructureLayer;
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, moving ? 0 : (StaticEditorFlags)~0);
                }

                PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                made++;
                log.AppendLine($"  + {name}: {colliders}{(moving ? ", " + behaviour : ", static")}");
            }
            finally { Object.DestroyImmediate(instance); }
        }

        log.AppendLine(made == 0 ? "  Nothing new." : $"  {made} prefab(s) created in {PrefabFolder}.");
        Debug.Log(log.ToString());
    }

    // ─── Colliders ────────────────────────────────────────────────────────────

    private static string AddColliders(GameObject root)
    {
        int boxes = 0, meshes = 0, custom = 0, skipped = 0;
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            var go   = filter.gameObject;
            var mesh = filter.sharedMesh;
            if (mesh == null) continue;
            string n = go.name;

            if (n.EndsWith("_NoCol")) { skipped++; continue; }

            bool trigger = n.EndsWith("_Trigger");
            if (n.EndsWith("_col") || n.StartsWith("UCX_"))
            {
                var renderer = go.GetComponent<MeshRenderer>();
                if (renderer != null) renderer.enabled = false;
                var shape = go.AddComponent<MeshCollider>();
                shape.convex    = true;
                shape.isTrigger = trigger;
                custom++;
                continue;
            }

            // Hand-made collider shapes next to it or under it: leave this mesh visual only
            if (HasCustomCollider(go.transform) || HasCustomCollider(go.transform.parent)) { skipped++; continue; }

            if (IsBoxShaped(mesh))
            {
                var box = go.AddComponent<BoxCollider>(); // Fits the mesh bounds when added
                box.isTrigger = trigger;
                boxes++;
            }
            else
            {
                var exact = go.AddComponent<MeshCollider>();
                exact.sharedMesh = mesh;
                exact.convex     = trigger; // Triggers must be convex
                exact.isTrigger  = trigger;
                meshes++;
            }
        }
        return $"{boxes} box, {meshes} mesh, {custom} custom collider(s){(skipped > 0 ? $", {skipped} without" : "")}";
    }

    private static bool HasCustomCollider(Transform parent)
    {
        if (parent == null) return false;
        foreach (Transform child in parent)
            if (child.name.EndsWith("_col") || child.name.StartsWith("UCX_")) return true;
        return false;
    }

    // Box-like when the closed mesh fills most of its bounding box
    private static bool IsBoxShaped(Mesh mesh)
    {
        Vector3 size = mesh.bounds.size;
        float boundsVolume = size.x * size.y * size.z;
        if (boundsVolume <= 1e-12f) return true; // Flat: a thin box is the best fit

        var   vertices  = mesh.vertices;
        var   triangles = mesh.triangles;
        float volume    = 0f;
        for (int i = 0; i + 2 < triangles.Length; i += 3)
            volume += Vector3.Dot(vertices[triangles[i]], Vector3.Cross(vertices[triangles[i + 1]], vertices[triangles[i + 2]])) / 6f;

        return Mathf.Abs(volume) >= BoxFillRatio * boundsVolume;
    }

    // ─── Interactables ────────────────────────────────────────────────────────

    /// <summary>Adds the behaviour script for known interactables. Returns a description, or null for plain kit.</summary>
    private static string AddBehaviour(string name, GameObject root)
    {
        switch (name)
        {
            case "BouncePadv2": return SetUpBouncePad(root);
            case "Fan":         return SetUpFan(root);
            default:            return null;
        }
    }

    /// <summary>
    /// BouncePad launches along its object's forward axis. The model root is imported rotated so its
    /// forward points up, so the script and a launch trigger go on the root: the solid pad stays
    /// walkable, and anything entering the zone just above it is launched.
    /// </summary>
    public static string SetUpBouncePad(GameObject padRoot, float triggerHeight = 0.6f)
    {
        var filter = padRoot.GetComponentInChildren<MeshFilter>();
        var zone   = filter.gameObject.AddComponent<BoxCollider>();
        zone.isTrigger = true;

        // Mesh space is centimetres under a ×100 scale: grow the box along the mesh's forward (world up)
        Bounds b      = filter.sharedMesh.bounds;
        float  extra  = triggerHeight / Mathf.Max(0.0001f, filter.transform.lossyScale.z);
        zone.size     = new Vector3(b.size.x, b.size.y, b.size.z + extra);
        zone.center   = b.center + Vector3.forward * (extra * 0.5f);

        if (filter.GetComponent<BouncePad>() == null) filter.gameObject.AddComponent<BouncePad>();
        return "BouncePad + launch trigger";
    }

    // The fan blows along its own forward: a "Wind" child pointing up carries the script; the blades spin
    private static string SetUpFan(GameObject root)
    {
        var wind = new GameObject("Wind");
        wind.transform.SetParent(root.transform, false);
        wind.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // Forward = up
        var fan = wind.AddComponent<Fan>();

        var so = new SerializedObject(fan);
        var blades = root.transform.Find("FanBlades");
        so.FindProperty("spinPart").objectReferenceValue = blades;
        so.FindProperty("force").floatValue = 50f;  // Above gravity (40), so it lifts players
        so.FindProperty("range").floatValue = 10f;  // The fan is ~7 m across
        so.ApplyModifiedPropertiesWithoutUndo();
        return "Fan (Wind child blows up, FanBlades spin)";
    }
}
