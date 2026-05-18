using UnityEngine;
using Photon.Pun;

/// <summary>
/// Detects physical collisions between the local hunter and hider players during the Active
/// phase, then routes a tag request through the master client so GameModeManager can process
/// it authoritatively. Attach to: ThePlayer prefab alongside the other player components.
/// </summary>
[RequireComponent(typeof(Player))]
[RequireComponent(typeof(PhotonView))]
public class HunterTagger : MonoBehaviour
{
    private Player     player;
    private PhotonView view;

    private void Awake()
    {
        player = GetComponent<Player>();
        view   = GetComponent<PhotonView>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        TryTag(collision.collider);
    }

    private void OnTriggerEnter(Collider other)
    {
        TryTag(other);
    }

    private void TryTag(Collider col)
    {
        if (!view.IsMine)     return;
        if (!player.isHunter) return;
        if (GameModeManager.Instance == null) return;
        if (GameModeManager.Instance.CurrentPhase != GameModeManager.MatchPhase.Active) return;

        // Walk up the hierarchy — collision may register on a child collider
        Player other = col.GetComponentInParent<Player>();
        if (other == null || other == player) return;

        PhotonView otherView = other.GetComponent<PhotonView>();
        if (otherView == null || otherView.Owner == null) return;

        // Use the authoritative GameModeManager set instead of other.isHunter,
        // which is never populated on remote player instances.
        if (!GameModeManager.Instance.IsHider(otherView.Owner.ActorNumber)) return;

        GameModeManager.Instance.photonView.RPC(
            nameof(GameModeManager.RPC_RequestTag),
            RpcTarget.MasterClient,
            otherView.Owner.ActorNumber
        );
    }
}
