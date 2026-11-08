using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Hooks Justin's ledge clips (Assets/Animations/Ledge*.fbx) into Jimmy's Animator: sets each FBX's clip up (one
/// clip named after the file; idle, shimmy and the looks loop), then (re)builds the "Ledge Layer" (full body,
/// override, weight driven by PlayerAnimatorController while hanging) with one state per clip, plus an empty
/// state, and the "LedgeShimmySpeed" float. The FBX files were exported with the Armature as their root, so their
/// curves say "Stomach/..." where Jimmy's Animator (on ThePlayer) needs "Armature/Stomach/...": each clip is copied to
/// Assets/Animations/Ledge/{name}.anim with the paths fixed (and the climb-up's Stomach slide removed: the body is
/// lifted by physics, so the clip moving it too would double up), and the states use the copies (the shimmy plays backwards for the other direction). Re-runnable: the
/// layer is replaced each time. Report: Library/TurboTag/scratch/ledge_setup.txt.
/// Menu: Turbo Tag/Animation/Set Up Ledge Animations (editor job: SetupLedgeAnimations).
/// </summary>
public static class LedgeAnimationSetup
{
    public const string LayerName = "Ledge Layer";
    public const string ShimmySpeed = "LedgeShimmySpeed";
    private const string Controller = "Assets/Animations/JimmyAnimationController.controller";
    private const string Folder = "Assets/Animations/";
    private const string Report = "Library/TurboTag/scratch/ledge_setup.txt";
    private const string OutFolder = "Assets/Animations/Ledge";
    private const float  ClimbSeconds = 0.75f; // PlayerMovement.ClimbOntoLedge's duration

    // Jimmy's armature scale ÷ the clip's (1 when they already match). `root` is the clip's path to the Armature
    private static float UnitFactor(AnimationClip source, string root = "")
    {
        var prefab = Resources.Load<GameObject>("Player/ThePlayer");
        var armature = prefab != null ? prefab.transform.Find("Armature") : null;
        var binding = EditorCurveBinding.FloatCurve(root, typeof(Transform), "m_LocalScale.x");
        var curve = AnimationUtility.GetEditorCurve(source, binding);
        if (armature == null || curve == null) return 1f;
        float clipScale = curve.Evaluate(0f);
        return clipScale > 1e-5f ? armature.localScale.x / clipScale : 1f;
    }

    private static AnimationCurve Scaled(AnimationCurve curve, float k)
    {
        var keys = curve.keys;
        for (int i = 0; i < keys.Length; i++)
        {
            keys[i].value *= k;
            keys[i].inTangent *= k;
            keys[i].outTangent *= k;
        }
        return new AnimationCurve(keys) { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };
    }

