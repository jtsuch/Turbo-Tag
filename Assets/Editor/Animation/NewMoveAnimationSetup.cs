#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Sets up Jimmy's new move clips (Assets/Animations/{Crouch,CrouchWalk,Charge,ThrowLeft,PlantBomb,DefuseBomb,LadderClimb,LadderHold,Reload,WeaponSwap,KnifeSwing,CoverEyes}.fbx)
/// and hooks them into the Animator:
///   1. names each FBX's take after the file, sets the loops, and writes fixed copies (same path and unit fixes as the
///      ledge and emote clips, <see cref="LedgeAnimationSetup.Remapped"/>) to Assets/Animations/Moves;
///   2. puts each clip's feet on the floor: these were exported ~0.37 m high (their lowest bone sat that far above
///      the idle pose's), so the top animated bone's position curves are shifted down by the measured amount;
///   3. (re)builds three layers PlayerAnimatorController drives by name:
///        "Crouch Layer" (full body, just above the Base Layer): Crouch ↔ CrouchWalk on MovementSpeed;
///        "Action Layer" (full body, under the gun arm): Charge (Bulldozer), PlantBomb, DefuseBomb, LadderClimb, LadderHold, CoverEyes;
///        "Throw Layer" (left arm only, on top): ThrowLeft, Reload;
///        "Swap Layer" (right arm only, on top): WeaponSwap, KnifeSwing.
/// Re-runnable. Report: Library/TurboTag/scratch/move_setup.txt.
/// Menu: Turbo Tag/Animation/Set Up New Move Animations (editor job: SetupNewMoveAnimations).
/// </summary>
public static class NewMoveAnimationSetup
{
    public const string CrouchLayer = "Crouch Layer", ActionLayer = "Action Layer", ThrowLayer = "Throw Layer", SwapLayer = "Swap Layer";
    public const string ReloadSpeed = "ReloadSpeed", SwapSpeed = "SwapSpeed";
    private const string Controller = "Assets/Animations/JimmyAnimationController.controller";
    private const string PlayerPrefab = "Assets/Resources/Player/ThePlayer.prefab";
    private const string IdleClip = "Assets/Animations/Idle.anim";
    private const string LeftArmMaskPath = "Assets/Animations/LeftArmMask.mask";
    private const string RightArmMaskPath = "Assets/Animations/RightArmMask.mask";
    private const string Folder = "Assets/Animations/";
    private const string OutFolder = "Assets/Animations/Moves";
    private const string Report = "Library/TurboTag/scratch/move_setup.txt";

