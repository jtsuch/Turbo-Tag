using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps the cosmetic definitions and the registry in step with the items the project has
/// (Docs/CosmeticsPipeline.md): one <see cref="CosmeticDefinition"/> per item in Resources/Cosmetics/Definitions, all
/// listed in Resources/Cosmetics/CosmeticRegistry.asset in <see cref="Items"/> order. Definitions added by hand to
/// the registry are kept (after these). Run by the BuildCosmetics job.
/// <b>Add an item:</b> a line in <see cref="Items"/> (prefab path; for a skinned outfit the FBX in Meshes/ and its
/// icon in Icons/), then run BuildCosmetics.
/// </summary>
public static class CosmeticRegistryBuilder
{
    private const string Root     = "Assets/Resources/Cosmetics";
    private const string DefsPath = Root + "/Definitions";
    private const string Registry = Root + "/CosmeticRegistry.asset";

    // id, name, slot, kind, prefab (relative to Resources/Cosmetics), worn on the back (the Wardrobe turns Jimmy round)
    private static readonly (string id, string name, CosmeticSlot slot, CosmeticKind kind, string prefab, bool back)[] Items =
    {
        ("Tophat", "Top Hat", CosmeticSlot.Head, CosmeticKind.Rigid, "Tophat.prefab", false),
        ("Cowboy", "Cowboy Hat", CosmeticSlot.Head, CosmeticKind.Skinned, "Meshes/Cowboy.fbx", false),
        ("Wizard", "Wizard Hat", CosmeticSlot.Head, CosmeticKind.Skinned, "Meshes/Wizard.fbx", false),
        ("Pirate", "Pirate Hat", CosmeticSlot.Head, CosmeticKind.Skinned, "Meshes/Pirate.fbx", false),
        ("PartyHat", "Party Hat", CosmeticSlot.Head, CosmeticKind.Rigid, "PartyHat.prefab", false),
        ("PropellerCap", "Propeller Cap", CosmeticSlot.Head, CosmeticKind.Rigid, "PropellerCap.prefab", false),
        ("VikingHelmet", "Viking Helmet", CosmeticSlot.Head, CosmeticKind.Rigid, "VikingHelmet.prefab", false),
        ("ChefHat", "Chef Hat", CosmeticSlot.Head, CosmeticKind.Rigid, "ChefHat.prefab", false),
        ("Beanie", "Beanie", CosmeticSlot.Head, CosmeticKind.Skinned, "Meshes/Beanie.fbx", false),
        ("Headband", "Headband", CosmeticSlot.Head, CosmeticKind.Skinned, "Meshes/Headband.fbx", false),
        ("FlowerPot", "Flower Pot", CosmeticSlot.Head, CosmeticKind.Skinned, "Meshes/FlowerPot.fbx", false),
        ("TrafficCone", "Traffic Cone", CosmeticSlot.Head, CosmeticKind.Skinned, "Meshes/TrafficCone.fbx", false),
        ("BunnyEars", "Bunny Ears", CosmeticSlot.Head, CosmeticKind.Skinned, "Meshes/BunnyEars.fbx", false),
        ("CatEars", "Cat Ears", CosmeticSlot.Head, CosmeticKind.Skinned, "Meshes/CatEars.fbx", false),
        ("DevilHorns", "Devil Horns", CosmeticSlot.Head, CosmeticKind.Skinned, "Meshes/DevilHorns.fbx", false),
        ("Halo", "Halo", CosmeticSlot.Head, CosmeticKind.Skinned, "Meshes/Halo.fbx", false),
        ("Sunglasses", "Sunglasses", CosmeticSlot.Head, CosmeticKind.Skinned, "Meshes/Sunglasses.fbx", false),
        ("RoundGlasses", "Round Glasses", CosmeticSlot.Head, CosmeticKind.Skinned, "Meshes/RoundGlasses.fbx", false),
        ("Monocle", "Monocle", CosmeticSlot.Head, CosmeticKind.Skinned, "Meshes/Monocle.fbx", false),
        ("EyePatch", "Eye Patch", CosmeticSlot.Head, CosmeticKind.Skinned, "Meshes/EyePatch.fbx", false),
        ("SnorkelMask", "Snorkel Mask", CosmeticSlot.Head, CosmeticKind.Skinned, "Meshes/SnorkelMask.fbx", false),
        ("BowTie", "Bow Tie", CosmeticSlot.Body, CosmeticKind.Skinned, "Meshes/BowTie.fbx", false),
        ("Scarf", "Scarf", CosmeticSlot.Body, CosmeticKind.Skinned, "Meshes/Scarf.fbx", false),
        ("Bandana", "Bandana", CosmeticSlot.Body, CosmeticKind.Skinned, "Meshes/Bandana.fbx", false),
        ("FloatRing", "Float Ring", CosmeticSlot.Body, CosmeticKind.Skinned, "Meshes/FloatRing.fbx", false),
        ("AngelWings", "Angel Wings", CosmeticSlot.Body, CosmeticKind.Skinned, "Meshes/AngelWings.fbx", true),
        ("BatWings", "Bat Wings", CosmeticSlot.Body, CosmeticKind.Skinned, "Meshes/BatWings.fbx", true),
        ("TurtleShell", "Turtle Shell", CosmeticSlot.Body, CosmeticKind.Skinned, "Meshes/TurtleShell.fbx", true),
        ("Surfboard", "Surfboard", CosmeticSlot.Body, CosmeticKind.Skinned, "Meshes/Surfboard.fbx", true),
        ("Backpack", "Backpack", CosmeticSlot.Body, CosmeticKind.Rigid, "Backpack.prefab", true),
        ("Jetpack", "Jetpack", CosmeticSlot.Body, CosmeticKind.Rigid, "Jetpack.prefab", true),
        ("LicensePlate", "LAGGING Plate", CosmeticSlot.Body, CosmeticKind.Rigid, "LicensePlate.prefab", true),
        ("DinoOnesie", "Dino Onesie", CosmeticSlot.Outfit, CosmeticKind.Skinned, "Meshes/DinoOnesie.fbx", true),
        ("Shark", "Shark Suit", CosmeticSlot.Outfit, CosmeticKind.Skinned, "Meshes/Shark.fbx", true),
        ("Frog", "Frog Suit", CosmeticSlot.Outfit, CosmeticKind.Skinned, "Meshes/Frog.fbx", false),
        ("Bee", "Bee Suit", CosmeticSlot.Outfit, CosmeticKind.Skinned, "Meshes/Bee.fbx", true),
        ("Pumpkin", "Pumpkin Suit", CosmeticSlot.Outfit, CosmeticKind.Skinned, "Meshes/Pumpkin.fbx", false),
    };

