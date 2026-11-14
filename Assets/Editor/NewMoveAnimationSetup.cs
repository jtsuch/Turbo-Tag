#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Sets up Jimmy's new move clips (Assets/Animations/{Crouch,CrouchWalk,Charge,ThrowLeft,PlantBomb,DefuseBomb,LadderClimb,LadderHold}.fbx)
/// and hooks them into the Animator:
///   1. names each FBX's take after the file, sets the loops, and writes fixed copies (same path and unit fixes as the
///      ledge and emote clips, <see cref="LedgeAnimationSetup.Remapped"/>) to Assets/Animations/Moves;
///   2. puts each clip's feet on the floor: these were exported ~0.37 m high (their lowest bone sat that far above
///      the idle pose's), so the top animated bone's position curves are shifted down by the measured amount;
///   3. (re)builds three layers PlayerAnimatorController drives by name:
///        "Crouch Layer" (full body, just above the Base Layer): Crouch ↔ CrouchWalk on MovementSpeed;
///        "Action Layer" (full body, under the gun arm): Charge (Bulldozer), PlantBomb, DefuseBomb, LadderClimb, LadderHold;
///        "Throw Layer" (left arm only, on top): ThrowLeft, played once per throw.
/// Re-runnable. Report: Library/TurboTag/scratch/move_setup.txt.
/// Menu: Turbo Tag/Animation/Set Up New Move Animations (editor job: SetupNewMoveAnimations).
/// </summary>
public static class NewMoveAnimationSetup
{
    public const string CrouchLayer = "Crouch Layer", ActionLayer = "Action Layer", ThrowLayer = "Throw Layer";
    private const string Controller = "Assets/Animations/JimmyAnimationController.controller";
    private const string PlayerPrefab = "Assets/Resources/Player/ThePlayer.prefab";
    private const string IdleClip = "Assets/Animations/Idle.anim";
    private const string LeftArmMaskPath = "Assets/Animations/LeftArmMask.mask";
    private const string Folder = "Assets/Animations/";
    private const string OutFolder = "Assets/Animations/Moves";
    private const string Report = "Library/TurboTag/scratch/move_setup.txt";

    // Clip name (= file name), loops
    private static readonly (string name, bool loop)[] Clips =
    {
        ("Crouch", true), ("CrouchWalk", true), ("Charge", true),
        ("ThrowLeft", false), ("PlantBomb", true), ("DefuseBomb", true),
        ("LadderClimb", true), ("LadderHold", true),
    };

    [MenuItem("Turbo Tag/Animation/Set Up New Move Animations")]
    public static void Run()
    {
        var report = new StringBuilder();
        var clips = new Dictionary<string, AnimationClip>();
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

            var source = AssetDatabase.LoadAllAssetsAtPath(Folder + name + ".fbx").OfType<AnimationClip>()
                                      .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (source == null) { report.AppendLine("! no clip for " + name); continue; }
            clips[name] = LedgeAnimationSetup.Remapped(source, name, loop, straightenStomach: false, folder: OutFolder);
        }
        AssetDatabase.SaveAssets();

        Ground(clips, report);
        BuildLayers(clips, report);

