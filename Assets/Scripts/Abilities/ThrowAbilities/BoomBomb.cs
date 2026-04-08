using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;

public class BoomBomb : ThrowAbility
{

    [Header("References")]
    public Transform playerCam;
    public Transform throwPoint;
    public GameObject bombPrefab;
    //public JimmyMove pm;

    [Header("Throwing")]
    public float throwForce;
    public float throwUpwardForce;

    [Header("Explosion")]
    public float explosionForce;
    public float explosionRadius;
    public LayerMask affectedLayers;

    // Private variables
    private bool canDetonate = false;
    private Bomb currentBoom;

    protected override void Awake()
    {
        base.Awake(); // assigns rb and pm in Ability.Awake()
    }

    protected override void OnKeyDown()
    {
        if (canDetonate)
        {
            Boom();
        }
        else
        {
            Throw();
        }
    }

    void Throw()
    {
        // instantiate object to throw
        GameObject bombInstance = PhotonNetwork.Instantiate(
            "Object/BoomBrick",
            throwPoint.position,
            playerCam.rotation);

        // get rigidbody component
        Bomb bombType = bombInstance.GetComponent<Bomb>();

        Vector3 forceToAdd = playerCam.forward * throwForce + transform.up * throwUpwardForce;

        //bombType.AddForce(forceToAdd, ForceMode.Impulse);
        Vector3 playerVel = Vector3.zero;
        if (pm != null && pm.rb != null) playerVel = pm.rb.linearVelocity;

        bombType.photonView.RPC("ThrowRPC", RpcTarget.All, forceToAdd + playerVel);

        currentBoom = bombType;

        canDetonate = true;
    }

    void Boom()
    {
        if (currentBoom == null) return;
        currentBoom.DetonateRPC();
        canDetonate = false;
    }
}
