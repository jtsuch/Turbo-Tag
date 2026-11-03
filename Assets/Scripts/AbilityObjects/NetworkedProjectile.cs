using Photon.Pun;
using UnityEngine;

/// <summary>
/// Base for objects thrown by a ThrowAbility. On every client, reads the spawn data passed to
/// PhotonNetwork.Instantiate, ignores collisions with the thrower and applies the launch
/// impulse — so the projectile flies the same everywhere without follow-up RPCs.
///
/// Gameplay decisions (detonate, stick…) are made by the owning client
/// (<see cref="IsAuthority"/>) and broadcast with RPCs; effects then run on every client.
/// Prefab needs: Rigidbody, Collider, PhotonView and exactly one NetworkedProjectile subclass.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public abstract class NetworkedProjectile : MonoBehaviourPun, IPunInstantiateMagicCallback
{
    protected Rigidbody Body { get; private set; }

    /// <summary>Strength multiplier from the thrower's "Power" tunable (1 = normal).</summary>
    protected float Power { get; private set; } = 1f;

    /// <summary>The thrower's player object, if it still exists.</summary>
    protected PhotonView Thrower { get; private set; }

    /// <summary>True on the client that threw this object.</summary>
    protected bool IsAuthority => photonView.IsMine;

    protected virtual void Awake()
    {
        Body = GetComponent<Rigidbody>();
        // Fast throws could step straight through a player between physics steps. Speculative detection
        // also catches kinematic bodies (other players' copies are kinematic).
        Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        ObjectTunables.Apply(photonView); // Cheat values from the thrower, before anything reads them
    }

    // ─── Launch shape (also read from the prefab by ThrowAbility for the hand and the aim preview) ───

    /// <summary>Rotation relative to the throw direction, in hand and when launched (1 = as modelled).</summary>
    public virtual Quaternion LaunchRotation => Quaternion.identity;

    /// <summary>The velocity this object actually leaves the hand with, from the charged throw.</summary>
    public virtual Vector3 LaunchVelocity(Vector3 thrown) => thrown;

    /// <summary>Fraction of normal gravity on this object: 1 falls normally, 0 floats (the aim preview matches).</summary>
    public virtual float GravityScale => 1f;

    public void OnPhotonInstantiate(PhotonMessageInfo info)
    {
        Power   = SpawnData.Power(photonView);
        Thrower = SpawnData.Owner(photonView);

        IgnoreThrowerCollisions();
        // The launch value is a velocity: every throwable flies the arc the aim preview shows, whatever its mass
        Body.useGravity = Mathf.Approximately(GravityScale, 1f); // Other scales are applied in FixedUpdate
        Body.AddForce(LaunchVelocity(SpawnData.Impulse(photonView)), ForceMode.VelocityChange);
        OnLaunched();
    }

    // Partial gravity (frisbee, GravBall). Runs on every client, like the launch
    protected virtual void FixedUpdate()
    {
        if (Body == null || Body.isKinematic || Body.useGravity) return;
        float scale = GravityScale;
        if (scale > 0f) Body.AddForce(Physics.gravity * scale, ForceMode.Acceleration);
    }

    /// <summary>Called on every client right after launch.</summary>
    protected virtual void OnLaunched() { }

    private void IgnoreThrowerCollisions()
    {
        if (Thrower == null) return;

        var own = GetComponentsInChildren<Collider>();
        foreach (var other in Thrower.GetComponentsInChildren<Collider>())
            foreach (var mine in own)
                Physics.IgnoreCollision(other, mine);
    }

    /// <summary>Hides and freezes the object so it never lingers while the destroy propagates.</summary>
    protected void HideAndFreeze()
    {
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        if (Body != null)
        {
            Body.linearVelocity = Vector3.zero;
            Body.isKinematic    = true;
        }
    }

    /// <summary>Authority only: removes the object on every client.</summary>
    protected void DestroyNetworked()
    {
        if (IsAuthority && this != null) PhotonNetwork.Destroy(gameObject);
    }
}
