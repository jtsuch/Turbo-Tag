using System.Collections;
using UnityEngine;

/// <summary>
/// A plain physical throwable (rock, frisbee): launched like every projectile, then bounces around
/// and is cleaned up after <see cref="lifetime"/> seconds. Optional: low gravity, a bouncy surface,
/// and coming to rest the first time it lands on the ground (frisbee: floats, bounces off walls and
/// ceilings, stops on the floor).
/// Attach to: throwable prefabs in Resources/Object that need no special behaviour.
/// </summary>
public class SimpleProjectile : NetworkedProjectile
{
    [Tooltip("Seconds before the object is removed (0 = never).")]
    [TunableParam("Lifetime", 0f, 60f)]
    [SerializeField] private float lifetime = 15f;

    [Tooltip("Turns the object relative to the throw direction when launched (e.g. lay a frisbee flat).")]
    [SerializeField] private Vector3 launchRotation;

    [Tooltip("Spin around the object's own up axis when launched (degrees per second; frisbee-style).")]
    [TunableParam("Spin Speed", 0f, 2000f)]
    [SerializeField] private float spinSpeed;

    [Header("Flight")]
    [Tooltip("Fraction of normal gravity (1 = rock, low = floats).")]
    [TunableParam("Gravity", 0f, 1f)]
    [SerializeField] private float gravityScale = 1f;
    [Tooltip("Bounciness off walls and ceilings (0–1). Below 0 keeps the collider's own material.")]
    [SerializeField] private float bounciness = -1f;
    [Tooltip("Stops dead the first time it touches ground (a surface facing up).")]
    [SerializeField] private bool stopOnGround;

    private bool landed;

    public override Quaternion LaunchRotation => Quaternion.Euler(launchRotation);
    public override float      GravityScale   => landed ? 0f : gravityScale;

    protected override void Awake()
    {
        base.Awake();
        if (bounciness < 0f) return;
        var bouncy = new PhysicsMaterial("Bouncy")
        {
            bounciness      = bounciness,
            dynamicFriction = 0.1f,
            staticFriction  = 0.1f,
            bounceCombine   = PhysicsMaterialCombine.Maximum,
            frictionCombine = PhysicsMaterialCombine.Minimum,
        };
        foreach (var col in GetComponentsInChildren<Collider>()) col.sharedMaterial = bouncy;
    }

    // Each client lands its own copy; the floor is the same everywhere, so they agree
    private void OnCollisionEnter(Collision collision)
    {
        if (!stopOnGround || landed) return;
        foreach (var contact in collision.contacts)
        {
            if (contact.normal.y < 0.7f) continue; // Walls and ceilings: bounce
            landed = true;
            Body.linearVelocity  = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.isKinematic     = true;
            return;
        }
    }

    protected override void OnLaunched()
    {
        // Same on every client, so the object looks the same everywhere
        if (launchRotation != Vector3.zero) Body.rotation = Body.rotation * LaunchRotation;
        if (spinSpeed != 0f) Body.angularVelocity = Body.rotation * Vector3.up * (spinSpeed * Mathf.Deg2Rad);

        if (IsAuthority && lifetime > 0f) StartCoroutine(Expire());
    }

    private IEnumerator Expire()
    {
        yield return new WaitForSeconds(lifetime);
        DestroyNetworked();
    }
}
