using UnityEditor;
using UnityEngine;

/// <summary>
/// Renames throwable prefabs so their filenames match the abilityName used by ThrowAbility
/// to construct the Resources/Object/ load path.
/// Run via: Tools → Turbo Tag → Rename Mismatched Prefabs
/// Safe to run multiple times — skips any rename where the source no longer exists.
/// </summary>
public static class RenamePrefabs
{
    [MenuItem("Tools/Turbo Tag/Rename Mismatched Prefabs")]
    static void Run()
    {
        Rename("Assets/Resources/Object/BoomBrick.prefab",       "BoomBomb");
        Rename("Assets/Resources/Object/RockProjectile.prefab",  "Rock");
        Rename("Assets/Resources/Object/FrisbeeProjectile.prefab", "Frisbee");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[RenamePrefabs] Done.");
    }

    static void Rename(string path, string newName)
    {
        if (!System.IO.File.Exists(path))
        {
            // Already renamed or doesn't exist
            Debug.Log($"[RenamePrefabs] {path} not found — skipping (may already be renamed).");
            return;
        }
        string err = AssetDatabase.RenameAsset(path, newName);
        if (string.IsNullOrEmpty(err))
            Debug.Log($"[RenamePrefabs] Renamed to {newName}.prefab");
        else
            Debug.LogError($"[RenamePrefabs] Failed to rename {path}: {err}");
    }
}
