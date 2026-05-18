using Photon.Pun;
using UnityEngine;

public class Nuke : TrapAbility
{
    // Overrides base EnterPlacementMode to disable the NukeSequence script on the hologram,
    // preventing it from triggering while the player is still in placement mode.
    protected override void EnterPlacementMode()
    {
        isPlacementMode = true;

        hologramObject = PhotonNetwork.Instantiate("Object/" + abilityName, Vector3.zero, Quaternion.identity);
        hologramObject.GetComponent<NukeSequence>().enabled = false;

        Renderer[] renderers = hologramObject.GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers)
        {
            Material[] mats = new Material[r.materials.Length];
            for (int i = 0; i < mats.Length; i++)
                mats[i] = hologramMaterial;
            r.materials = mats;
        }

        foreach (Collider col in hologramObject.GetComponentsInChildren<Collider>())
            col.enabled = false;

        if (hologramObject.TryGetComponent<Rigidbody>(out var holoRb))
            holoRb.isKinematic = true;
    }
}
