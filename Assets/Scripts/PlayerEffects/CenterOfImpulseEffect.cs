using UnityEngine;
using Photon.Pun;

/// <summary>
/// The affected player emits a continuous repulsion field for the effect duration.
/// A networked RepulseField object is spawned at the player's position and tracked
/// each LateUpdate.  All remote clients push their own local player away from it.
/// The owning player is never affected.
/// </summary>
public class CenterOfImpulseEffect : PlayerEffect
{
    private GameObject repulseFieldObj;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;

        repulseFieldObj = PhotonNetwork.Instantiate(
            "Object/RepulseField",
            player.transform.position,
            Quaternion.identity);
    }

    private void LateUpdate()
    {
        if (repulseFieldObj != null && player != null)
            repulseFieldObj.transform.position = player.transform.position;
    }

    protected override void OnEffectEnd()
    {
        if (repulseFieldObj != null)
        {
            PhotonNetwork.Destroy(repulseFieldObj);
            repulseFieldObj = null;
        }
    }
}
