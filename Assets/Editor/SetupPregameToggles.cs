using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using TMPro;

/// <summary>
/// Creates and wires all missing ability toggles in the Pregame scene.
/// Each new toggle is duplicated from the existing template in its group
/// (dash → quick, boomBomb → throw, box → trap), then renamed and re-wired.
/// Run via: Tools → Turbo Tag → Setup Pregame Toggles
/// Safe to run multiple times — skips any toggle whose AbilityToggle.abilityName already exists.
/// </summary>
public static class SetupPregameToggles
{
    private const string SCENE_PATH = "Assets/Scenes/Pregame.unity";

    [MenuItem("Tools/Turbo Tag/Setup Pregame Toggles")]
    static void Run()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(SCENE_PATH);
        bool wasAlreadyOpen = scene.isLoaded;
        if (!wasAlreadyOpen)
            scene = EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Additive);

        var pgManager = Object.FindObjectOfType<PregameManager>();
        if (pgManager == null)
        {
            Debug.LogError("[SetupToggles] PregameManager not found in the Pregame scene.");
            if (!wasAlreadyOpen) EditorSceneManager.CloseScene(scene, false);
            return;
        }

        var pgSO = new SerializedObject(pgManager);

        // ── Quick abilities ───────────────────────────────────────────────────
        if (pgManager.dashToggle == null)
            Debug.LogWarning("[SetupToggles] dashToggle is unassigned — Quick toggles skipped.");
        else
        {
            Make(pgManager.dashToggle, pgManager, pgManager.quickToggleGroup,
                "Launch",  pgSO, "launchToggle",
                t => UnityEventTools.AddObjectPersistentListener<Toggle>(t.onValueChanged, pgManager.OnQuickToggleChanged, t));
            Make(pgManager.dashToggle, pgManager, pgManager.quickToggleGroup,
                "Shrink",  pgSO, "shrinkToggle",
                t => UnityEventTools.AddObjectPersistentListener<Toggle>(t.onValueChanged, pgManager.OnQuickToggleChanged, t));
        }

        // ── Throw abilities ───────────────────────────────────────────────────
        if (pgManager.boomBombToggle == null)
            Debug.LogWarning("[SetupToggles] boomBombToggle is unassigned — Throw toggles skipped.");
        else
        {
            Make(pgManager.boomBombToggle, pgManager, pgManager.throwToggleGroup,
                "BoomStick", pgSO, "boomStickToggle",
                t => UnityEventTools.AddObjectPersistentListener<Toggle>(t.onValueChanged, pgManager.OnThrowToggleChanged, t));
            Make(pgManager.boomBombToggle, pgManager, pgManager.throwToggleGroup,
                "Rock",      pgSO, "rockToggle",
                t => UnityEventTools.AddObjectPersistentListener<Toggle>(t.onValueChanged, pgManager.OnThrowToggleChanged, t));
            Make(pgManager.boomBombToggle, pgManager, pgManager.throwToggleGroup,
                "Flashbang", pgSO, "flashbangToggle",
                t => UnityEventTools.AddObjectPersistentListener<Toggle>(t.onValueChanged, pgManager.OnThrowToggleChanged, t));
            Make(pgManager.boomBombToggle, pgManager, pgManager.throwToggleGroup,
                "GravBall",  pgSO, "gravBallToggle",
                t => UnityEventTools.AddObjectPersistentListener<Toggle>(t.onValueChanged, pgManager.OnThrowToggleChanged, t));
            Make(pgManager.boomBombToggle, pgManager, pgManager.throwToggleGroup,
                "Semtex",    pgSO, "semtexToggle",
                t => UnityEventTools.AddObjectPersistentListener<Toggle>(t.onValueChanged, pgManager.OnThrowToggleChanged, t));
            Make(pgManager.boomBombToggle, pgManager, pgManager.throwToggleGroup,
                "Snowball",  pgSO, "snowballToggle",
                t => UnityEventTools.AddObjectPersistentListener<Toggle>(t.onValueChanged, pgManager.OnThrowToggleChanged, t));
            Make(pgManager.boomBombToggle, pgManager, pgManager.throwToggleGroup,
                "Frisbee",   pgSO, "frisbeeToggle",
                t => UnityEventTools.AddObjectPersistentListener<Toggle>(t.onValueChanged, pgManager.OnThrowToggleChanged, t));
        }

        // ── Trap abilities ────────────────────────────────────────────────────
        if (pgManager.boxToggle == null)
            Debug.LogWarning("[SetupToggles] boxToggle is unassigned — Trap toggles skipped.");
        else
        {
            Make(pgManager.boxToggle, pgManager, pgManager.trapToggleGroup,
                "Ladder",      pgSO, "ladderToggle",
                t => UnityEventTools.AddObjectPersistentListener<Toggle>(t.onValueChanged, pgManager.OnTrapToggleChanged, t));
            Make(pgManager.boxToggle, pgManager, pgManager.trapToggleGroup,
                "IceTrap",     pgSO, "iceTrapToggle",
                t => UnityEventTools.AddObjectPersistentListener<Toggle>(t.onValueChanged, pgManager.OnTrapToggleChanged, t));
            Make(pgManager.boxToggle, pgManager, pgManager.trapToggleGroup,
                "GravityWell", pgSO, "gravityWellToggle",
                t => UnityEventTools.AddObjectPersistentListener<Toggle>(t.onValueChanged, pgManager.OnTrapToggleChanged, t));
            Make(pgManager.boxToggle, pgManager, pgManager.trapToggleGroup,
                "Nuke",        pgSO, "nukeToggle",
                t => UnityEventTools.AddObjectPersistentListener<Toggle>(t.onValueChanged, pgManager.OnTrapToggleChanged, t));
        }

        pgSO.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[SetupToggles] Done — Pregame scene saved.");
    }

    static void Make(Toggle template, PregameManager pgManager, ToggleGroup group,
        string abilityName, SerializedObject pgSO, string fieldName,
        System.Action<Toggle> wireCallback)
    {
        Transform parent = template.transform.parent;

        // Idempotent — skip if already present
        foreach (Transform child in parent)
        {
            var at = child.GetComponent<AbilityToggle>();
            if (at != null && at.abilityName == abilityName)
            {
                Debug.Log($"[SetupToggles] {abilityName} toggle already exists, skipped.");
                return;
            }
        }

        // Duplicate template
        GameObject newGO = Object.Instantiate(template.gameObject, parent);
        newGO.name = abilityName + "Toggle";
        Undo.RegisterCreatedObjectUndo(newGO, "Add " + abilityName + " Toggle");

        // Rename the visual toggle child to match this ability (e.g. "DashToggle" → "LaunchToggle")
        foreach (Transform child in newGO.transform)
        {
            if (child.name.EndsWith("Toggle"))
            {
                child.name = abilityName + "Toggle";
                break;
            }
        }

        // Label text — find AbilityNameText by name first, fall back to first TMP_Text
        Transform abilityNameTf = newGO.transform.Find("AbilityNameText");
        var label = abilityNameTf != null
            ? abilityNameTf.GetComponent<TMP_Text>()
            : newGO.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = InsertSpaces(abilityName);

        // AbilityToggle identity
        var abilityToggle = newGO.GetComponent<AbilityToggle>();
        if (abilityToggle == null) abilityToggle = newGO.AddComponent<AbilityToggle>();
        abilityToggle.abilityName = abilityName;

        // Toggle settings
        var toggle = newGO.GetComponent<Toggle>();
        toggle.group = group;
        toggle.isOn  = false;

        // Clear the listeners copied from the template, then wire this toggle's own callback
        var toggleSO = new SerializedObject(toggle);
        toggleSO.FindProperty("m_OnValueChanged.m_PersistentCalls.m_Calls").ClearArray();
        toggleSO.ApplyModifiedProperties();
        wireCallback(toggle);

        // Assign to PregameManager's serialized field
        var prop = pgSO.FindProperty(fieldName);
        if (prop != null)
            prop.objectReferenceValue = toggle;
        else
            Debug.LogWarning($"[SetupToggles] Field '{fieldName}' not found on PregameManager — assign {abilityName}Toggle manually in the Inspector.");

        Debug.Log($"[SetupToggles] Created {abilityName} toggle.");
    }

    static string InsertSpaces(string s)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (i > 0 && char.IsUpper(s[i]) && !char.IsUpper(s[i - 1]))
                sb.Append(' ');
            sb.Append(s[i]);
        }
        return sb.ToString();
    }
}
