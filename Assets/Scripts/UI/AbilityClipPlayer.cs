using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plays an ability clip: a sheet of 8 × 6 frames (Resources/AbilityClips/{Id}, filmed by the clip recorder) shown
/// frame by frame on a RawImage at 15 fps, looping. <see cref="Has"/> says whether an ability has a clip yet.
/// </summary>
[RequireComponent(typeof(RawImage))]
public class AbilityClipPlayer : MonoBehaviour
{
    private const int Columns = 8, Rows = 6, Fps = 15;

    private RawImage image;
    private float started;

    public static Texture2D Load(string abilityId) => Resources.Load<Texture2D>("AbilityClips/" + abilityId);

    public static bool Has(string abilityId) => Load(abilityId) != null;

    /// <summary>Shows <paramref name="abilityId"/>'s clip (false if it has none).</summary>
    public bool Play(string abilityId)
    {
        image ??= GetComponent<RawImage>();
        var sheet = Load(abilityId);
        image.texture = sheet;
        image.enabled = sheet != null;
        started = Time.unscaledTime;
        Update();
        return sheet != null;
    }

    private void Update()
    {
        if (image == null || image.texture == null) return;
        int frame = Mathf.FloorToInt((Time.unscaledTime - started) * Fps) % (Columns * Rows);
        int col = frame % Columns, row = frame / Columns;
        image.uvRect = new Rect(col / (float)Columns, 1f - (row + 1) / (float)Rows, 1f / Columns, 1f / Rows);
    }
}