    public static CosmeticRegistry Sync()
    {
        // Outfit meshes and hand-made pictures: re-import so CosmeticImportRules' settings apply
        if (AssetDatabase.IsValidFolder(Root + "/Meshes"))
            AssetDatabase.ImportAsset(Root + "/Meshes", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
        foreach (var (id, _, _, kind, _, _) in Items)
            if (kind == CosmeticKind.Skinned && File.Exists($"{Root}/Icons/{id}.png"))
                AssetDatabase.ImportAsset($"{Root}/Icons/{id}.png", ImportAssetOptions.ForceUpdate);

        if (!AssetDatabase.IsValidFolder(DefsPath)) AssetDatabase.CreateFolder(Root, "Definitions");
        var registry = AssetDatabase.LoadAssetAtPath<CosmeticRegistry>(Registry);
        if (registry == null)
        {
            registry = ScriptableObject.CreateInstance<CosmeticRegistry>();
            AssetDatabase.CreateAsset(registry, Registry);
        }

        var ordered = new System.Collections.Generic.List<CosmeticDefinition>();
        foreach (var (id, name, slot, kind, prefabPath, back) in Items)
        {
            string path = $"{DefsPath}/{id}.asset";
            var def = AssetDatabase.LoadAssetAtPath<CosmeticDefinition>(path);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<CosmeticDefinition>();
                AssetDatabase.CreateAsset(def, path);
            }
            def.id = id;
            def.displayName = name;
            def.slot = slot;
            def.kind = kind;
            def.backFacing = back;
            def.prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/{prefabPath}");
            if (def.prefab == null) Debug.LogError($"[Cosmetics] {id}: no prefab at {Root}/{prefabPath}");
            var icon = AssetDatabase.LoadAssetAtPath<Sprite>($"{Root}/Icons/{id}.png");
            if (icon != null) def.icon = icon;
            EditorUtility.SetDirty(def);
            ordered.Add(def);
        }
        // Meshes nobody has listed yet
        foreach (var file in Directory.GetFiles(Root + "/Meshes", "*.fbx"))
        {
            string id = Path.GetFileNameWithoutExtension(file);
            if (!ordered.Exists(d => d.id == id)) Debug.LogWarning($"[Cosmetics] {id}.fbx is in Meshes/ but not in CosmeticRegistryBuilder.Items");
        }
        // Keep anything added to the registry by hand (not ones whose asset is gone)
        foreach (var extra in registry.items)
            if (extra != null && !ordered.Contains(extra) && AssetDatabase.Contains(extra)) ordered.Add(extra);
        registry.items = ordered;
        EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssets();
        return registry;
    }

    /// <summary>Points every definition at its picture (after the pictures are rendered).</summary>
    public static void LinkIcons()
    {
        var registry = AssetDatabase.LoadAssetAtPath<CosmeticRegistry>(Registry);
        if (registry == null) return;
        foreach (var def in registry.items)
        {
            if (def == null) continue;
            var icon = AssetDatabase.LoadAssetAtPath<Sprite>($"{Root}/Icons/{def.id}.png");
            if (icon == null) continue;
            def.icon = icon;
            EditorUtility.SetDirty(def);
        }
        AssetDatabase.SaveAssets();
    }
}
