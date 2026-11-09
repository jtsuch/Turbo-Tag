using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
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

    [Header("Lightning (Rock)")]
    [Tooltip("Chance that hitting a player calls down lightning (0 = never). It chains to everyone within " +
             "chainRange of anyone struck, and kills them all.")]
    [TunableParam("Lightning Chance", 0f, 1f)]
    [SerializeField] private float lightningChance;
    [SerializeField] private float chainRange = 10f;

    private bool landed, rolledLightning;

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
        TryLightning(collision);
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

    // ─── Lightning ────────────────────────────────────────────────────────────

    // The thrower's copy, on its first player hit: the roll; then the chain is worked out here and sent to everyone
    private void TryLightning(Collision collision)
    {
        if (lightningChance <= 0f || rolledLightning || !IsAuthority) return;
        var first = collision.collider.GetComponentInParent<Player>();
        if (first == null || first.photonView.OwnerActorNr == photonView.OwnerActorNr || first.IsCaught || !RealContact(collision)) return;
        rolledLightning = true;
        if (Random.value >= lightningChance) return;

        // Everyone within reach of anyone already struck, round and round until nobody new is in reach
        var struck = new List<Player> { first };
        var links = new List<int> { -1 }; // Who each was struck from (the arc drawn between them)
        for (int i = 0; i < struck.Count; i++)
            foreach (var p in Player.All)
            {
                if (p == null || struck.Contains(p) || p.IsCaught || p.IsDownAfterTag || p.Role == PlayerRole.Ghost) continue;
                if (p.photonView.OwnerActorNr == photonView.OwnerActorNr) continue; // Never the thrower
                if (Vector3.Distance(p.transform.position, struck[i].transform.position) > chainRange) continue;
                struck.Add(p);
                links.Add(i);
            }

        var actors = new int[struck.Count];
        var points = new Vector3[struck.Count];
        for (int i = 0; i < struck.Count; i++) { actors[i] = struck[i].ActorNumber; points[i] = AbilityDamage.Chest(struck[i]); }
        photonView.RPC(nameof(RPC_Lightning), RpcTarget.All, actors, points, links.ToArray());

        // Gun modes: a kill by the thrower (the mode's rules still decide who can be hurt)
        if (AbilityDamage.Active)
            foreach (var p in struck) AbilityDamage.Hit(p, 9999f, "Lightning", Vector3.down, trap: false);
    }

    [PunRPC]
    private void RPC_Lightning(int[] actors, Vector3[] points, int[] links)
    {
        Lightning.Strike(points, links);
        // Modes without health: anyone struck goes as if they'd fallen off the level (hunters back to a spawn,
        // hiders caught)
        var local = Player.Local;
        if (local == null || AbilityDamage.Active || System.Array.IndexOf(actors, local.ActorNumber) < 0) return;
        if (Spawner.Instance != null) Spawner.Instance.LocalPlayerFell();
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
