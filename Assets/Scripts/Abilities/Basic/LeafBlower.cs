using Photon.Pun;
using UnityEngine;

/// <summary>
/// Hold to blow: pushes the player directly away from where they're looking, hard enough to lift
/// them off the ground when looking down. The leaf blower is held while blowing, in the LEFT hand (abilities use the
/// left, guns the right): a first-person model under the camera, mirrored to the left at start, and a third-person
/// model held at the left hand (raised by <see cref="RaisesArm"/>, pointed where the player aims). The blowing flag
/// is synced so others see it.
/// </summary>
[DefaultExecutionOrder(100)] // After PlayerAnimatorController has posed the left arm
[AbilityInfo("Hold to blast air: it pushes you the opposite way you look. Look down to hover and fly.")]
public class LeafBlower : BasicAbility, IPunObservable
{
    [Header("References")]
    [Tooltip("The camera (look direction).")]
    [SerializeField] private Transform  cam;
    [Tooltip("Leaf blower shown to the owner, under the camera.")]
    [SerializeField] private GameObject firstPersonModel;
    [Tooltip("Leaf blower in the right hand, seen by everyone else.")]
    [SerializeField] private GameObject thirdPersonModel;

    [Header("Blow")]
    [Tooltip("Push away from the look direction (m/s²). Gravity is 40, so above that lifts off when looking straight down.")]
    [TunableParam("Thrust", 5f, 200f)]
    [SerializeField] private float thrust = 58f;

    [Header("Effects")]
    [Tooltip("Looping blower sound (optional).")]
    [SerializeField] private AudioClip blowSFX;
    [SerializeField] private float     audioRadius = 25f;
    [Tooltip("Air effect at the nozzle, toggled while blowing (optional).")]
    [SerializeField] private GameObject airVFX;

    private bool        held;
    private bool        blowing;     // Owner: from input; others: synced
    private AudioSource blowAudio;

    public override bool RaisesArm => enabled && blowing;

    protected override void Awake()
    {
        base.Awake();
        if (!GetComponent<PhotonView>().ObservedComponents.Contains(this))
            GetComponent<PhotonView>().ObservedComponents.Add(this);
        ShowBlower(false);
        // Into the left hand: the first-person model mirrors to the left of the view
        if (firstPersonModel != null)
        {
            var t = firstPersonModel.transform;
            Vector3 p = t.localPosition;
            Quaternion r = t.localRotation;
            if (p.x > 0f) { t.localPosition = new Vector3(-p.x, p.y, p.z); t.localRotation = new Quaternion(r.x, -r.y, -r.z, r.w); }
        }
    }

    protected override void OnPress()
    {
        if (CurrentDuration > 0.01f) held = true;
    }

    protected override void OnRelease() => Deactivate();

    protected override void Deactivate()
    {
        held     = false;
        IsActive = false;
        blowing  = false;
        ShowBlower(false);
    }

    protected override void FixedUpdate()
    {
        if (IsLocal)
        {
            IsActive = held && CurrentDuration > 0.01f;
            blowing  = IsActive;
            if (blowing && cam != null)
            {
                Owner.NotePushed(); // An outside force: movement mustn't brake it away
                Body.AddForce(-cam.forward * thrust * Mathf.Sqrt(Owner.AbilityPower), ForceMode.Acceleration);
            }
        }

        base.FixedUpdate();
    }

    // ─── Visuals (every client) ───────────────────────────────────────────────

    private void LateUpdate()
    {
        ShowBlower(enabled && blowing);

        // Held at the left hand, pointing where the player aims (the hand bone's own angle varies with the animation)
        if (blowing && thirdPersonModel != null)
        {
            var anim = Owner != null ? Owner.Animation : null;
            if (anim != null && anim.LeftHand != null)
                thirdPersonModel.transform.SetPositionAndRotation(anim.LeftHand.position, anim.AimRotation);
            else
                thirdPersonModel.transform.rotation = Quaternion.LookRotation(transform.forward, Vector3.up);
        }
    }

    private void ShowBlower(bool show)
    {
        if (firstPersonModel != null && firstPersonModel.activeSelf != show) firstPersonModel.SetActive(show);
        if (thirdPersonModel != null && thirdPersonModel.activeSelf != show) thirdPersonModel.SetActive(show);
        if (airVFX != null && airVFX.activeSelf != show) airVFX.SetActive(show);

        if (blowSFX == null) return;
        if (blowAudio == null)
        {
            blowAudio = gameObject.AddComponent<AudioSource>();
            WorldFx.Configure3D(blowAudio, audioRadius, 0.6f);
            blowAudio.clip = blowSFX;
            blowAudio.loop = true;
        }
        if (show && !blowAudio.isPlaying) blowAudio.Play();
        else if (!show && blowAudio.isPlaying) blowAudio.Stop();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        blowing = false;
        ShowBlower(false);
    }

    // ─── Network Sync ─────────────────────────────────────────────────────────

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting) stream.SendNext(blowing);
        else                  blowing = (bool)stream.ReceiveNext();
    }
}