    /// <summary>
    /// A copy of an FBX clip that binds to Jimmy's hierarchy, saved as {folder}/{name}.anim. Clips exported with the
    /// Armature as their root ("" → Armature, "Stomach/…" → "Armature/Stomach/…") are re-pathed; clips that already
    /// start at "Armature" keep their paths. Either way the units are matched to Jimmy (see UnitFactor). Shared with
    /// the emote clips (EmoteAnimationSetup).
    /// </summary>
    internal static AnimationClip Remapped(AnimationClip source, string name, bool loop, bool straightenStomach, string folder = OutFolder)
    {
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace(Path.DirectorySeparatorChar, '/'), Path.GetFileName(folder));
        var clip = new AnimationClip { name = name, frameRate = source.frameRate };
        var bindings = AnimationUtility.GetCurveBindings(source);
        bool armatureRooted = bindings.Any(b => b.path == "Armature" || b.path.StartsWith("Armature/"));
        string rootPath = armatureRooted ? "Armature" : "";
        // Units: these FBX files have the Armature scaled 100× smaller than Jimmy's (0.595 vs 59.5) and every bone
        // offset 100× bigger, which collapsed him to a speck. Rescale both to Jimmy's armature.
        float factor = UnitFactor(source, rootPath);
        foreach (var b in bindings)
        {
            var curve = AnimationUtility.GetEditorCurve(source, b);
            bool root = b.path == rootPath;
            if (root && b.propertyName.StartsWith("m_LocalScale")) curve = Scaled(curve, factor);
            else if (!root && b.propertyName.StartsWith("m_LocalPosition")) curve = Scaled(curve, 1f / factor);
            // Climb-up: physics lifts the body in a straight line over the clip's length, so take the clip's own
            // straight-line travel out of the hips and keep only its arc (up first, then over). Removing the hips'
            // motion completely left them behind while the legs swung forward.
            if (straightenStomach && b.path == "Stomach" && b.propertyName.StartsWith("m_LocalPosition"))
                curve = Ramped(curve, -(curve.Evaluate(source.length) - curve.Evaluate(0f)), 0f, source.length);
            var to = b;
            to.path = armatureRooted ? b.path : root ? "Armature" : "Armature/" + b.path;
            AnimationUtility.SetEditorCurve(clip, to, curve);
        }
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        string path = $"{folder}/{name}.anim";
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing != null) { EditorUtility.CopySerialized(clip, existing); EditorUtility.SetDirty(existing); return existing; }
        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }

    // Adds a straight ramp to a curve: `atStart` at t = 0, rising by `change` by t = length
    private static AnimationCurve Ramped(AnimationCurve curve, float change, float atStart, float length)
    {
        var keys = curve.keys;
        if (keys.Length == 0 || length <= 0f) return curve;
        float slope = change / length;
        float first = keys[0].value;
        for (int i = 0; i < keys.Length; i++)
        {
            keys[i].value += atStart + slope * keys[i].time;
            keys[i].inTangent += slope;
            keys[i].outTangent += slope;
        }
        var result = new AnimationCurve(keys) { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };
        // A constant curve may have a single key: give it one at the end so the ramp happens
        if (keys.Length == 1) result.AddKey(new Keyframe(length, first + atStart + change, slope, slope));
        return result;
    }

    // ─── Hang placement ───────────────────────────────────────────────────────

    // From PlayerMovement: hanging, the player's root sits 1 m out from the ledge point and 1.8 m below it, and
    // the ledge point is 0.25 m inside the wall, so the wall face is 0.75 m in front of the root.
    private const float WallFaceAhead = 0.75f;
    private const float LedgeTopAbove = 1.8f;
    private const float HandsAbove    = 0.08f;  // Hands just above the ledge top

    private const float BellyOverlap  = 0.15f;  // How far the round belly may sink into the wall
    private const float MaxLean       = 0f;   // Off: a lean swung the feet out and angled the whole body away from the wall    // Most the model leans in toward the wall (degrees)
    private const float LeanPivotY    = 1.75f;   // The lean turns around the belly (root space height): top in, feet back

    // Where the Armature has to move so a lean turns the model around the belly, not around the feet
    private static Vector3 LeanShift(Vector3 armaturePos, float lean)
    {
        var pivot = new Vector3(0f, LeanPivotY, armaturePos.z);
        return pivot + Quaternion.AngleAxis(lean, Vector3.right) * (armaturePos - pivot) - armaturePos;
    }

    /// <summary>
    /// Poses a copy of the player with the idle hang clip and finds how to place the model against the wall: the
    /// clip's flippers end 0.35 m short of the wall while the belly already touches it, so the model leans in from
    /// the feet (the smallest lean that gets the flipper tips onto the ledge, belly sinking at most BellyOverlap),
    /// slides forward to the wall, and drops so the hands sit just above the ledge top. Only the model moves (the
    /// Armature's position and rotation in the clips); the collider stays, so physics never fights the wall.
    /// </summary>
    private static (float forward, float down, float lean, Vector3 shift) MeasureHang(AnimationClip idle, StringBuilder report)
    {
        var prefab = Resources.Load<GameObject>("Player/ThePlayer");
        if (prefab == null || idle == null) return (0f, 0f, 0f, Vector3.zero);
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var copy = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var armature = copy.transform.Find("Armature");
            var smr = copy.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.bones.Length > 0);
            var tips = copy.GetComponentsInChildren<Transform>(true).Where(t => t.name.EndsWith("Arm4_end")).ToArray();
            if (armature == null || smr == null || tips.Length == 0) return (0f, 0f, 0f, Vector3.zero);

            (float forward, float down, float lean, Vector3 shift) best = (0f, 0f, 0f, Vector3.zero);
            for (float lean = 0f; lean <= MaxLean; lean += 1f)
            {
                idle.SampleAnimation(copy, 0f);
                Vector3 shift = LeanShift(armature.localPosition, lean);
                armature.localRotation = Quaternion.AngleAxis(lean, Vector3.right) * armature.localRotation;
                armature.localPosition += shift;
                float front = BellyFront(smr);
                float tipZ = tips.Average(t => t.position.z), tipY = tips.Max(t => t.position.y);
                float forward = Mathf.Clamp(WallFaceAhead + BellyOverlap - front, -0.3f, 0.6f);
                float down = Mathf.Clamp(tipY - (LedgeTopAbove + HandsAbove), -0.6f, 0.8f);
                best = (forward, down, lean, shift);
                if (tipZ + forward >= WallFaceAhead + 0.03f) break; // Flipper tips over the ledge
            }
            idle.SampleAnimation(copy, 0f);
            armature.localRotation = Quaternion.AngleAxis(best.lean, Vector3.right) * armature.localRotation;
            armature.localPosition += best.shift;
            report.AppendLine($"Hang pose: lean {best.lean:0} deg, model {best.forward:0.00} m toward the wall, {best.down:0.00} m down " +
                              $"(belly front {BellyFront(smr) + best.forward:0.00} m, flipper tips {tips.Average(t => t.position.z) + best.forward:0.00} m; wall at {WallFaceAhead})");
            return best;
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    // How far the body reaches toward the wall below the ledge, between the flippers (root space, z)
    private static float BellyFront(SkinnedMeshRenderer smr)
    {
        var mesh = new Mesh();
        smr.BakeMesh(mesh); // Comes out at world size, in the renderer's orientation (its 80x scale not applied twice)
        var m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
        float front = float.MinValue;
        foreach (var v in mesh.vertices)
        {
            var pt = m.MultiplyPoint3x4(v);
            if (pt.y > 0.4f && pt.y < LedgeTopAbove - 0.15f && Mathf.Abs(pt.x) < 0.45f) front = Mathf.Max(front, pt.z);
        }
        Object.DestroyImmediate(mesh);
        return front > float.MinValue ? front : 0f;
    }

    // Places the model (the Armature) for hanging: leaned in, forward and down. Held for the hanging clips; over the
    // climb-up it eases to nothing (it ends standing on top, where the normal animations take over)
    private static void ApplyHangOffset(AnimationClip clip, float forward, float down, float lean, Vector3 shift, bool easeOut)
    {
        foreach (var (axis, offset) in new[] { ("m_LocalPosition.z", forward + shift.z), ("m_LocalPosition.y", -down + shift.y) })
        {
            var binding = EditorCurveBinding.FloatCurve("Armature", typeof(Transform), axis);
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null) continue;
            curve = Ramped(curve, easeOut ? -offset : 0f, offset, clip.length);
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }
        PreRotate(clip, lean, easeOut ? 0f : lean);
        EditorUtility.SetDirty(clip);
    }

    // Turns the Armature's rotation curves by a lean around the root's sideways axis (from `from` degrees at the
    // start to `to` at the end), resampled at 60 fps
    private static void PreRotate(AnimationClip clip, float from, float to)
    {
        if (Mathf.Approximately(from, 0f) && Mathf.Approximately(to, 0f)) return;
        var axes = new[] { "x", "y", "z", "w" };
        var bindings = axes.Select(a => EditorCurveBinding.FloatCurve("Armature", typeof(Transform), "m_LocalRotation." + a)).ToArray();
        var curves = bindings.Select(b => AnimationUtility.GetEditorCurve(clip, b)).ToArray();
        if (curves.Any(c => c == null)) return;

        int n = Mathf.Max(2, Mathf.CeilToInt(clip.length * 60f) + 1);
        var keys = new Keyframe[4][];
        for (int a = 0; a < 4; a++) keys[a] = new Keyframe[n];
        Quaternion previous = Quaternion.identity;
        for (int i = 0; i < n; i++)
        {
            float t = clip.length * i / (n - 1);
            var q = new Quaternion(curves[0].Evaluate(t), curves[1].Evaluate(t), curves[2].Evaluate(t), curves[3].Evaluate(t)).normalized;
            q = Quaternion.AngleAxis(Mathf.Lerp(from, to, t / Mathf.Max(clip.length, 1e-4f)), Vector3.right) * q;
            if (i > 0 && Quaternion.Dot(previous, q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w); // Stay on one side
            previous = q;
            keys[0][i] = new Keyframe(t, q.x); keys[1][i] = new Keyframe(t, q.y);
            keys[2][i] = new Keyframe(t, q.z); keys[3][i] = new Keyframe(t, q.w);
        }
        for (int a = 0; a < 4; a++)
        {
            var curve = new AnimationCurve(keys[a]);
            for (int k = 0; k < curve.length; k++) curve.SmoothTangents(k, 0f);
            AnimationUtility.SetEditorCurve(clip, bindings[a], curve);
        }
    }

    // State name (= clip name = file name), loops
    private static readonly (string name, bool loop)[] Clips =
    {
        ("LedgeIdle", true), ("LedgeShimmy", true), ("LedgeLookLeft", true), ("LedgeLookRight", true), ("LedgeClimbUp", false),
    };

    [MenuItem("Turbo Tag/Animation/Set Up Ledge Animations")]
    public static void Run()
    {
        var report = new StringBuilder();
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
        if (controller == null) { File.WriteAllText(Report, "No controller at " + Controller); return; }

        // ─── Import settings ───
        foreach (var (name, loop) in Clips)
        {
            string path = Folder + name + ".fbx";
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) { report.AppendLine("! missing " + path); continue; }
            var source = importer.defaultClipAnimations;
            if (source.Length == 0) { report.AppendLine("! no takes in " + path); continue; }
            var clip = source[0];
            clip.name = name;
            clip.loopTime = loop;
            clip.lockRootRotation = clip.lockRootHeightY = clip.lockRootPositionXZ = false;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.clipAnimations = new[] { clip };
            importer.SaveAndReimport();
            report.AppendLine($"{name}: takes {source.Length} ({string.Join(", ", source.Select(s => s.takeName))}), loop {loop}");
        }

        // ─── Parameter ───
        if (!controller.parameters.Any(p => p.name == ShimmySpeed))
            controller.AddParameter(new AnimatorControllerParameter { name = ShimmySpeed, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });

        // ─── Layer (replaced) ───
        var layers = controller.layers.ToList();
        int old = layers.FindIndex(l => l.name == LayerName);
        if (old >= 0) controller.RemoveLayer(old);

        var machine = new AnimatorStateMachine { name = LayerName, hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(machine, controller);
        var empty = machine.AddState("Empty");
        machine.defaultState = empty;
        // Fixed copies of every clip, then the hang offset measured on the idle pose and applied to all of them
        var clips = new System.Collections.Generic.Dictionary<string, AnimationClip>();
        foreach (var (name, loop) in Clips)
        {
            var source = AssetDatabase.LoadAllAssetsAtPath(Folder + name + ".fbx").OfType<AnimationClip>()
                                      .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (source != null) clips[name] = Remapped(source, name, loop, straightenStomach: name == "LedgeClimbUp");
        }
        clips.TryGetValue("LedgeIdle", out var idleClip);
        var (forward, down, lean, shift) = MeasureHang(idleClip, report);
        foreach (var pair in clips) ApplyHangOffset(pair.Value, forward, down, lean, shift, easeOut: pair.Key == "LedgeClimbUp");
        AssetDatabase.SaveAssets();

        foreach (var (name, loop) in Clips)
        {
            clips.TryGetValue(name, out var clip);
            var state = machine.AddState(name);
            state.motion = clip;
            if (name == "LedgeClimbUp" && clip != null) state.speed = clip.length / ClimbSeconds; // Matches PlayerMovement's climb
            if (name == "LedgeShimmy")
            {
                state.speedParameterActive = true;
                state.speedParameter = ShimmySpeed;
            }
            if (clip != null)
            {
                var bindings = AnimationUtility.GetCurveBindings(clip);
                var rootMoves = bindings.Where(b => b.propertyName.StartsWith("m_LocalPosition") && b.path.Count(c => c == '/') <= 1)
                                        .Select(b => b.path + "." + b.propertyName);
                report.AppendLine($"  {name}: {clip.length:0.00} s, {bindings.Length} curves, root-ish position curves: {string.Join(" ", rootMoves)}");
                foreach (var b in bindings.Where(b => b.propertyName == "m_LocalPosition.y" && b.path.Count(c => c == '/') <= 1))
                {
                    var curve = AnimationUtility.GetEditorCurve(clip, b);
                    report.AppendLine($"    {b.path} y: {curve.Evaluate(0f):0.00} → {curve.Evaluate(clip.length):0.00}");
                }
            }
            else report.AppendLine("  ! no clip for " + name);
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
