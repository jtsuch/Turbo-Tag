using System;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// An ability object that bullets can destroy (Recon Bolt, Drone, Turret, Ice Wall). The shooter's client reports
/// the hit (PlayerGun stops the bullet on its collider) and the object's owner keeps the health; at zero everyone
/// sees it break and the owner removes it. Shots from the owner's own team do nothing (the bullet still stops).
/// Needs a PhotonView on the same object and a collider somewhere on it.
/// </summary>
public class Shootable : MonoBehaviourPun
{
    [Tooltip("Damage it takes before breaking.")]
    public float health = 50f;

    /// <summary>Above 0: a shot passing within this distance of the object's centre hits it, even with no solid
    /// collider (small traps with only trigger colliders: Ice Trap, Mouse Trap, the Trip Wire's box). PlayerGun.Trace.</summary>
    public float hitRadius;

    /// <summary>Every shootable hit by distance (<see cref="hitRadius"/> above 0).</summary>
    public static readonly System.Collections.Generic.List<Shootable> ByDistance = new();

    /// <summary>Where a shot aims for it (the centre of its renderers, or its position).</summary>
    public Vector3 Centre
    {
        get
        {
            var r = GetComponentInChildren<Renderer>();
            return r != null && r.enabled ? r.bounds.center : transform.position;
        }
    }

    private void OnEnable()  { if (hitRadius > 0f && !ByDistance.Contains(this)) ByDistance.Add(this); }
    private void OnDisable() => ByDistance.Remove(this);

    /// <summary>Makes <paramref name="go"/> shootable by distance with <paramref name="hp"/> health (adds the
    /// component if it's missing). Call from Awake, before any RPC goes out on its PhotonView.</summary>
    public static Shootable Ensure(GameObject go, float hp, float radius)
    {
        if (!go.TryGetComponent(out Shootable s)) s = go.AddComponent<Shootable>();
        s.health = hp;
        s.hitRadius = radius;
        if (!ByDistance.Contains(s)) ByDistance.Add(s);
        // Its PhotonView may have listed its RPC receivers already: add this one
        if (go.TryGetComponent(out PhotonView view)) view.RefreshRpcMonoBehaviourCache();
        return s;
    }

    /// <summary>Owner: it broke (before it's removed).</summary>
    public event Action Broken;

    private bool broken;

    /// <summary>The local shooter hit it for <paramref name="amount"/>.</summary>
    public void Hit(float amount)
    {
        if (broken) return;
        var local = Player.Local;
        if (local != null && local.IsTeammateOf(photonView.OwnerActorNr)) return; // Your team's: shots don't hurt it
        photonView.RPC(nameof(RPC_ShotHit), photonView.Owner, amount);
        DamageNumber.Show(transform.position + Vector3.up * 0.5f, amount, false);
    }

    [PunRPC]
    private void RPC_ShotHit(float amount)
    {
        if (!photonView.IsMine || broken) return;
        health -= amount;
        if (health > 0f) return;
        broken = true;
        photonView.RPC(nameof(RPC_Broke), RpcTarget.All, transform.position);
        Broken?.Invoke();
        Invoke(nameof(Remove), 0.2f);
    }

    [PunRPC]
    private void RPC_Broke(Vector3 at)
    {
        broken = true;
        Sfx.At(SfxKind.Snap, at, 1f, 40f, 0.8f);
        Vfx.Burst(at, new Color(0.9f, 0.9f, 1f), 24, 6f, 0.25f, 0.6f);
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        ByDistance.Remove(this);
    }

    private void Remove()
    {
        if (this != null && photonView.IsMine) PhotonNetwork.Destroy(gameObject);
    }
}
