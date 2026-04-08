using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;

public class Rock : MonoBehaviour
{

    [Header("References")]
    public Transform throwSpot;
    public GameObject rockProjectile;
    public JimmyMove pm;
    public GameObject rockLocation;

    [Header("Modifiers")]
    public float initialThrowStength;
    public int numberOfRocks;


    PhotonView view;

    private GameObject throwingRock;
    private float throwStrength;
    private Queue<GameObject> rockList = new Queue<GameObject>();
/*
    void Start()
    {
        view = GetComponent<PhotonView>();
        if(view.IsMine)
            throwStrength = initialThrowStength;
    }

    void Update()
    {
        if (UIController.GameIsPaused) return;
        if (view.IsMine)
        {
            // When key is released, throw
            if (pm != null && Input.GetKeyUp(pm.rockKey))
                Throw();

            // When key is initially pressed, display rock being held
            else if (pm != null && Input.GetKeyDown(pm.rockKey))
                rockLocation.SetActive(true);

            // While holding key, charge throw
            else if (pm != null && Input.GetKey(pm.rockKey))
                Charge();
        }
        
    }

    void Charge()
    {
        // Increate charge of throw the more you hold the key
        throwStrength += Time.deltaTime * 10;
    }

    void Throw()
    {
        //view.RPC("PlayerRockThrow", RpcTarget.All);

        rockLocation.SetActive(false);
        throwingRock = Instantiate(rockProjectile, throwSpot.position, transform.rotation);

        // To limit spawn amount, destroy the oldest rock if too many
        if (rockList.Count == numberOfRocks)
        {
            Destroy(rockList.Dequeue());
        }
        rockList.Enqueue(throwingRock);

        // Then get it's rigidbody
        Rigidbody projectileRb = throwingRock.GetComponent<Rigidbody>();

        //RaycastHit hit;

        //if (Physics.Raycast(cam.position, cam.forward, out hit, 500f))
        //{
        //    forceDirection = (hit.point - attackPoint.position).normalized;
        //}

        // add force
        //Vector3 forceToAdd = forceDirection * throwForce + transform.up * throwUpwardForce;
        Vector3 forceToAdd = throwSpot.forward * throwStrength + throwSpot.up * throwStrength/10;
        projectileRb.AddForce(forceToAdd, ForceMode.Impulse);

        throwStrength = initialThrowStength;
    }

    /*[PunRPC]
    void PlayerRockThrow()
    {
        Debug.Log("Throws a rock");
    }*
*/
}
