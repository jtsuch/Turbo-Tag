using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Hooks Justin's emote clips (Assets/Animations/{DanceShuffle,WaveFriendly}.fbx) into Jimmy's Animator: sets each
/// FBX's clip up (named after the file; the dance loops), writes fixed copies to Assets/Animations/Emotes (the same
/// path and unit fixes as the ledge clips: <see cref="LedgeAnimationSetup.Remapped"/>), and (re)builds the full-body
/// "Emote Layer" with an Empty state and one state per clip. PlayerEmotes cross-fades into them and blends the layer
/// in for the emote's length. Re-runnable. Report: Library/TurboTag/scratch/emote_setup.txt.
/// Menu: Turbo Tag/Animation/Set Up Emote Animations (editor job: SetupEmoteAnimations).
/// </summary>
public static class EmoteAnimationSetup
{
    public const string LayerName = "Emote Layer";
    private const string Controller = "Assets/Animations/JimmyAnimationController.controller";
    private const string Folder = "Assets/Animations/";
    private const string OutFolder = "Assets/Animations/Emotes";
    private const string Report = "Library/TurboTag/scratch/emote_setup.txt";

    // State name (= clip name = file name), loops
    private static readonly (string name, bool loop)[] Clips = { ("DanceShuffle", true), ("WaveFriendly", false) };

    [MenuItem("Turbo Tag/Animation/Set Up Emote Animations")]
    public static void Run()
    {
        var report = new StringBuilder();
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
        if (controller == null) { File.WriteAllText(Report, "No controller at " + Controller); return; }

        foreach (var (name, loop) in Clips)
        {
            var importer = AssetImporter.GetAtPath(Folder + name + ".fbx") as ModelImporter;
            if (importer == null) { report.AppendLine("! missing " + name); continue; }
            var takes = importer.defaultClipAnimations;
            if (takes.Length == 0) { report.AppendLine("! no takes in " + name); continue; }
            var take = takes[0];
            take.name = name;
            take.loopTime = loop;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.clipAnimations = new[] { take };
            importer.SaveAndReimport();
        }

        var old = controller.layers.ToList().FindIndex(l => l.name == LayerName);
        if (old >= 0) controller.RemoveLayer(old);
        var machine = new AnimatorStateMachine { name = LayerName, hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(machine, controller);
        machine.defaultState = machine.AddState("Empty");

        foreach (var (name, loop) in Clips)
        {
            var source = AssetDatabase.LoadAllAssetsAtPath(Folder + name + ".fbx").OfType<AnimationClip>()
                                      .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (source == null) { report.AppendLine("! no clip for " + name); continue; }
            var clip = LedgeAnimationSetup.Remapped(source, name, loop, straightenStomach: false, folder: OutFolder);
            machine.AddState(name).motion = clip;
            var paths = AnimationUtility.GetCurveBindings(source).Select(b => b.path).Distinct().Take(3);
            report.AppendLine($"{name}: {clip.length:0.00} s, loop {loop}, source paths {string.Join(" | ", paths)}");
        }

        controller.AddLayer(new AnimatorControllerLayer
        {
            name = LayerName,
            stateMachine = machine,
            blendingMode = AnimatorLayerBlendingMode.Override,
            defaultWeight = 0f,
        });
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        report.AppendLine("Layers: " + string.Join(", ", controller.layers.Select(l => l.name)));
        Directory.CreateDirectory(Path.GetDirectoryName(Report));
        File.WriteAllText(Report, report.ToString());
    }
}
