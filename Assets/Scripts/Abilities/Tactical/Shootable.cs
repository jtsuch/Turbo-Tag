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
    }

    private void Remove()
    {
        if (this != null && photonView.IsMine) PhotonNetwork.Destroy(gameObject);
    }
}
