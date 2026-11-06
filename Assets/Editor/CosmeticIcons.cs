using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Renders a picture of every cosmetic for the Wardrobe (Resources/Cosmetics/Icons/{Id}.png, transparent, imported
/// as a sprite): hats from a little above and to the side, back items from behind (their design faces +Z).
/// Run by the BuildCosmetics job after the prefabs are made.
/// </summary>
public static class CosmeticIcons
{
    private const string Folder = "Assets/Resources/Cosmetics/Icons";
    private const int Size = 256;

    public static void Build()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Resources/Cosmetics", "Icons");

        var lightGo = new GameObject("Key");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.4f;
        lightGo.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
        var fillGo = new GameObject("Fill");
        var fill = fillGo.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = 0.6f;
        fillGo.transform.rotation = Quaternion.Euler(20f, 150f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);

        foreach (var item in Cosmetics.All)
        {
            var prefab = Cosmetics.Prefab(item.Id);
            if (prefab == null) continue;
            var go = Object.Instantiate(prefab);
            go.transform.rotation = Quaternion.Euler(0f, -25f, 0f);
            foreach (var spin in go.GetComponentsInChildren<CosmeticSpin>()) Object.DestroyImmediate(spin);
            var rs = go.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);

            var camGo = new GameObject("Icon Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.fieldOfView = 22f;
            float radius = b.extents.magnitude;
            float distance = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.82f;
            Vector3 view = new Vector3(0f, item.Slot == Cosmetics.Slot.Hat ? 0.35f : 0.1f, 1f).normalized;
            camGo.transform.position = b.center + view * distance;
            camGo.transform.LookAt(b.center);

            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            string path = $"{Folder}/{item.Id}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(go);

            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer != null && (importer.textureType != TextureImporterType.Sprite || !importer.alphaIsTransparency
                                     || importer.spriteImportMode != SpriteImportMode.Single))
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single; // Loadable by name
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
        }
    }
}
