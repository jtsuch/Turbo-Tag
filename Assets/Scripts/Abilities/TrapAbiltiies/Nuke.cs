using Photon.Pun;
using UnityEngine;

public class Nuke : TrapAbility
{
    protected PhotonView view;

    private void Start()
    {
        view = GetComponent<PhotonView>();
        isLocalPlayer = view.IsMine;
    }

    // Overriding this method in order to specifically disable the nuke's script while a hologram
    protected override void EnterPlacementMode()
    {
        isPlacementMode = true;
        
        // Create hologram object
        hologramObject = PhotonNetwork.Instantiate("Object/"+abilityName, new(0, 0, 0), Quaternion.identity);
        hologramObject.GetComponent<NukeSequence>().enabled = false;
        // Apply hologram material to all renderers
        Renderer[] renderers = hologramObject.GetComponentsInChildren<Renderer>();
        foreach (Renderer renderer in renderers)
        {
            Material[] mats = new Material[renderer.materials.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                mats[i] = hologramMaterial;
            }
            renderer.materials = mats;
        }
        
        // Disable colliders on hologram
        Collider[] colliders = hologramObject.GetComponentsInChildren<Collider>();
        foreach (Collider col in colliders)
        {
            col.enabled = false;
        }
        
        // Disable any rigidbodies
        Rigidbody rb = hologramObject.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
        }
    }
}