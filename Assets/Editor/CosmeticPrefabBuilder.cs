using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Makes the cosmetic prefabs (Resources/Cosmetics/{Id}.prefab) from Justin's models: an FBX left in
/// Resources/Cosmetics is first moved to Assets/Models/Cosmetics (an FBX in Resources makes Resources.Load
/// ambiguous). Each prefab is the model under a root, scaled to <see cref="Items"/>' width, with the pivot at the
/// hat's base centre or the plate's centre, facing +Z (the plate's text faces +Z). No colliders.
/// Job BuildCosmetics; menu Turbo Tag/Build Cosmetics.
/// </summary>
public static class CosmeticPrefabBuilder
{
    private const string ModelFolder = "Assets/Models/Cosmetics";
    private const string OutFolder   = "Assets/Resources/Cosmetics";

    // FBX name, prefab id, width (m), pivot at the base (hats) or the centre
    private static readonly (string fbx, string id, float width, bool baseFirst)[] Items =
    {
        ("Cosmetic-Tophat",               "Tophat",       0.95f, true),
        ("Cosmetic-LicensePlate-LAGGING", "LicensePlate", 0.9f,  false),
    };

    [MenuItem("Turbo Tag/Build Cosmetics")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(ModelFolder)) AssetDatabase.CreateFolder("Assets/Models", "Cosmetics");
        foreach (var (fbx, id, width, baseFirst) in Items)
        {
            string inResources = $"{OutFolder}/{fbx}.fbx", model = $"{ModelFolder}/{fbx}.fbx";
            if (File.Exists(inResources) && !File.Exists(model))
            {
                string error = AssetDatabase.MoveAsset(inResources, model);
                if (!string.IsNullOrEmpty(error)) { Debug.LogError($"[Cosmetics] move {fbx}: {error}"); continue; }
            }
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(model);
            if (asset == null) { Debug.LogError($"[Cosmetics] missing {model}"); continue; }

            var root = new GameObject(id);
            var copy = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            copy.name = "Model";
            copy.transform.SetParent(root.transform, false);
            copy.transform.localRotation = asset.transform.localRotation;
            copy.transform.localScale = asset.transform.localScale;

            var b = Bounds(copy);
            float scale = width / Mathf.Max(0.001f, Mathf.Max(b.size.x, b.size.z));
            copy.transform.localScale *= scale;
            b = Bounds(copy);
            Vector3 pivot = baseFirst ? new Vector3(b.center.x, b.min.y, b.center.z) : b.center;
            copy.transform.localPosition -= pivot;
            foreach (var c in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

            PrefabUtility.SaveAsPrefabAsset(root, $"{OutFolder}/{id}.prefab");
            Object.DestroyImmediate(root);
            Debug.Log($"[Cosmetics] {id}: scale {scale:0.###}, size {Bounds(asset).size * scale}");
        }
        AssetDatabase.SaveAssets();
        CosmeticMaker.Build(); // Claude's own cosmetics, built from shapes
        CosmeticIcons.Build(); // Their pictures for the Wardrobe
    }

    private static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs.Length > 0 ? rs[0].bounds : new Bounds(go.transform.position, Vector3.zero);
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }
}
