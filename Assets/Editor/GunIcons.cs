using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Side-on silhouettes of every gun for the shop (Resources/Guns/Icons/{Id}.png: white on transparent, 512 × 256,
/// muzzle to the right, imported as sprites; the shop tints them). Rendered from the gun prefabs (Resources/Guns), and
/// for the Pebble, which uses the player's built-in pistol, from Assets/Models/Pistol.fbx laid out the same way.
/// Run by BuildGunPrefabs after the prefabs; job "BuildGunIcons", menu Turbo Tag/Build Gun Icons.
/// </summary>
public static class GunIcons
{
    private const string Folder = "Assets/Resources/Guns/Icons";
    private const int Width = 512, Height = 256;
    private const int Layer = 31;
    private static readonly Vector3 Stage = new(0f, -10000f, 0f);   // Far from any level

    [MenuItem("Turbo Tag/Build Gun Icons")]
    public static void Build()
    {
        Directory.CreateDirectory(Folder);
        var white = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color")) { color = Color.white };
        var log = new System.Text.StringBuilder();
        foreach (var gun in GunCatalog.All)
        {
            GameObject go = null;
            if (gun.Model != null)
            {
                var prefab = Resources.Load<GameObject>(gun.Model);
                if (prefab != null) go = Object.Instantiate(prefab);
            }
            else if (gun.Id == GunCatalog.DefaultId) go = GunPrefabBuilder.Align("Assets/Models/Pistol.fbx", gun.Id, out _);
            if (go == null) { log.AppendLine($"! {gun.Id}: no model"); continue; }
            log.AppendLine(Render(go, gun.Id, white));
            Object.DestroyImmediate(go);
        }
        Object.DestroyImmediate(white);
        AssetDatabase.Refresh();
        Debug.Log("[GunIcons]\n" + log);
    }

    // Crops a render to its silhouette and fits it (aspect kept, 6% padding, centred) into a Width × Height icon,
    // white with the coverage as alpha
    private static Texture2D Fit(Texture2D src)
    {
        var px = src.GetPixels32();
        int minX = src.width, minY = src.height, maxX = -1, maxY = -1;
        for (int y = 0; y < src.height; y++)
            for (int x = 0; x < src.width; x++)
                if (px[y * src.width + x].a > 8)
                {
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
        var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        var outPx = new Color32[Width * Height];
        if (maxX >= 0)
        {
            float cw = maxX - minX + 1, ch = maxY - minY + 1;
            float scale = Mathf.Min(Width * 0.88f / cw, Height * 0.88f / ch);
            float ox = (Width - cw * scale) / 2f, oy = (Height - ch * scale) / 2f;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    float sx = minX + (x + 0.5f - ox) / scale, sy = minY + (y + 0.5f - oy) / scale;
                    float a = 0f;
                    if (sx >= minX - 1 && sx <= maxX + 1 && sy >= minY - 1 && sy <= maxY + 1)
                        a = src.GetPixelBilinear(sx / src.width, sy / src.height).a;
                    outPx[y * Width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
        }
        tex.SetPixels32(outPx);
        tex.Apply();
        return tex;
    }

    private static string Render(GameObject go, string id, Material white)
    {
        go.transform.SetPositionAndRotation(Stage, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return $"! {id}: no renderers";
        Bounds b = default;
        bool any = false;
        foreach (var r in renderers)
        {
            r.gameObject.layer = Layer;
            if (!r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer) continue;
            var mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = white;
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
        }

        // Orthographic, from the gun's right side (+X) looking back: the muzzle (+Z) points right
        var camGo = new GameObject("Gun Icon Camera");
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(1f, 1f, 1f, 0f);
        cam.cullingMask = 1 << Layer;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 50f;
        cam.aspect = (float)Width / Height;
        cam.orthographicSize = Mathf.Max(b.extents.y, b.extents.z / cam.aspect) * 1.06f;
        camGo.transform.SetPositionAndRotation(b.center + Vector3.right * 10f, Quaternion.LookRotation(Vector3.left, Vector3.up));

        // Rendered big with room to spare, then cropped to the silhouette and fitted into the icon (model bounds
        // can be loose: the AR came out tiny and the Flurry clipped)
        cam.orthographicSize *= 1.3f;
        int bw = Width * 2, bh = Height * 2;
        var rt = new RenderTexture(bw, bh, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var big = new Texture2D(bw, bh, TextureFormat.RGBA32, false);
        big.ReadPixels(new Rect(0, 0, bw, bh), 0, 0);
        big.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;
        var tex = Fit(big);
        Object.DestroyImmediate(big);

        string path = $"{Folder}/{id}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(camGo);

        AssetDatabase.ImportAsset(path);
        if (AssetImporter.GetAtPath(path) is TextureImporter importer
            && (importer.textureType != TextureImporterType.Sprite || !importer.alphaIsTransparency))
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
        return $"{id}: {path}";
    }
}
