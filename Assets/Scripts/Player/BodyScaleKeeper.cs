using UnityEngine;

/// <summary>
/// Keeps a resized penguin standing on its feet. <see cref="Player.ApplyBodyScale"/> scales the model roots (the
/// Armature and Jimmy) for size and stance, and their offsets from the player's root have to scale with them. The
/// Animator rewrites the Armature's position every frame with its normal-size value (0.35 m), which undid that: grown
/// penguins sank into the floor (31 cm at 2×) and shrunk ones floated (25 cm at 0.3×), crouching floated a little.
/// So after the Animator, each frame, this scales whatever position the root has been given. A value the Animator
/// wrote this frame is taken as the new unscaled pose; one it didn't touch keeps the last pose, so nothing compounds.
/// Runs early in LateUpdate so bone followers (cosmetics, name tags, flinches, the left arm aim) see the fixed pose.
/// Added to every player copy by <see cref="Player"/>.
/// </summary>
[DefaultExecutionOrder(-200)]
public class BodyScaleKeeper : MonoBehaviour
{
    private Transform[] roots;
    private Vector3[] pose, lastSet;
    private Vector3[] factor;

    /// <summary>Player: the model roots and their unscaled positions (once, in Awake).</summary>
    public void Init(Transform[] visualRoots, Vector3[] basePositions)
    {
        roots = visualRoots;
        pose = (Vector3[])basePositions.Clone();
        lastSet = new Vector3[roots.Length];
        factor = new Vector3[roots.Length];
        for (int i = 0; i < roots.Length; i++) { lastSet[i] = new Vector3(float.NaN, 0f, 0f); factor[i] = Vector3.one; }
    }

    /// <summary>Player: the scale each root's offset gets (size on every axis, the stance squash and bounce on the up axis).</summary>
    public void SetFactor(Vector3 f)
    {
        if (factor == null) return;
        for (int i = 0; i < factor.Length; i++) factor[i] = f;
        Apply();
    }

    private void LateUpdate() => Apply();

    private void Apply()
    {
        if (roots == null) return;
        for (int i = 0; i < roots.Length; i++)
        {
            var root = roots[i];
            if (root == null) continue;
            Vector3 now = root.localPosition;
            if ((now - lastSet[i]).sqrMagnitude > 1e-10f || float.IsNaN(lastSet[i].x)) pose[i] = float.IsNaN(lastSet[i].x) ? pose[i] : now;
            Vector3 want = Vector3.Scale(pose[i], factor[i]);
            root.localPosition = want;
            lastSet[i] = want;
        }
    }
}