        AssetDatabase.SaveAssets();
        Directory.CreateDirectory(Path.GetDirectoryName(Report));
        File.WriteAllText(Report, report.ToString());
        Debug.Log("[New Move Animations]\n" + report);
    }

    // ─── Feet on the floor ────────────────────────────────────────────────────

    private static void Ground(Dictionary<string, AnimationClip> clips, StringBuilder report)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClip);
        if (prefab == null || idle == null) { report.AppendLine("! can't ground clips: no player prefab or idle clip"); return; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        try
        {
            go.transform.position = new Vector3(0f, 500f, 0f);
            var animatorObject = go.GetComponentInChildren<Animator>().gameObject;
            var armature = go.transform.Find("Armature");
            float Lowest()
            {
                float low = float.MaxValue;
                foreach (var t in armature.GetComponentsInChildren<Transform>(true)) low = Mathf.Min(low, t.position.y);
                return low;
            }
            idle.SampleAnimation(animatorObject, 0f);
            float reference = Lowest();

            foreach (var (name, _) in Clips)
            {
                if (!clips.TryGetValue(name, out var clip)) continue;
                // Ladder poses hang on the rungs, not on the floor (and only move the Stomach): left as Justin made them
                if (name.StartsWith("Ladder")) { report.AppendLine($"{name}: {clip.length:0.00} s, not grounded (a ladder pose)"); continue; }
                clip.SampleAnimation(animatorObject, 0f);
                float lift = Lowest() - reference;
                if (Mathf.Abs(lift) < 0.03f) { report.AppendLine($"{name}: {clip.length:0.00} s, on the floor already"); continue; }

                // The top-most bone with position curves carries the body: move it down by the lift, in its parent's space
                var bindings = AnimationUtility.GetCurveBindings(clip)
                    .Where(b => b.type == typeof(Transform) && b.propertyName.StartsWith("m_LocalPosition")).ToList();
                if (bindings.Count == 0) { report.AppendLine($"! {name}: no position curves to lower"); continue; }
                string path = bindings.Select(b => b.path).OrderBy(p => p.Count(c => c == '/')).First();
                var bone = animatorObject.transform.Find(path);
                if (bone == null || bone.parent == null) { report.AppendLine($"! {name}: bone {path} not found"); continue; }
                Vector3 delta = bone.parent.InverseTransformVector(Vector3.down * lift);
                string[] axes = { "x", "y", "z" };
                for (int a = 0; a < 3; a++)
                {
                    var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition." + axes[a]);
                    var curve = AnimationUtility.GetEditorCurve(clip, binding)
                                ?? AnimationCurve.Constant(0f, clip.length, bone.localPosition[a]);
                    var keys = curve.keys;
                    for (int k = 0; k < keys.Length; k++) keys[k].value += delta[a];
                    curve.keys = keys;
                    AnimationUtility.SetEditorCurve(clip, binding, curve);
                }
                EditorUtility.SetDirty(clip);
                clip.SampleAnimation(animatorObject, 0f);
                report.AppendLine($"{name}: {clip.length:0.00} s, lowered {lift:0.00} m on {path} (now {Lowest() - reference:+0.00;-0.00} from idle)");
            }
        }
        finally { Object.DestroyImmediate(go); }
    }

    // ─── Layers ───────────────────────────────────────────────────────────────

    private static void BuildLayers(Dictionary<string, AnimationClip> clips, StringBuilder report)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
        if (controller == null) { report.AppendLine("! no controller at " + Controller); return; }
        foreach (var name in new[] { CrouchLayer, ActionLayer, ThrowLayer })
        {
            int old = controller.layers.ToList().FindIndex(l => l.name == name);
            if (old >= 0) controller.RemoveLayer(old);
        }
        clips.TryGetValue("Crouch", out var crouch);
        clips.TryGetValue("CrouchWalk", out var crouchWalk);

        // Crouch: still ↔ walking (MovementSpeed is 0 still, 0.5 walking, 1 sprinting)
        var crouchMachine = Machine(controller, CrouchLayer);
        var tree = new BlendTree { name = "CrouchTree", blendParameter = "MovementSpeed", blendType = BlendTreeType.Simple1D, useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(tree, controller);
        tree.AddChild(crouch, 0f);
        tree.AddChild(crouchWalk, 0.5f);
        tree.AddChild(crouchWalk, 1f);
        var crouchState = crouchMachine.AddState("CrouchMove");
        crouchState.motion = tree;
        crouchMachine.defaultState = crouchState;

        var actionMachine = Machine(controller, ActionLayer);
        actionMachine.defaultState = actionMachine.AddState("Empty");
        foreach (var name in new[] { "Charge", "PlantBomb", "DefuseBomb", "LadderClimb", "LadderHold" })
            if (clips.TryGetValue(name, out var clip)) actionMachine.AddState(name).motion = clip;

        var throwMachine = Machine(controller, ThrowLayer);
        throwMachine.defaultState = throwMachine.AddState("Empty");
        if (clips.TryGetValue("ThrowLeft", out var throwClip)) throwMachine.AddState("ThrowLeft").motion = throwClip;

        // Order: Crouch and Action just above the Base Layer (so the gun arm, wall run, climb, head, emote and ledge
        // layers still win over them); Throw on top of everything, on the left arm only
        var layers = controller.layers.ToList();
        var crouchLayer = new AnimatorControllerLayer { name = CrouchLayer, stateMachine = crouchMachine, blendingMode = AnimatorLayerBlendingMode.Override, defaultWeight = 0f };
        var actionLayer = new AnimatorControllerLayer { name = ActionLayer, stateMachine = actionMachine, blendingMode = AnimatorLayerBlendingMode.Override, defaultWeight = 0f };
        var throwLayer = new AnimatorControllerLayer { name = ThrowLayer, stateMachine = throwMachine, blendingMode = AnimatorLayerBlendingMode.Override, defaultWeight = 0f, avatarMask = LeftArmMask(report) };
        layers.Insert(1, actionLayer);
        layers.Insert(1, crouchLayer);
        layers.Add(throwLayer);
        controller.layers = layers.ToArray();
        EditorUtility.SetDirty(controller);
        report.AppendLine("Layers: " + string.Join(", ", controller.layers.Select(l => l.name)));
    }

    private static AnimatorStateMachine Machine(AnimatorController controller, string name)
    {
        var machine = new AnimatorStateMachine { name = name, hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(machine, controller);
        return machine;
    }

    // Every transform of the player's model, on only for the left arm's chain
    private static AvatarMask LeftArmMask(StringBuilder report)
    {
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(LeftArmMaskPath);
        if (mask == null)
        {
            mask = new AvatarMask();
            AssetDatabase.CreateAsset(mask, LeftArmMaskPath);
        }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
        var root = prefab.GetComponentInChildren<Animator>().transform;
        var paths = root.GetComponentsInChildren<Transform>(true).Where(t => t != root).Select(t => AnimationUtility.CalculateTransformPath(t, root)).ToList();
        mask.transformCount = paths.Count;
        int on = 0;
        for (int i = 0; i < paths.Count; i++)
        {
            bool left = paths[i].Split('/').Any(s => s.StartsWith("LeftShoulder") || s.StartsWith("LeftArm") || s.StartsWith("LeftHand"));
            mask.SetTransformPath(i, paths[i]);
            mask.SetTransformActive(i, left);
            if (left) on++;
        }
        EditorUtility.SetDirty(mask);
        report.AppendLine($"Left arm mask: {on} of {paths.Count} transforms");
        return mask;
    }
}
#endif
