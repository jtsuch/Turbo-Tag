using UnityEngine;

/// <summary>
/// Simple procedural gun animation on top of a gun model's resting pose: a kick on each shot, and a reload
/// (the gun tips down and rolls; a child named "Magazine" drops out and slides back in, or for guns without
/// one the gun tilts up to check the chamber). Call <see cref="Kick"/> per shot and <see cref="Apply"/> every
/// frame after the model is placed. Used for first-person, third-person and the killcam's view model.
/// </summary>
public class GunViewModel : MonoBehaviour
{
    private Vector3    basePosition;
    private Quaternion baseRotation;
    private Transform  magazine;
    private Vector3    magazineBase;
    private Renderer[] magazineRenderers;
    private bool       captured;
    private float      kick;

    /// <summary>Remembers the current local pose as the resting pose (call once the model is placed).</summary>
    public void Capture()
    {
        basePosition = transform.localPosition;
        baseRotation = transform.localRotation;
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == "Magazine") { magazine = t; break; }
        if (magazine != null)
        {
            magazineBase      = magazine.localPosition;
            magazineRenderers = magazine.GetComponentsInChildren<Renderer>(true);
        }
        captured = true;
    }

    /// <summary>Resting pose for models placed by code each frame (third-person guns on other guns' hands).</summary>
    public void SetBase(Vector3 localPosition, Quaternion localRotation)
    {
        if (!captured) Capture();
        basePosition = localPosition;
        baseRotation = localRotation;
    }

    public void Kick(float amount = 1f) => kick = Mathf.Min(1.5f, kick + amount);

    /// <summary>A knife swing: a quick slash across and forward, then back to rest.</summary>
    public void Slash() => slashAt = Time.time;
    private float slashAt = -10f;
    private const float SlashTime = 0.32f;

    /// <summary>Poses the gun: <paramref name="reload"/> is reload progress 0–1, or a negative number when not reloading.</summary>
    public void Apply(float reload)
    {
        // Offsets are in metres; local units shrink or grow with the parent's scale (bones, Blender scale)
        float scale = transform.parent != null ? 1f / Mathf.Max(0.0001f, transform.parent.lossyScale.x) : 1f;
        if (!captured) Capture();
        kick = Mathf.MoveTowards(kick, 0f, Time.deltaTime * 8f);

        // Reload envelope: tip in quickly, hold, tip back
        float r = reload >= 0f ? Envelope(reload, 0.15f, 0.85f) : 0f;
        bool  hasMag = magazine != null;

        Vector3    offset = new(0f, -0.08f * r, -0.06f * kick);
        Quaternion turn   = hasMag
            ? Quaternion.Euler(18f * r - 6f * kick, 0f, -28f * r)     // Tip down and roll to show the magazine well
            : Quaternion.Euler(-35f * r - 6f * kick, 0f, -15f * r);   // Tilt up to check the chamber
        // Slash: wind back a touch (first 25%), sweep forward and across to the left, ease back
        float s = (Time.time - slashAt) / SlashTime;
        if (s >= 0f && s < 1f)
        {
            float wind  = s < 0.25f ? Smooth(s / 0.25f) : 1f - Smooth((s - 0.25f) / 0.75f);
            float sweep = s < 0.25f ? -0.3f * Smooth(s / 0.25f) : Mathf.Sin(Mathf.PI * Mathf.Clamp01((s - 0.25f) / 0.6f));
            offset += new Vector3(-0.22f * sweep, 0.04f * wind, 0.18f * Mathf.Max(0f, sweep));
            turn   *= Quaternion.Euler(10f * sweep, -55f * sweep, -35f * sweep);
        }
        transform.localPosition = basePosition + offset * scale;
        transform.localRotation = baseRotation * turn;

        if (!hasMag) return;

        // Magazine: out (0.2–0.4), away (0.4–0.6), back in (0.6–0.8)
        float drop = reload >= 0f
            ? (reload < 0.2f ? 0f : reload < 0.4f ? (reload - 0.2f) / 0.2f : reload < 0.6f ? 1f : reload < 0.8f ? 1f - (reload - 0.6f) / 0.2f : 0f)
            : 0f;
        // A third of the gun's length straight down from the gun, in the magazine's parent's units
        Transform parent = magazine.parent;
        float metres = 0.3f * transform.lossyScale.z;
        Vector3 down = parent != null
            ? parent.InverseTransformDirection(-transform.up) * (metres / Mathf.Max(0.0001f, parent.lossyScale.x))
            : Vector3.down * metres;
        magazine.localPosition = magazineBase + down * Smooth(drop);
        bool visible = !(reload >= 0.38f && reload < 0.62f);
        foreach (var renderer in magazineRenderers) if (renderer != null) renderer.enabled = visible;
    }

    private static float Envelope(float t, float rise, float fall) =>
        t < rise ? Smooth(t / rise) : t > fall ? Smooth((1f - t) / (1f - fall)) : 1f;

    private static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
}
