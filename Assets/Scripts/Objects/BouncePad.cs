using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class BouncePad : MonoBehaviour
{
    public float bounceForce = 40f;
    public TextMeshPro strengthText;

    void Start()
    {
        if (strengthText != null)
            strengthText.text = bounceForce.ToString("F0");
    }
    private void OnTriggerEnter(Collider other)
    {
        Rigidbody rb = other.attachedRigidbody;
        if (rb != null)
        {
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
            rb.AddForce(transform.forward * bounceForce, ForceMode.Impulse);
        }
    }
}