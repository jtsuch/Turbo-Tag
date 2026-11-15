#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Swaps the visuals of the ability object prefabs in Resources/Object for the new low-poly models in
/// Assets/Models/Abilities, and makes model-only prefabs for the new objects that had none yet.
///
/// Only the visual children (anything with a mesh, or a nested model instance) are replaced; the root, its scripts,
/// PhotonView, Rigidbody and colliders are left exactly as they were. The new model is rotated/scaled so it covers the
/// same space as the visuals it replaced (see <see cref="Fit"/>), so throw feel and hit boxes stay the same.
/// Run it from Tools/Turbo Tag/Apply Ability Models (editor job ApplyAbilityModels). It is safe to run again: a model
/// from an earlier run keeps its fit (MatchOld) or is fitted afresh (FixedScale). The models without a script of their
/// own are used from code: SnowPile (Snow Screen's wall), SearchDestroyBomb (the S&D bomb), AirHorn (Honk).
/// </summary>
public static class ApplyAbilityModels
{
    private const string ModelFolder  = "Assets/Models/Abilities/";
    private const string PrefabFolder = "Assets/Resources/Object/";

    /// <summary>How big the model ends up.</summary>
    private enum Fit
    {
        MatchOld,   // Largest side equals the largest side of the visuals it replaces
        FixedScale  // Uniform scale = Value (model units are metres)
    }

    private sealed class Entry
    {
        public string prefab, model;
        public Vector3 rotation;
        public Fit fit;
        public float value;
        public bool createIfMissing;
        public bool centre;   // A new model sits centred on the root (a thrown thing), not standing on it

        public Entry(string prefab, string model, Vector3 rotation, Fit fit, float value = 1f, bool create = false, bool centre = false)
        {
            this.prefab = prefab; this.model = model; this.rotation = rotation; this.fit = fit; this.value = value;
            createIfMissing = create; this.centre = centre;
        }
    }

    // Tunables live here: rotation orients the model to the prefab's forward (+Z for darts/fish) or long axis (+Z for
    // the can and bottle); FixedScale values are for objects that did not exist before.
    private static readonly Entry[] Entries =
    {
        new("Smoke",      "SmokeGrenade",      new Vector3(90f, 0f, 0f),  Fit.MatchOld),
        new("Frag",       "FragGrenade",       Vector3.zero,              Fit.MatchOld),
        new("Molly",      "Molotov",           new Vector3(90f, 0f, 0f),  Fit.MatchOld),
        new("ReconBolt",  "ReconDart",         new Vector3(0f, 180f, 0f), Fit.MatchOld),
        new("ShockDart",  "ShockDart",         new Vector3(0f, 180f, 0f), Fit.MatchOld),
        new("Suppressor", "SuppressionDevice", Vector3.zero,              Fit.MatchOld),
        new("Nuke",       "Nuke",              Vector3.zero,              Fit.MatchOld),
        // The old bait had no mesh to match (the fish came in 2.3 m long): ~0.6 m, centred on the bait's collider
        new("FishBait",   "Fish",              new Vector3(0f, 180f, 0f), Fit.FixedScale, 0.27f, centre: true),
        // New objects (no prefab before): model only, add scripts as the abilities get built
        new("SnowPile",         "SnowPile",         Vector3.zero, Fit.FixedScale, 1f,   true),
        new("SearchDestroyBomb","SearchDestroyBomb",Vector3.zero, Fit.FixedScale, 0.5f, true),
        new("AirHorn",          "AirHorn",          Vector3.zero, Fit.FixedScale, 0.35f,true),
    };

    [MenuItem("Tools/Turbo Tag/Apply Ability Models")]
    public static void Apply()
    {
        int done = 0;
        foreach (var e in Entries)
            if (ApplyOne(e)) done++;
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Apply Ability Models] Updated {done} of {Entries.Length} prefabs.");
    }

    private static bool ApplyOne(Entry e)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelFolder + e.model + ".fbx");
        if (model == null) { Debug.LogWarning($"[Apply Ability Models] Model not found: {e.model}"); return false; }

        string path = PrefabFolder + e.prefab + ".prefab";
        bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
        if (!exists && !e.createIfMissing) { Debug.LogWarning($"[Apply Ability Models] Prefab not found: {path}"); return false; }

        GameObject root = exists ? PrefabUtility.LoadPrefabContents(path) : new GameObject(e.prefab);
        try
        {
            Matrix4x4 toRoot = root.transform.worldToLocalMatrix;
            var old = new Bounds();
            bool hasOld = false;

            // 1. Visual children out (the model from an earlier run counts too: its fit is kept, since the visuals it
            //    was matched to are gone)
            var remove = new List<GameObject>();
            Vector3? keptScale = null, keptPosition = null;
            foreach (Transform child in root.transform)
            {
                bool visual = child.GetComponent<MeshFilter>() != null || child.GetComponent<MeshRenderer>() != null
                              || child.name == "Model" || PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject);
                if (visual) remove.Add(child.gameObject);
            }
            foreach (var go in remove)
            {
                if (go.name != "Model") Encapsulate(go, toRoot, ref old, ref hasOld);
                else { keptScale = go.transform.localScale; keptPosition = go.transform.localPosition; }
                Object.DestroyImmediate(go);
            }

            // 2. New model in, oriented and sized
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            inst.name = "Model";
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.Euler(e.rotation);
            inst.transform.localScale = Vector3.one;

            var nb = new Bounds(); bool hasNew = false;
            Encapsulate(inst, toRoot, ref nb, ref hasNew);
            if (!hasNew) { Debug.LogWarning($"[Apply Ability Models] {e.model} has no mesh"); return false; }

            if (e.fit == Fit.MatchOld && !hasOld && keptScale.HasValue)
            {
                inst.transform.localScale = keptScale.Value;
                inst.transform.localPosition = keptPosition.Value;
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[Apply Ability Models] {e.prefab}: kept the earlier fit of '{e.model}'");
                return true;
            }
            float scale = e.value;
            if (e.fit == Fit.MatchOld)
                scale = hasOld ? Mathf.Max(old.size.x, old.size.y, old.size.z) / Mathf.Max(nb.size.x, nb.size.y, nb.size.z) : 1f;
            inst.transform.localScale = Vector3.one * scale;

            nb = new Bounds(); hasNew = false;
            Encapsulate(inst, toRoot, ref nb, ref hasNew);
            // Same centre as what it replaced; new objects stand on the origin
            Vector3 offset = hasOld ? old.center - nb.center
                : e.centre ? -nb.center
                : new Vector3(-nb.center.x, -nb.min.y, -nb.center.z);
            inst.transform.localPosition = offset;

            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log($"[Apply Ability Models] {e.prefab}: removed {remove.Count} visuals, model '{e.model}' x{scale:0.###}");
            return true;
        }
        finally
        {
            if (exists) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }
    }

    /// <summary>Grows b to cover every mesh under go, in the root's space.</summary>
    private static void Encapsulate(GameObject go, Matrix4x4 toRoot, ref Bounds b, ref bool has)
    {
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
            Bounds mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = m.MultiplyPoint3x4(c);
                if (!has) { b = new Bounds(p, Vector3.zero); has = true; }
                else b.Encapsulate(p);
            }
        }
    }
}
#endif
