using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Claude's cosmetics, built from simple shapes (spheres, cylinders, boxes, a generated cone) with their own
/// materials (Resources/Cosmetics/Materials) into Resources/Cosmetics/{Id}.prefab. Space: hats stand on their
/// base (y = 0 at the top of the head, +Z forward); back items sit at the back anchor (lower back) with +Z
/// pointing away from the body and +Y up. Run with job BuildCosmetics (after Justin's models).
/// </summary>
public static class CosmeticMaker
{
    private const string Out = "Assets/Resources/Cosmetics";
    private const string MatFolder = Out + "/Materials";

    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(MatFolder)) AssetDatabase.CreateFolder(Out, "Materials");
        Mesh cone = ConeMesh();

        // ── Hats ──
        Make("PartyHat", root =>
        {
            Part(root, cone, "Cone", new Vector3(0f, 0f, 0f), new Vector3(0.62f, 0.8f, 0.62f), new Vector3(-6f, 0f, 0f), Mat("Party Pink", "#FF4FA3"));
            Part(root, PrimitiveType.Sphere, "Pom", new Vector3(0f, 0.8f, 0.08f), Vector3.one * 0.2f, Vector3.zero, Mat("Party Yellow", "#FFD23F"));
            Part(root, PrimitiveType.Cylinder, "Band", new Vector3(0f, 0.12f, 0f), new Vector3(0.56f, 0.03f, 0.56f), new Vector3(-6f, 0f, 0f), Mat("Party Cyan", "#3DE0FF"));
        });
        Make("PropellerCap", root =>
        {
            Part(root, PrimitiveType.Sphere, "Cap", new Vector3(0f, -0.05f, 0f), new Vector3(0.9f, 0.62f, 0.9f), Vector3.zero, Mat("Cap Blue", "#2E6BFF"));
            Part(root, PrimitiveType.Cylinder, "Brim", new Vector3(0f, -0.02f, 0.42f), new Vector3(0.55f, 0.02f, 0.4f), new Vector3(-8f, 0f, 0f), Mat("Cap Yellow", "#FFD23F"));
            Part(root, PrimitiveType.Cylinder, "Stick", new Vector3(0f, 0.32f, 0f), new Vector3(0.05f, 0.08f, 0.05f), Vector3.zero, Mat("Metal", "#B8BCC8", 0.8f, 0.6f));
            var blades = new GameObject("Blades").transform;
            blades.SetParent(root, false);
            blades.localPosition = new Vector3(0f, 0.41f, 0f);
            Part(blades, PrimitiveType.Cube, "Blade A", Vector3.zero, new Vector3(0.8f, 0.02f, 0.11f), new Vector3(0f, 0f, 8f), Mat("Cap Red", "#FF3B4E"));
            Part(blades, PrimitiveType.Cube, "Blade B", Vector3.zero, new Vector3(0.11f, 0.02f, 0.8f), new Vector3(8f, 0f, 0f), Mat("Cap Red", "#FF3B4E"));
            blades.gameObject.AddComponent<CosmeticSpin>();
        });
        Make("VikingHelmet", root =>
        {
            var metal = Mat("Helmet Steel", "#9AA3B2", 0.85f, 0.55f);
            Part(root, PrimitiveType.Sphere, "Dome", new Vector3(0f, -0.08f, 0f), new Vector3(0.98f, 0.8f, 0.98f), Vector3.zero, metal);
            Part(root, PrimitiveType.Cylinder, "Band", new Vector3(0f, 0.0f, 0f), new Vector3(1.0f, 0.05f, 1.0f), Vector3.zero, Mat("Leather", "#7A4A24"));
            Part(root, PrimitiveType.Cube, "Nose Guard", new Vector3(0f, -0.08f, 0.49f), new Vector3(0.09f, 0.3f, 0.05f), Vector3.zero, metal);
            var horn = Mat("Horn", "#F2E6C9");
            Part(root, cone, "Horn L", new Vector3(-0.42f, 0.12f, 0f), new Vector3(0.2f, 0.5f, 0.2f), new Vector3(0f, 0f, 55f), horn);
            Part(root, cone, "Horn R", new Vector3(0.42f, 0.12f, 0f), new Vector3(0.2f, 0.5f, 0.2f), new Vector3(0f, 0f, -55f), horn);
        });
        Make("ChefHat", root =>
        {
            var white = Mat("Chef White", "#F7F7F2", 0f, 0.2f);
            Part(root, PrimitiveType.Cylinder, "Band", new Vector3(0f, 0.17f, 0f), new Vector3(0.66f, 0.17f, 0.66f), Vector3.zero, white);
            foreach (var p in new[] { new Vector3(0f, 0.55f, 0f), new Vector3(0.17f, 0.5f, 0.1f), new Vector3(-0.17f, 0.5f, 0.1f), new Vector3(0f, 0.5f, -0.18f) })
                Part(root, PrimitiveType.Sphere, "Puff", p, Vector3.one * 0.48f, Vector3.zero, white);
        });

        // ── Back ──
        Make("Backpack", root =>
        {
            Part(root, PrimitiveType.Cube, "Pack", new Vector3(0f, 0.60f, 0.10f), new Vector3(0.7f, 0.8f, 0.36f), new Vector3(8f, 0f, 0f), Mat("Pack Red", "#E2423B"));
            Part(root, PrimitiveType.Cube, "Pocket", new Vector3(0f, 0.41f, 0.30f), new Vector3(0.5f, 0.28f, 0.1f), new Vector3(8f, 0f, 0f), Mat("Pack Dark", "#8C221D"));
            Part(root, PrimitiveType.Cube, "Flap", new Vector3(0f, 0.95f, 0.05f), new Vector3(0.72f, 0.12f, 0.4f), new Vector3(8f, 0f, 0f), Mat("Pack Dark", "#8C221D"));
        });
        Make("Jetpack", root =>
        {
            var steel = Mat("Jet Steel", "#B8BCC8", 0.9f, 0.7f);
            var flame = Mat("Jet Flame", "#FF8A3D", 0f, 0.3f, "#FF6A00");
            foreach (float x in new[] { -0.17f, 0.17f })
            {
                Part(root, PrimitiveType.Cylinder, "Tank", new Vector3(x, 0.60f, 0.10f), new Vector3(0.26f, 0.36f, 0.26f), Vector3.zero, steel);
                Part(root, PrimitiveType.Sphere, "Tank Top", new Vector3(x, 0.96f, 0.10f), Vector3.one * 0.26f, Vector3.zero, steel);
                Part(root, cone, "Nozzle", new Vector3(x, 0.24f, 0.10f), new Vector3(0.24f, 0.16f, 0.24f), new Vector3(180f, 0f, 0f), Mat("Jet Dark", "#3A3F4B", 0.6f, 0.4f));
                Part(root, cone, "Flame", new Vector3(x, 0.08f, 0.10f), new Vector3(0.15f, 0.24f, 0.15f), new Vector3(180f, 0f, 0f), flame);
            }
            Part(root, PrimitiveType.Cube, "Frame", new Vector3(0f, 0.60f, -0.04f), new Vector3(0.5f, 0.55f, 0.06f), Vector3.zero, Mat("Jet Dark", "#3A3F4B", 0.6f, 0.4f));
        });
        AssetDatabase.SaveAssets();
    }

    private static void Make(string id, System.Action<Transform> build)
    {
        var root = new GameObject(id).transform;
        build(root);
        foreach (var c in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        PrefabUtility.SaveAsPrefabAsset(root.gameObject, $"{Out}/{id}.prefab");
        Object.DestroyImmediate(root.gameObject);
    }

    private static GameObject Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Vector3 euler, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        return Place(go, parent, name, pos, scale, euler, mat);
    }

    private static GameObject Part(Transform parent, Mesh mesh, string name, Vector3 pos, Vector3 scale, Vector3 euler, Material mat)
    {
        var go = new GameObject(name);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>();
        return Place(go, parent, name, pos, scale, euler, mat);
    }

    private static GameObject Place(GameObject go, Transform parent, string name, Vector3 pos, Vector3 scale, Vector3 euler, Material mat)
    {
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    private static Material Mat(string name, string hex, float metallic = 0f, float smoothness = 0.45f, string emission = null)
    {
        string path = $"{MatFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        ColorUtility.TryParseHtmlString(hex, out Color c);
        mat.SetColor("_BaseColor", c);
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Smoothness", smoothness);
        if (emission != null && ColorUtility.TryParseHtmlString(emission, out Color e))
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", e * 1.5f);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // A cone: base radius 0.5 at y = 0, tip at y = 1 (so its scale is width × height)
    private static Mesh ConeMesh()
    {
        string path = $"{MatFolder}/Cone.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh != null) return mesh;
        const int sides = 24;
        var verts = new System.Collections.Generic.List<Vector3>();
        var normals = new System.Collections.Generic.List<Vector3>();
        var tris = new System.Collections.Generic.List<int>();
        for (int i = 0; i < sides; i++)
        {
            float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
            Vector3 p0 = new(Mathf.Cos(a0) * 0.5f, 0f, Mathf.Sin(a0) * 0.5f), p1 = new(Mathf.Cos(a1) * 0.5f, 0f, Mathf.Sin(a1) * 0.5f);
            Vector3 tip = Vector3.up;
            Vector3 n = Vector3.Cross(p1 - p0, tip - p0).normalized;
            int b = verts.Count;
            verts.AddRange(new[] { p0, tip, p1 });
            normals.AddRange(new[] { -n, -n, -n });
            tris.AddRange(new[] { b, b + 1, b + 2 });
            b = verts.Count; // Base
            verts.AddRange(new[] { Vector3.zero, p0, p1 });
            normals.AddRange(new[] { Vector3.down, Vector3.down, Vector3.down });
            tris.AddRange(new[] { b, b + 1, b + 2 });
        }
        mesh = new Mesh { name = "Cone" };
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }
}
