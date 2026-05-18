using Photon.Pun;
using UnityEngine;

/// <summary>
/// Auto-added to the player by ThrowAbility.Awake — exactly one instance per player.
/// Provides unambiguous RPC endpoints for held-object setup and throw-force replication.
/// Keeping these RPCs on a dedicated single component avoids the "method found Nx" error
/// that occurs when they live directly on ThrowAbility (of which multiple exist per player).
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class ThrowDispatcher : MonoBehaviour
{
    // Called on remote clients: parents the held object to the correct ThrowAbility's throwOrigin.
    [PunRPC]
    private void RPC_AttachHeld(int heldViewID, string abilityName)
    {
        PhotonView heldView = PhotonView.Find(heldViewID);
        if (heldView == null) return;

        foreach (var ability in GetComponents<ThrowAbility>())
        {
            if (ability.abilityName == abilityName)
            {
                ability.AttachHeldObject(heldView.gameObject);
                return;
            }
        }
    }

    // Called on remote clients: applies the throw impulse so the projectile flies on every screen.
    [PunRPC]
    private void RPC_ApplyThrowForce(int thrownViewID, Vector3 direction, float force)
    {
        PhotonView thrownView = PhotonView.Find(thrownViewID);
        if (thrownView == null) return;

        if (thrownView.TryGetComponent<Rigidbody>(out var rb))
            rb.AddForce(direction * force, ForceMode.Impulse);
    }
}