    // Clip name (= file name), loops
    private static readonly (string name, bool loop)[] Clips =
    {
        ("Crouch", true), ("CrouchWalk", true), ("Charge", true),
        ("ThrowLeft", false), ("PlantBomb", true), ("DefuseBomb", true),
        ("LadderClimb", true), ("LadderHold", true),
        ("Reload", false), ("WeaponSwap", false), ("KnifeSwing", false),
        ("CoverEyes", true),
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
            // Same units as the rest: Justin's clips come out of Blender with an FBX unit scale of 1 (centimetres: Unity's
            // file scale 0.01), which the player model's Armature scale expects. One saved with a unit scale of 100
            // (CoverEyes, Nov 14) had bone offsets 100x too big and stretched into a "tornado" in the sky; its scale
            // factor is set so file scale × scale factor is 0.01 like the rest
            float scale = importer.fileScale > 0f ? 0.01f / importer.fileScale : 1f;
            if (!importer.useFileScale) scale = importer.globalScale;
            if (Mathf.Abs(importer.globalScale - scale) > 1e-5f)
            {
                report.AppendLine($"{name}: file scale {importer.fileScale} -> scale factor {scale} (was {importer.globalScale})");
                importer.globalScale = scale;
            }
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

    // Poses leaned forward a little (degrees, round the Armature root; Justin, Nov 14: the crouch looked upright)
    private static readonly Dictionary<string, float> Lean = new() { ["Crouch"] = 8f, ["CrouchWalk"] = 8f };

    /// <summary>The throw plays this many times faster than it was made (Justin, Nov 14: "greatly sped up").</summary>
    public const float ThrowSpeed = 3f;

    // Feet on the floor, measured on the skinned mesh itself (the lowest vertex against Idle's; bone positions let the
    // crouch hover 6–12 cm), after an optional forward lean. Ladder poses hang on the rungs, so they're not lowered:
    // they're moved forward instead so the body sits on its capsule (they were made ~0.6 m behind it, so the climber
    // looked a body's width off the ladder), matching Idle's Stomach across the ground.
    private static void Ground(Dictionary<string, AnimationClip> clips, StringBuilder report)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClip);
        if (prefab == null || idle == null) { report.AppendLine("! can't ground clips: no player prefab or idle clip"); return; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        try
        {
            go.transform.SetPositionAndRotation(new Vector3(0f, 500f, 0f), Quaternion.identity);
            var animatorObject = go.GetComponentInChildren<Animator>().gameObject;
            var skins = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var mesh = new Mesh();
            float LowestMesh()
            {
                float low = float.MaxValue;
                foreach (var s in skins)
                {
                    if (s.sharedMesh == null) continue;
                    s.BakeMesh(mesh, true);
                    foreach (var v in mesh.vertices) low = Mathf.Min(low, s.transform.TransformPoint(v).y);
                }
                return low;
            }
            Transform Find(string n) => go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
            idle.SampleAnimation(animatorObject, 0f);
            float reference = LowestMesh();
            var stomach = Find("Stomach");
            Vector3 idleStomach = stomach != null ? stomach.position : Vector3.zero;

            foreach (var (name, _) in Clips)
            {
                if (!clips.TryGetValue(name, out var clip)) continue;
                var bindings = AnimationUtility.GetCurveBindings(clip)
                    .Where(b => b.type == typeof(Transform) && b.propertyName.StartsWith("m_LocalPosition")).ToList();
                if (bindings.Count == 0) { report.AppendLine($"! {name}: no position curves to move"); continue; }
                // The top-most bone with position curves carries the body
                string path = bindings.Select(b => b.path).OrderBy(p => p.Count(c => c == '/')).First();
                var bone = animatorObject.transform.Find(path);
                if (bone == null || bone.parent == null) { report.AppendLine($"! {name}: bone {path} not found"); continue; }

                if (Lean.TryGetValue(name, out float lean)) report.AppendLine($"{name}: {LeanForward(clip, bone, path, lean, animatorObject)}");

                clip.SampleAnimation(animatorObject, 0f);
                Vector3 move;
                if (name.StartsWith("Ladder"))
                {
                    if (stomach == null) { report.AppendLine($"! {name}: no Stomach bone"); continue; }
                    Vector3 off = idleStomach - stomach.position;
                    move = new Vector3(off.x, 0f, off.z);   // Across the ground only: the height is the pose's own
                }
                else
                {
                    float lift = LowestMesh() - reference;
                    if (Mathf.Abs(lift) < 0.01f) { report.AppendLine($"{name}: {clip.length:0.00} s, on the floor already"); continue; }
                    move = Vector3.down * lift;
                }
                Shift(clip, bone, path, move);
                // Ladder poses: the hips (LeftHip / RigthHip) are the Stomach's siblings, not its children, so they move
                // with it too (Justin, Nov 14: the legs stuck out further from the ladder than the arms). The same for any
                // clip without Armature curves (CoverEyes, the arm clips): lowering only the Stomach left the feet 10 cm up
                if (path.Contains("/"))
                {
                    string parentPath = path.Contains("/") ? path.Substring(0, path.LastIndexOf('/')) : "";
                    foreach (var sibling in bindings.Select(b => b.path).Distinct())
                    {
                        if (sibling == path || !sibling.Contains("/") || sibling.Substring(0, sibling.LastIndexOf('/')) != parentPath) continue;
                        var sib = animatorObject.transform.Find(sibling);
                        if (sib != null) { Shift(clip, sib, sibling, move); report.AppendLine($"  {name}: also moved {sibling}"); }
                    }
                }
                clip.SampleAnimation(animatorObject, 0f);
                string after = name.StartsWith("Ladder") && stomach != null
                    ? $"stomach now {(stomach.position - idleStomach).z:+0.00;-0.00} m from idle's (forward)"
                    : $"mesh now {LowestMesh() - reference:+0.000;-0.000} m from idle's lowest point";
                report.AppendLine($"{name}: {clip.length:0.00} s, moved ({move.x:0.00}, {move.y:0.00}, {move.z:0.00}) on {path}; {after}");
            }
            Object.DestroyImmediate(mesh);
        }
        finally { Object.DestroyImmediate(go); }
    }

