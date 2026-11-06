using UnityEngine;

/// <summary>
/// A shot body jolts: the chest snaps back along the shot and springs back (the head too, harder for a
/// headshot), layered on top of the animation each frame. Remote bodies only (your own body is hidden from you).
/// Triggered on every screen by <see cref="PlayerHealth"/>'s hit reaction. Added to every player copy by Player.
/// </summary>
[RequireComponent(typeof(Player))]
public class HitFlinch : MonoBehaviour
{
    private const float Stiffness = 180f; // Spring back
    private const float Damping   = 14f;

    private Player    player;
    private Transform chest, head;
    private Vector3   axis = Vector3.right;
    private float     angle, velocity;        // Chest lean (degrees) and its rate
    private float     headAngle, headVelocity;

    private void Awake() => player = GetComponent<Player>();

    /// <summary>A hit along <paramref name="direction"/> for <paramref name="damage"/>.</summary>
    public void Flinch(Vector3 direction, float damage, bool headshot)
    {
        if (player.IsLocal) return;
        if (chest == null) FindBones();
        Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (flat.sqrMagnitude < 0.0001f) flat = transform.forward;
        axis = Vector3.Cross(Vector3.up, flat.normalized); // Tips the top of the body along the shot

        float k = Mathf.Clamp01(damage / 50f);
        velocity     += Mathf.Lerp(250f, 700f, k);
        headVelocity += headshot ? 1100f : Mathf.Lerp(150f, 400f, k);
    }

    private void FindBones()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Chest") chest = t;
            else if (t.name == "Head") head = t;
        }
    }

    // After the Animator has posed the body
    private void LateUpdate()
    {
        if (player.IsLocal || (Mathf.Abs(angle) < 0.05f && Mathf.Abs(velocity) < 0.5f && Mathf.Abs(headAngle) < 0.05f && Mathf.Abs(headVelocity) < 0.5f))
            return;

        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        Spring(ref angle, ref velocity, dt);
        Spring(ref headAngle, ref headVelocity, dt);
        angle     = Mathf.Clamp(angle, -35f, 35f);
        headAngle = Mathf.Clamp(headAngle, -50f, 50f);

        if (chest != null) chest.rotation = Quaternion.AngleAxis(angle, axis) * chest.rotation;
        if (head  != null) head.rotation  = Quaternion.AngleAxis(headAngle, axis) * head.rotation;
    }

    private static void Spring(ref float x, ref float v, float dt)
    {
        v += (-Stiffness * x - Damping * v) * dt;
        x += v * dt;
    }
}
