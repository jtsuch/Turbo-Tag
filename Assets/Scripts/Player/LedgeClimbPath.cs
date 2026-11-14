using UnityEngine;

/// <summary>
/// The shape of the ledge climb-up, measured from Justin's LedgeClimbUp clip (LedgeAnimationSetup writes it to
/// Resources/Player/LedgeClimbPath): how far along its forward and its upward travel the hips are at each moment, both
/// 0 → 1 over the climb (time 0 → 1). PlayerMovement moves the body along this path (up first, then over, exactly as
/// animated), and the clip's own hip travel is taken out, so the model rides on the body the way it was animated
/// instead of the body sliding in a straight line while the hips did their own thing.
/// </summary>
[CreateAssetMenu(menuName = "Turbo Tag/Ledge Climb Path")]
public class LedgeClimbPath : ScriptableObject
{
    [Tooltip("Share of the forward (toward the wall) travel done, over the climb's time (both 0–1).")]
    public AnimationCurve forward = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    [Tooltip("Share of the upward travel done, over the climb's time (both 0–1).")]
    public AnimationCurve up = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    private static LedgeClimbPath loaded;
    private static bool looked;

    /// <summary>The measured path, or null (then the climb is a straight line).</summary>
    public static LedgeClimbPath Current
    {
        get
        {
            if (!looked) { looked = true; loaded = Resources.Load<LedgeClimbPath>("Player/LedgeClimbPath"); }
            return loaded;
        }
    }
}
