using UnityEditor;

/// <summary>
/// Import settings for cosmetics (Docs/CosmeticsPipeline.md), applied automatically: outfit FBX files in
/// Resources/Cosmetics/Meshes import with no animations and no avatar, rig type Generic (not None: with None Unity
/// drops the skin weights and the outfit comes in as a static mesh that can't follow Jimmy's bones); pictures in
/// Resources/Cosmetics/Icons import as single sprites with transparency.
/// </summary>
public class CosmeticImportRules : AssetPostprocessor
{
    private const string Meshes = "Assets/Resources/Cosmetics/Meshes/";
    private const string Icons  = "Assets/Resources/Cosmetics/Icons/";

    private void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(Meshes)) return;
        var importer = (ModelImporter)assetImporter;
        importer.animationType = ModelImporterAnimationType.Generic; // Keeps the skinning
        importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
        importer.importAnimation = false;
    }

    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Icons)) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
    }
}
