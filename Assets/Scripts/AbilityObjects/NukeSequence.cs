using Photon.Pun;
using UnityEngine;

/// <summary>
/// Placed nuke: counts down with accelerating beeps, then blasts every rigidbody in a huge
/// radius. Any player standing next to it can disarm it with the Grab key.
/// The placing client owns the countdown; beeps, detonation and disarming are RPCs to all.
/// Attach to: Resources/Object/Nuke (with PhotonView).
/// </summary>
public class NukeSequence : MonoBehaviourPun
{
    [Header("Countdown")]
    [TunableParam("Countdown", 1f, 30f)]
    [SerializeField] private float countdownTime     = 5f;
    [SerializeField] private float fastBeepThreshold = 5f;

    [Header("Explosion")]
    [TunableParam("Blast Radius", 5f, 300f)]
    [SerializeField] private float     explosionRadius = 100f;
    [TunableParam("Blast Force", 100f, 20000f)]
    [SerializeField] private float     explosionForce  = 8000f;
    [SerializeField] private LayerMask affectedLayers;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip   beepSound;
    [SerializeField] private AudioClip   explosionSound;

    [Header("Disarm")]
    [TunableParam("Disarm Range", 0f, 10f)]
    [SerializeField] private float disarmRange = 3f;

    [Header("Visual Feedback")]
    [SerializeField] private GameObject explosionEffect;
    [SerializeField] private Light      warningLight;

    private float timeRemaining;
    private float nextBeepTime;
    private float power = 1f;
    private bool  isActive = true;

    private void Awake() => ObjectTunables.Apply(photonView); // Cheat values from the placer

    private void Start()
    {
        power         = SpawnData.Power(photonView);
        timeRemaining = countdownTime;
        nextBeepTime  = countdownTime;

        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        WorldFx.Configure3D(audioSource, 50f);
        if (warningLight != null) warningLight.enabled = false;
    }

    private void Update()
    {
        if (!isActive) return;

        if (photonView.IsMine) UpdateCountdown();
        CheckDisarm();
    }

    private void UpdateCountdown()
    {
        timeRemaining -= Time.deltaTime;

        if (timeRemaining <= nextBeepTime)
        {
            photonView.RPC(nameof(RPC_Beep), RpcTarget.All);
            nextBeepTime -= timeRemaining <= fastBeepThreshold ? 0.5f : 1f;
        }

        if (timeRemaining <= 0f)
            photonView.RPC(nameof(RPC_Detonate), RpcTarget.All);
    }

    private void CheckDisarm()
    {
        var local = Player.Local;
        if (local == null || PauseMenuManager.IsPaused) return;
        if (!Input.GetKeyDown(Keybinds.Get(InputAction.Grab))) return;
        if (Vector3.Distance(local.transform.position, transform.position) > disarmRange) return;

        photonView.RPC(nameof(RPC_Disarm), RpcTarget.All);
    }

    // ─── RPCs ─────────────────────────────────────────────────────────────────

    [PunRPC]
    private void RPC_Beep()
    {
        if (beepSound != null) audioSource.PlayOneShot(beepSound);
        if (warningLight == null) return;
        warningLight.enabled = true;
        CancelInvoke(nameof(LightOff));
        Invoke(nameof(LightOff), 0.1f);
    }

    private void LightOff() => warningLight.enabled = false;

    [PunRPC]
    private void RPC_Detonate()
    {
        if (!isActive) return;
        isActive = false;

        WorldFx.PlayClip(explosionSound, transform.position, explosionRadius);
        WorldFx.SpawnVfx(explosionEffect, transform.position);
        Fx.Explosion(transform.position, explosionRadius);
        // explosionForce is tuned as a continuous force; convert to one physics step of impulse
        WorldFx.Explode(transform.position, explosionRadius, explosionForce * power * Time.fixedDeltaTime, affectedLayers, sourceActor: photonView.OwnerActorNr);

        if (photonView.IsMine) PhotonNetwork.Destroy(gameObject);
    }

    [PunRPC]
    private void RPC_Disarm()
    {
        if (!isActive) return;
        isActive = false;
        Sfx.At(SfxKind.Pop, transform.position, 1f, 30f);
        Vfx.Burst(transform.position, new Color(0.5f, 1f, 0.5f), 20, 3f, 0.25f, 0.6f);
        if (photonView.IsMine) PhotonNetwork.Destroy(gameObject);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, explosionRadius);
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, disarmRange);
    }
}
