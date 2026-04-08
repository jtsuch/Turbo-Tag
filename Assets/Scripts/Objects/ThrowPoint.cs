using UnityEngine;

public class ThrowPoint : MonoBehaviour
{
    private Transform playerCam;

    private void Start()
    {
        playerCam = GetComponentInParent<Transform>();
    }

    // Ensures that the ThrowPoint is in the correct position via LateUpdate
    void LateUpdate()
    {
        transform.position = playerCam.position;// + new Vector3(-0.5179996f, -0.589f, 0.957f); 
        transform.rotation = playerCam.rotation;
    }
}
