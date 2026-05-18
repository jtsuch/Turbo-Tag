using UnityEngine;
using UnityEditor;

/// <summary>
/// Adds all missing concrete ability scripts to ThePlayer prefab with correct field assignments.
/// ThrowAbility refs (throwOrigin, trajectoryLine) are copied from the existing BoomBomb component.
/// TrapAbility refs (cameraHolder, hologramMaterial, placementLayers) are copied from the existing Box component.
/// Run via: Tools → Turbo Tag → Add Missing Ability Scripts
/// Safe to run multiple times — skips any script already present.
/// </summary>
public static class AddMissingAbilities
{
    private const string PREFAB_PATH = "Assets/Resources/Player/ThePlayer.prefab";

    [MenuItem("Tools/Turbo Tag/Add Missing Ability Scripts")]
    static void Run()
    {
        GameObject prefab = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
        if (prefab == null)
        {
            Debug.LogError("[AddAbilities] Could not load ThePlayer prefab at " + PREFAB_PATH);
            return;
        }

        try
        {
            // ── Find shared child references by path ──────────────────────────
            Transform throwTrajectoryTf = prefab.transform.Find("CameraHolder/ThrowTrajectory");
            Transform throwPointTf      = prefab.transform.Find("CameraHolder/Camera/ThrowPoint");
            Transform cameraHolderTf    = prefab.transform.Find("CameraHolder");

            LineRenderer trajLine = throwTrajectoryTf != null
                ? throwTrajectoryTf.GetComponent<LineRenderer>()
                : null;

            if (cameraHolderTf  == null) Debug.LogWarning("[AddAbilities] CameraHolder not found — TrapAbility cameraHolder will be unassigned.");
            if (throwPointTf    == null) Debug.LogWarning("[AddAbilities] ThrowPoint not found — ThrowAbility throwOrigin will be unassigned.");
            if (trajLine        == null) Debug.LogWarning("[AddAbilities] ThrowTrajectory LineRenderer not found — trajectoryLine will be unassigned.");

            // ── Copy ThrowAbility refs from the existing BoomBomb component ───
            // Fall back to BoomStick if BoomBomb isn't present.
            Transform  existingThrowOrigin = null;
            LineRenderer existingTrajLine  = null;
            var sourceBomb = prefab.GetComponent<BoomBomb>() as ThrowAbility
                          ?? prefab.GetComponent<BoomStick>() as ThrowAbility;
            if (sourceBomb != null)
            {
                var bombSO = new SerializedObject(sourceBomb);
                existingThrowOrigin = bombSO.FindProperty("throwOrigin").objectReferenceValue as Transform;
                existingTrajLine    = bombSO.FindProperty("trajectoryLine").objectReferenceValue as LineRenderer;
            }

            // Prefer existing serialized values; fall back to path-found references
            Transform   throwOrigin  = existingThrowOrigin != null ? existingThrowOrigin : throwPointTf;
            LineRenderer trajectoryLine = existingTrajLine != null ? existingTrajLine : trajLine;

            // ── Copy TrapAbility refs from the existing Box component ─────────
            Material hologramMat       = null;
            int      placementLayerMask = 0;
            var existingBox = prefab.GetComponent<Box>();
            if (existingBox != null)
            {
                var boxSO = new SerializedObject(existingBox);
                hologramMat        = boxSO.FindProperty("hologramMaterial").objectReferenceValue as Material;
                placementLayerMask = boxSO.FindProperty("placementLayers").intValue;
            }
            else Debug.LogWarning("[AddAbilities] Box component not found — TrapAbility hologram/layers will be unassigned.");

            // ── QuickAbilities (numeric fields only, defaults are fine) ───────
            SetAbilityName(AddIfMissing<Launch>(prefab));
            SetAbilityName(AddIfMissing<Shrink>(prefab));

            // ── ThrowAbilities ────────────────────────────────────────────────
            SetThrowRefs<Rock>     (prefab, throwOrigin, trajectoryLine);
            SetThrowRefs<Flashbang>(prefab, throwOrigin, trajectoryLine);
            SetThrowRefs<GravBall> (prefab, throwOrigin, trajectoryLine);
            SetThrowRefs<Semtex>   (prefab, throwOrigin, trajectoryLine);
            SetThrowRefs<Snowball> (prefab, throwOrigin, trajectoryLine);
            SetThrowRefs<Frisbee>  (prefab, throwOrigin, trajectoryLine);

            // ── TrapAbilities ─────────────────────────────────────────────────
            SetTrapRefs<IceTrap>    (prefab, cameraHolderTf?.gameObject, hologramMat, placementLayerMask);
            SetTrapRefs<GravityWell>(prefab, cameraHolderTf?.gameObject, hologramMat, placementLayerMask);

            PrefabUtility.SaveAsPrefabAsset(prefab, PREFAB_PATH);
            Debug.Log("[AddAbilities] Done — ThePlayer prefab updated successfully.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static T AddIfMissing<T>(GameObject go) where T : Component
    {
        var existing = go.GetComponent<T>();
        if (existing != null)
        {
            Debug.Log($"[AddAbilities] {typeof(T).Name} already present, skipped.");
            return existing;
        }
        var added = go.AddComponent<T>();
        Debug.Log($"[AddAbilities] Added {typeof(T).Name}.");
        return added;
    }

    static void SetAbilityName(Component comp)
    {
        var so = new SerializedObject(comp);
        var prop = so.FindProperty("abilityName");
        if (prop != null && prop.stringValue == "New Ability")
        {
            prop.stringValue = comp.GetType().Name;
            so.ApplyModifiedProperties();
        }
    }

    static void SetThrowRefs<T>(GameObject go, Transform throwOrigin, LineRenderer trajLine)
        where T : ThrowAbility
    {
        var comp = AddIfMissing<T>(go);
        var so   = new SerializedObject(comp);
        SetAbilityName(comp);
        if (throwOrigin != null) so.FindProperty("throwOrigin").objectReferenceValue    = throwOrigin;
        if (trajLine    != null) so.FindProperty("trajectoryLine").objectReferenceValue = trajLine;
        so.ApplyModifiedProperties();
    }

    static void SetTrapRefs<T>(GameObject go, GameObject camHolder, Material hologramMat, int layerMask)
        where T : TrapAbility
    {
        var comp = AddIfMissing<T>(go);
        var so   = new SerializedObject(comp);
        SetAbilityName(comp);
        if (camHolder   != null) so.FindProperty("cameraHolder").objectReferenceValue    = camHolder;
        if (hologramMat != null) so.FindProperty("hologramMaterial").objectReferenceValue = hologramMat;
        so.FindProperty("placementLayers").intValue = layerMask;
        so.ApplyModifiedProperties();
    }
}
