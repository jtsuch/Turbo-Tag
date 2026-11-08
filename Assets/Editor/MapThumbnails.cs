using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Renders a lobby thumbnail for every level in Assets/Scenes/Levels (names starting with "_" skipped) into
/// Assets/Resources/MapThumbnails/{Map}.png: a steep three-quarter view framing the walkable geometry (structure layer,
/// minus death zones and big ground planes; near boundary walls hidden), lit by the level's own lights. Re-run after changing a map.
/// Menu: Turbo Tag/Levels/Build Map Thumbnails (editor job: BuildMapThumbnails).
/// </summary>
public static class MapThumbnails
{
    private const string LevelsFolder = "Assets/Scenes/Levels";
    private const string OutFolder    = "Assets/Resources/MapThumbnails";
    private const int    Width = 640, Height = 360, Structure = 6;

    [MenuItem("Turbo Tag/Levels/Build Map Thumbnails")]
    public static void Build()
    {
        Directory.CreateDirectory(OutFolder);
        foreach (string path in Directory.GetFiles(LevelsFolder, "*.unity", SearchOption.AllDirectories)) // Map categories are subfolders
        {
            string map = Path.GetFileNameWithoutExtension(path);
            if (map.StartsWith("_")) continue;
            var scene = EditorSceneManager.OpenScene(path.Replace('\\', '/'), OpenSceneMode.Single);
            if (!Frame(out Bounds bounds)) { Debug.LogWarning($"[MapThumbnails] {map}: nothing on the structure layer"); continue; }
            Render(bounds, $"{OutFolder}/{map}.png");
            Debug.Log($"[MapThumbnails] {map}: {bounds.size}");
        }
        AssetDatabase.Refresh();
        foreach (string png in Directory.GetFiles(OutFolder, "*.png"))
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(png.Replace('\\', '/'));
            if (importer == null || importer.mipmapEnabled == false) continue;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }
    }

    // The walkable geometry's bounds (renderers on the structure layer, ignoring death zones and huge planes)
    private static bool Frame(out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r.gameObject.layer != Structure || r.GetComponentInParent<DeathZone>() != null) continue;
            if (r.bounds.size.x > 1500f || r.bounds.size.z > 1500f) continue;
            if (r.bounds.size.y < 2f && r.bounds.size.x * r.bounds.size.z > 4000f) continue; // Big ground planes
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return any;
    }

    private static void Render(Bounds b, string file)
    {
        var go = new GameObject("Thumbnail Camera");
        var cam = go.AddComponent<Camera>();
        float radius = new Vector2(b.extents.x, b.extents.z).magnitude;
        Vector3 dir = new Vector3(-0.45f, 1.25f, -0.6f).normalized; // Steep, to see over boundary walls
        // Hide boundary walls on the camera's side (tall, thin, long, at the near edge) so enclosed maps show inside
        var hidden = new System.Collections.Generic.List<Renderer>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            var rb = r.bounds;
            bool thinX = rb.size.x < 4f && rb.size.z > b.size.z * 0.4f, thinZ = rb.size.z < 4f && rb.size.x > b.size.x * 0.4f;
            bool tall = rb.size.y > 5f;
            bool nearWest = rb.center.x < b.center.x - b.extents.x * 0.7f, nearSouth = rb.center.z < b.center.z - b.extents.z * 0.7f;
            if (tall && ((thinX && nearWest) || (thinZ && nearSouth)) && r.enabled) { r.enabled = false; hidden.Add(r); }
        }
        cam.fieldOfView = 40f;
        float distance = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.95f;
        go.transform.position = b.center + dir * distance;
        go.transform.LookAt(b.center);
        cam.farClipPlane = distance * 3f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.16f, 0.12f, 0.27f); // The menu's dark purple

        var rt = new RenderTexture(Width, Height, 24) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(go);
        foreach (var r in hidden) r.enabled = true;
    }
}