    // Adds a world-space offset to a bone's position curves (in its parent's space; curves made where missing)
    private static void Shift(AnimationClip clip, Transform bone, string path, Vector3 world)
    {
        Vector3 delta = bone.parent.InverseTransformVector(world);
        string[] axes = { "x", "y", "z" };
        for (int a = 0; a < 3; a++)
        {
            var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition." + axes[a]);
            var curve = AnimationUtility.GetEditorCurve(clip, binding) ?? AnimationCurve.Constant(0f, clip.length, bone.localPosition[a]);
            var keys = curve.keys;
            for (int k = 0; k < keys.Length; k++) keys[k].value += delta[a];
            curve.keys = keys;
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }
        EditorUtility.SetDirty(clip);
    }

    // Tips the whole pose forward by turning the carrying bone's rotation curves (quaternion keys, re-keyed at every
    // key time of any of the four; made where missing) about the character's right axis
    private static string LeanForward(AnimationClip clip, Transform bone, string path, float degrees, GameObject animatorObject)
    {
        string[] comps = { "x", "y", "z", "w" };
        var curves = new AnimationCurve[4];
        var times = new SortedSet<float> { 0f, clip.length };
        for (int c = 0; c < 4; c++)
        {
            curves[c] = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation." + comps[c]));
            if (curves[c] != null) foreach (var k in curves[c].keys) times.Add(k.time);
        }
        clip.SampleAnimation(animatorObject, 0f);
        Quaternion parent = bone.parent.rotation;
        Quaternion turn = Quaternion.Inverse(parent) * Quaternion.AngleAxis(degrees, Vector3.right) * parent;   // World tilt → parent space
        Quaternion rest = bone.localRotation;
        var outCurves = new AnimationCurve[4];
        for (int c = 0; c < 4; c++) outCurves[c] = new AnimationCurve();
        foreach (float t in times)
        {
            Quaternion q = curves.All(cv => cv != null)
                ? new Quaternion(curves[0].Evaluate(t), curves[1].Evaluate(t), curves[2].Evaluate(t), curves[3].Evaluate(t)).normalized
                : rest;
            Quaternion r = turn * q;
            for (int c = 0; c < 4; c++) outCurves[c].AddKey(new Keyframe(t, r[c]));
        }
        for (int c = 0; c < 4; c++)
        {
            for (int k = 0; k < outCurves[c].length; k++) AnimationUtility.SetKeyLeftTangentMode(outCurves[c], k, AnimationUtility.TangentMode.Linear);
            for (int k = 0; k < outCurves[c].length; k++) AnimationUtility.SetKeyRightTangentMode(outCurves[c], k, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation." + comps[c]), outCurves[c]);
        }
        EditorUtility.SetDirty(clip);
        return $"leaned {degrees:0}° forward on {path} ({times.Count} keys)";
    }

    // ─── Layers ───────────────────────────────────────────────────────────────

    private static void BuildLayers(Dictionary<string, AnimationClip> clips, StringBuilder report)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
        if (controller == null) { report.AppendLine("! no controller at " + Controller); return; }
        foreach (var name in new[] { CrouchLayer, ActionLayer, ThrowLayer, SwapLayer })
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
        foreach (var name in new[] { "Charge", "PlantBomb", "DefuseBomb", "LadderClimb", "LadderHold", "CoverEyes" })
            if (clips.TryGetValue(name, out var clip)) actionMachine.AddState(name).motion = clip;

        var throwMachine = Machine(controller, ThrowLayer);
        throwMachine.defaultState = throwMachine.AddState("Empty");
        if (clips.TryGetValue("ThrowLeft", out var throwClip))
        {
            var throwState = throwMachine.AddState("ThrowLeft");
            throwState.motion = throwClip;
            throwState.speed = ThrowSpeed;   // PlayerAnimatorController.ThrowLength follows
        }
        // Reload and weapon swap play at a speed set per use (ReloadSpeed / SwapSpeed: the clip's length ÷ the gun's
        // reload or draw time), so every gun's reload and draw match their animations
        foreach (var p in new[] { ReloadSpeed, SwapSpeed })
            if (!controller.parameters.Any(x => x.name == p))
                controller.AddParameter(new AnimatorControllerParameter { name = p, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
        if (clips.TryGetValue("Reload", out var reloadClip))
        {
            var reloadState = throwMachine.AddState("Reload");   // left arm only, same layer as the throw
            reloadState.motion = reloadClip;
            reloadState.speedParameterActive = true;
            reloadState.speedParameter = ReloadSpeed;
        }

        var swapMachine = Machine(controller, SwapLayer);
        swapMachine.defaultState = swapMachine.AddState("Empty");
        if (clips.TryGetValue("WeaponSwap", out var swapClip))
        {
            var swapState = swapMachine.AddState("WeaponSwap");
            swapState.motion = swapClip;
            swapState.speedParameterActive = true;
            swapState.speedParameter = SwapSpeed;
        }
        if (clips.TryGetValue("KnifeSwing", out var knifeClip)) swapMachine.AddState("KnifeSwing").motion = knifeClip;   // right arm only, same layer as the weapon swap

        // Order: Crouch and Action just above the Base Layer (so the gun arm, wall run, climb, head, emote and ledge
        // layers still win over them); Throw on top of everything, on the left arm only
        var layers = controller.layers.ToList();
        var crouchLayer = new AnimatorControllerLayer { name = CrouchLayer, stateMachine = crouchMachine, blendingMode = AnimatorLayerBlendingMode.Override, defaultWeight = 0f };
        var actionLayer = new AnimatorControllerLayer { name = ActionLayer, stateMachine = actionMachine, blendingMode = AnimatorLayerBlendingMode.Override, defaultWeight = 0f };
        var throwLayer = new AnimatorControllerLayer { name = ThrowLayer, stateMachine = throwMachine, blendingMode = AnimatorLayerBlendingMode.Override, defaultWeight = 0f, avatarMask = LeftArmMask(report) };
        layers.Insert(1, actionLayer);
        layers.Insert(1, crouchLayer);
        layers.Add(throwLayer);
        layers.Add(new AnimatorControllerLayer { name = SwapLayer, stateMachine = swapMachine, blendingMode = AnimatorLayerBlendingMode.Override, defaultWeight = 0f, avatarMask = ArmMask(RightArmMaskPath, "Right", report) });
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
    private static AvatarMask LeftArmMask(StringBuilder report) => ArmMask(LeftArmMaskPath, "Left", report);

    private static AvatarMask ArmMask(string maskPath, string side, StringBuilder report)
    {
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(maskPath);
        if (mask == null)
        {
            mask = new AvatarMask();
            AssetDatabase.CreateAsset(mask, maskPath);
        }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
        var root = prefab.GetComponentInChildren<Animator>().transform;
        var paths = root.GetComponentsInChildren<Transform>(true).Where(t => t != root).Select(t => AnimationUtility.CalculateTransformPath(t, root)).ToList();
        mask.transformCount = paths.Count;
        int on = 0;
        for (int i = 0; i < paths.Count; i++)
        {
            bool left = paths[i].Split('/').Any(s => s.StartsWith(side + "Shoulder") || s.StartsWith(side + "Arm") || s.StartsWith(side + "Hand"));
            mask.SetTransformPath(i, paths[i]);
            mask.SetTransformActive(i, left);
            if (left) on++;
        }
        EditorUtility.SetDirty(mask);
        report.AppendLine($"{side} arm mask: {on} of {paths.Count} transforms");
        return mask;
    }
}
#endif
