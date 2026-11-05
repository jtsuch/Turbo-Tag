using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// Makes the menu theme: TextMeshPro font assets (dynamic SDF) from the TTFs in Assets/UI/Fonts, and
/// Resources/UI/UITheme pointing at them plus the Turbo Tag logo. Re-running keeps existing assets
/// (delete one to rebuild it). Job "BuildUITheme", menu Turbo Tag/UI/Build UI Theme.
/// </summary>
public static class UIThemeBuilder
{
    private const string FontFolder = "Assets/UI/Fonts";
    private const string ThemePath  = "Assets/Resources/UI/UITheme.asset";
    private const string LogoPath   = "Assets/Models/2D-Icons/TurboTagTitleNoBG.png";

    [MenuItem("Turbo Tag/UI/Build UI Theme")]
    public static void Build()
    {
        var display = FontAssetFor("LilitaOne-Regular");
        var body    = FontAssetFor("Poppins-Medium");
        var bold    = FontAssetFor("Poppins-Bold");

        // Missing glyphs (‹ › × −) fall back to Poppins, then TMP's default font
        var fallback = TMP_Settings.defaultFontAsset;
        foreach (var f in new[] { display, body, bold })
        {
            if (f == null) continue;
            f.fallbackFontAssetTable ??= new System.Collections.Generic.List<TMP_FontAsset>();
            if (f != body && body != null && !f.fallbackFontAssetTable.Contains(body)) f.fallbackFontAssetTable.Add(body);
            if (fallback != null && !f.fallbackFontAssetTable.Contains(fallback)) f.fallbackFontAssetTable.Add(fallback);
            EditorUtility.SetDirty(f);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(ThemePath));
        var theme = AssetDatabase.LoadAssetAtPath<UITheme>(ThemePath);
        if (theme == null)
        {
            theme = ScriptableObject.CreateInstance<UITheme>();
            AssetDatabase.CreateAsset(theme, ThemePath);
        }
        theme.display  = display;
        theme.body     = body;
        theme.bodyBold = bold;
        theme.logo     = AssetDatabase.LoadAssetAtPath<Texture2D>(LogoPath);
        EditorUtility.SetDirty(theme);
        AssetDatabase.SaveAssets();
        Debug.Log($"[UIThemeBuilder] Theme ready: display {Name(display)}, body {Name(body)}, bold {Name(bold)}, logo {(theme.logo != null)}");
    }

    private static string Name(Object o) => o != null ? o.name : "MISSING";

    private static TMP_FontAsset FontAssetFor(string fileName)
    {
        string assetPath = $"{FontFolder}/{fileName} SDF.asset";
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
        if (existing != null) return existing;

        var font = AssetDatabase.LoadAssetAtPath<Font>($"{FontFolder}/{fileName}.ttf");
        if (font == null) { Debug.LogError($"[UIThemeBuilder] Missing {fileName}.ttf"); return null; }

        var asset = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
        asset.name = fileName + " SDF";
        AssetDatabase.CreateAsset(asset, assetPath);

        // The atlas texture and material live inside the font asset
        asset.atlasTextures[0].name = fileName + " Atlas";
        AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);
        asset.material.name = fileName + " Material";
        AssetDatabase.AddObjectToAsset(asset.material, asset);
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        return asset;
    }
}
