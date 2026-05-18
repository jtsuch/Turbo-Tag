using Photon.Pun;
using UnityEngine;

/// <summary>
/// Attach to every throwable-object prefab (Resources/Object/...).
/// ThrowAbility calls RPC_SetupHeld on the held object's own PhotonView so remote
/// clients can parent the object correctly, avoiding the "method found 2x" error that
/// occurs when the RPC lives on the player (which has multiple ThrowAbility components).
/// </summary>
public class HeldObjectSetup : MonoBehaviourPun
{
    [PunRPC]
    private void RPC_SetupHeld(int ownerViewID, string abilityName)
    {
        PhotonView ownerView = PhotonView.Find(ownerViewID);
        if (ownerView == null) return;

        foreach (var ability in ownerView.GetComponents<ThrowAbility>())
        {
            if (ability.abilityName == abilityName)
            {
                ability.AttachHeldObject(gameObject);
                return;
            }
        }
    }
}
