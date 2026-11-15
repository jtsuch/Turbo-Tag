using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Records the ability clips (see AbilityClipRecorder): job RecordAbilityClips (or menu Turbo Tag/Abilities/Record
/// Ability Clips) opens Pinnacle and plays it offline; the recorder films every ability and leaves play mode, then
/// the clip sheets get their import settings (no resizing to a power of two, no mipmaps). To redo a few, set
/// SessionState "TT_RecordClips" to "Id1,Id2" instead of "all".
/// </summary>
[InitializeOnLoad]
public static class AbilityClipTools
{
    private const string PlayKey  = "TT_RecordClipsScene";
    private const string Scene    = "Assets/Scenes/Levels/Pinnacle.unity";

    static AbilityClipTools()
    {
        EditorApplication.update += LaunchWhenIdle;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool("TT_ClipsRecorded", false))
            {
                SessionState.EraseBool("TT_ClipsRecorded");
                FixImports();
            }
        };
    }

    [MenuItem("Turbo Tag/Abilities/Record Ability Clips")]
    public static void Record() => Record("all");

    public static void Record(string which)
    {
        SessionState.SetString(AbilityClipRecorder.SessionKey, which);
        SessionState.SetString(PlayKey, Scene);
    }

    // After the job queue has finished (and restored the scenes): open the plaza level and press Play
    private static void LaunchWhenIdle()
    {
        string scene = SessionState.GetString(PlayKey, "");
        if (string.IsNullOrEmpty(scene) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        if (File.Exists("Library/TurboTag/PendingEditorJobs.txt")) return;
        SessionState.EraseString(PlayKey);
        if (EditorSceneManager.GetActiveScene().isDirty) EditorSceneManager.SaveOpenScenes();
        EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
        SessionState.SetBool("TT_ClipsRecorded", true);
        EditorApplication.isPlaying = true;
    }

    [MenuItem("Turbo Tag/Abilities/Fix Clip Imports")]
    public static void FixImports()
    {
        AssetDatabase.Refresh();
        if (!Directory.Exists(AbilityClipRecorder.OutFolder)) return;
        foreach (string file in Directory.GetFiles(AbilityClipRecorder.OutFolder, "*.jpg"))
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(file.Replace('\\', '/'));
            if (importer == null) continue;
            if (importer.npotScale == TextureImporterNPOTScale.None && !importer.mipmapEnabled && importer.maxTextureSize >= 2048) continue;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }
        Debug.Log("[AbilityClipTools] Clip imports fixed.");
    }
}
