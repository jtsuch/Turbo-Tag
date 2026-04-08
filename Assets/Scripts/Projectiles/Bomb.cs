using UnityEngine;
using Photon.Pun;

public class Bomb : MonoBehaviourPun
{
    public float explosionRadius = 5f;
    public float explosionForce = 1200f;
    public LayerMask affectedLayers;

    private Rigidbody rb;
    private PhotonView view;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        view = GetComponent<PhotonView>();
    }

    [PunRPC]
    public void ThrowRPC(Vector3 force)
    {
        if( rb == null) return;
        rb.AddForce(force, ForceMode.Impulse);
    }

    public void DetonateRPC()
    {
        photonView.RPC("Detonate", RpcTarget.All);
        PhotonNetwork.Destroy(gameObject);
    }

    [PunRPC]
    public void Detonate()
    {
        Collider[] colliders = Physics.OverlapSphere(
            transform.position,
            explosionRadius,
            affectedLayers);

        foreach (Collider nearby in colliders)
        {
            Rigidbody nearbyRb = nearby.attachedRigidbody;
            if (nearbyRb != null)
            {
                nearbyRb.AddExplosionForce(
                    explosionForce,
                    transform.position,
                    explosionRadius,
                    1.0f, ForceMode.Impulse);
            }
        }
        //if((view.IsMine || PhotonNetwork.IsMasterClient) && gameObject != null) 
        //   PhotonNetwork.Destroy(gameObject);
    }
}