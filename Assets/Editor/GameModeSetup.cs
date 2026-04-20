#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor utility that creates the default GameModeDefinition ScriptableObject assets under
/// Assets/Resources/GameModes/. Run via the Unity menu: TurboTag → Create Game Mode Definitions.
/// Safe to re-run — existing assets are overwritten.
/// </summary>
public static class GameModeSetup
{
    private const string Folder = "Assets/Resources/GameModes";

    [MenuItem("TurboTag/Create Game Mode Definitions")]
    public static void CreateGameModeDefinitions()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets/Resources", "GameModes");

        CreateTag();
        CreateFreeplay();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[GameModeSetup] Assets created in {Folder}");
    }

    // ─── Tag ──────────────────────────────────────────────────────────────────

    private static void CreateTag()
    {
        var d = ScriptableObject.CreateInstance<GameModeDefinition>();
        d.gameModeName      = "Tag";
        d.gameModeKey       = "Tag";
        d.cheatsAutoEnabled = false;
        d.settings = new RuleSetting[]
        {
            // ── Time ──────────────────────────────────────────────────────────
            S("Hide Time",                  "Rule_HideTime",        F.Slider,     30,  300,  120, "Time"),
            S("Seek Time",                  "Rule_SeekTime",        F.Slider,     30,  300,  120, "Time"),
            S("Max Seek Time Per Round",    "Rule_MaxSeekTime",     F.Slider,     30,  600,  180, "Time"),
            S("Seeker Start Delay",         "Rule_SeekerDelay",     F.Slider,      0,   30,    5, "Time"),

            // ── Hunters ───────────────────────────────────────────────────────
            S("Number of Starting Hunters", "Rule_HunterCount",     F.Slider,      1,    8,    1, "Hunters"),
            S("Hunter Cooldown Reduction",  "Rule_HunterCooldown",  F.Slider,      0,  100,    0, "Hunters"),
            S("Tag Cooldown",               "Rule_TagCooldown",     F.Slider,      0,   10,    2, "Hunters"),

            // ── World ─────────────────────────────────────────────────────────
            S("Gravity",                    "Rule_Gravity",         F.Slider,     10,  200,  100, "World"),
            S("Effects Respawn Rate",       "Rule_EffectsRespawn",  F.Slider,      0,  120,   30, "World"),
            S("Global Cooldown Multiplier", "Rule_GlobalCooldown",  F.Slider,     10,  300,  100, "World"),

            // ── Scoring ───────────────────────────────────────────────────────
            S("Players Hunted Bonus",       "Rule_HuntedBonus",     F.InputField,  0,    0,   10, "Scoring"),
            S("Score Limit",                "Rule_ScoreLimit",      F.Slider,      0,  200,    0, "Scoring"),
            S("Teams",                      "Rule_Teams",           F.Toggle,      0,    0,    0, "Scoring"),
            S("Round Count",                "Rule_RoundCount",      F.Slider,      1,   20,    3, "Scoring"),

            // ── Allow Kills ───────────────────────────────────────────────────
            S("Allow Kills",                "Rule_AllowKills",      F.Toggle,      0,    0,    0, "Allow Kills"),
            S("Player Health",              "Rule_PlayerHealth",    F.Slider,      1,  500,  100, "Allow Kills"),
            S("Fall Damage",                "Rule_FallDamage",      F.Toggle,      0,    0,    0, "Allow Kills"),
        };
        Save(d, "Tag");
    }

    // ─── Freeplay ─────────────────────────────────────────────────────────────

    private static void CreateFreeplay()
    {
        var d = ScriptableObject.CreateInstance<GameModeDefinition>();
        d.gameModeName      = "Freeplay";
        d.gameModeKey       = "Freeplay";
        d.cheatsAutoEnabled = true;
        d.settings          = new RuleSetting[0];
        Save(d, "Freeplay");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static void Save(GameModeDefinition asset, string name)
    {
        string path = $"{Folder}/{name}.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(asset, path);
    }

    // Short alias so the settings array above stays readable
    private static RuleSetting S(string display, string key, F type,
                                  float min, float max, float def, string cat) =>
        new RuleSetting
        {
            displayName      = display,
            roomPropertyKey  = key,
            fieldType        = (RuleSetting.FieldType)type,
            minValue         = min,
            maxValue         = max,
            defaultValue     = def,
            category         = cat,
        };

    // Local alias to avoid typing RuleSetting.FieldType everywhere
    private enum F { Slider, Toggle, InputField, Dropdown }
}
#endif
