using UnityEngine;

public class BoxObject : MonoBehaviour
{
    [Header("Audio")]
    [SerializeField] private AudioClip impactSound;
    [SerializeField] private float minImpactSpeed = 2f;  // m/s — filters out gentle grazes
    [SerializeField] private float audioRadius    = 15f;
    [SerializeField] private float audioVolume    = 1f;

    void OnCollisionEnter(Collision collision)
    {
        if (impactSound == null) return;
        if (collision.relativeVelocity.magnitude < minImpactSpeed) return;

        GameObject audioObj = new("BoxImpactAudio");
        audioObj.transform.position = transform.position;
        AudioSource src = audioObj.AddComponent<AudioSource>();
        src.clip         = impactSound;
        src.spatialBlend = 1f;
        src.rolloffMode  = AudioRolloffMode.Linear;
        src.minDistance  = 1f;
        src.maxDistance  = audioRadius;
        src.volume       = audioVolume;
        src.Play();
        Destroy(audioObj, impactSound.length + 0.1f);
    }
}
