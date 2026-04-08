using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

public class NukeSequence : MonoBehaviourPunCallbacks
{
    [Header("Countdown Settings")]
    [SerializeField] private float countdownTime = 5f;
    [SerializeField] private float fastBeepThreshold = 5f;
    
    [Header("Explosion Settings")]
    [SerializeField] private float explosionRadius = 100f;
    [SerializeField] private float explosionForce = 8000f;
    [SerializeField] private LayerMask affectedLayers;
    
    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip beepSound;
    [SerializeField] private AudioClip explosionSound;
    
    [Header("Disarm Settings")]
    [SerializeField] private float disarmRange = 3f;
    [SerializeField] private KeyCode disarmKey = KeyCode.F;
    
    [Header("Visual Feedback")]
    [SerializeField] private GameObject explosionEffect;
    [SerializeField] private Light warningLight;
    
    private float timeRemaining;
    private float nextBeepTime;
    private bool isActive = true;
    private Transform playerTransform;
    
    private void Start()
    {
        // Check if this object has a valid PhotonView
        if (photonView.ViewID == 0)
        {
            Debug.LogError("NukeSequence must be instantiated using PhotonNetwork.Instantiate()!");
            return;
        }
        
        // Only the master client controls the countdown
        if (PhotonNetwork.IsMasterClient)
        {
            timeRemaining = countdownTime;
            nextBeepTime = countdownTime;
        }
        
        // Find the local player
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            playerTransform = player.transform;
        }
        
        // Setup audio source if not assigned
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }
        
        audioSource.spatialBlend = 1f; // 3D sound
        audioSource.maxDistance = 50f;
    }
    
    private void Update()
    {
        if (!isActive || photonView.ViewID == 0) return;
        
        // Only master client updates the countdown
        if (PhotonNetwork.IsMasterClient)
        {
            UpdateCountdown();
        }
        
        // All clients can attempt to disarm
        CheckDisarmInput();
    }
    
    private void UpdateCountdown()
    {
        timeRemaining -= Time.deltaTime;
        
        // Determine beep interval
        float beepInterval = timeRemaining <= fastBeepThreshold ? 0.5f : 1f;
        
        // Check if it's time to beep
        if (timeRemaining <= nextBeepTime)
        {
            photonView.RPC("PlayBeep", RpcTarget.All);
            nextBeepTime -= beepInterval;
        }
        
        // Check for detonation
        if (timeRemaining <= 0f)
        {
            photonView.RPC("Detonate", RpcTarget.All);
        }
    }
    
    private void CheckDisarmInput()
    {
        if (playerTransform == null) return;
        
        float distance = Vector3.Distance(playerTransform.position, transform.position);
        
        if (distance <= disarmRange && Input.GetKeyDown(disarmKey))
        {
            // Call disarm on all clients
            photonView.RPC("Disarm", RpcTarget.All);
        }
    }
    
    [PunRPC]
    private void PlayBeep()
    {
        if (beepSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(beepSound);
        }
        
        // Flash warning light if available
        if (warningLight != null)
        {
            StartCoroutine(FlashLight());
        }
    }
    
    [PunRPC]
    private void Detonate()
    {
        if (!isActive) return;
        isActive = false;
        
        // Play explosion sound
        if (explosionSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(explosionSound);
        }
        
        // Spawn explosion effect
        if (explosionEffect != null)
        {
            Instantiate(explosionEffect, transform.position, Quaternion.identity);
        }
        
        // Apply explosion force to all rigidbodies in range
        Collider[] colliders = Physics.OverlapSphere(transform.position, explosionRadius, affectedLayers);
        
        foreach (Collider col in colliders)
        {
            Rigidbody rb = col.GetComponent<Rigidbody>();
            if (rb != null)
            {
                // Calculate force direction and distance
                Vector3 direction = col.transform.position - transform.position;
                float distance = direction.magnitude;
                
                // Apply force with falloff based on distance
                float forceFalloff = 1f - (distance / explosionRadius);
                rb.AddExplosionForce(explosionForce * forceFalloff, transform.position, explosionRadius);
            }
        }
        
        // Destroy the nuke object after a delay
        if (PhotonNetwork.IsMasterClient)
        {
            Destroy(gameObject, 2f);
        }
    }
    
    [PunRPC]
    private void Disarm()
    {
        if (!isActive) return;
        isActive = false;
        
        Debug.Log("Nuke disarmed!");
        
        // Play disarm sound/effect if you have one
        // audioSource.PlayOneShot(disarmSound);
        
        // Destroy the object
        if (PhotonNetwork.IsMasterClient)
        {
            PhotonNetwork.Destroy(gameObject);
        }
    }
    
    private System.Collections.IEnumerator FlashLight()
    {
        if (warningLight == null) yield break;
        
        warningLight.enabled = true;
        yield return new WaitForSeconds(0.1f);
        warningLight.enabled = false;
    }
    
    private void OnDrawGizmosSelected()
    {
        // Visualize explosion radius
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, explosionRadius);
        
        // Visualize disarm range
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, disarmRange);
    }
}