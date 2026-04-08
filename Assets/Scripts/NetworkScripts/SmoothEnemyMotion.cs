using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;

public class SmoothEnemyMotion : MonoBehaviour
{
    
    // Put this on remote players only
    private Vector3 networkedPosition;
    private Quaternion networkedRotation;
    public PhotonView playerView;

    void Update()
    {
        if (!playerView.IsMine) // Only for remote players
        {
            // Smoothly interpolate from current position to networked position
            transform.position = Vector3.Lerp(transform.position, networkedPosition, Time.deltaTime * 10f);
            transform.rotation = Quaternion.Lerp(transform.rotation, networkedRotation, Time.deltaTime * 10f);
        }
    }

    public void SetNetworkState(Vector3 pos, Quaternion rot)
    {
        networkedPosition = pos;
        networkedRotation = rot;
    }

    // When you receive a new position/rotation from the network:
    /*void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsReading)
        {
            networkedPosition = (Vector3)stream.ReceiveNext();
            networkedRotation = (Quaternion)stream.ReceiveNext();
        }
    }*/
}
