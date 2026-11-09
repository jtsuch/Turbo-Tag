using Photon.Pun;
using UnityEngine;

/// <summary>
/// A placed security camera (SpyCamera ability). The base stays fixed to the wall; the head (body and
/// lens, one unit) turns to wherever its owner looks while viewing through it. The head's angle is
/// synced, so everyone sees the camera turn. <see cref="ViewPoint"/> is where the owner's view sits.
/// Attach to: Resources/Object/SpyCamera (PhotonView observing this, kinematic Rigidbody, collider).
/// </summary>
public class SpyCameraObject : MonoBehaviourPun, IPunObservable
{
    [Header("Parts")]
    [Tooltip("Turning part (camera body + lens). Its rest pose faces out from the wall.")]
    [SerializeField] private Transform head;
    [Tooltip("Where the view comes from (in front of the lens, under the head).")]
    [SerializeField] private Transform viewPoint;

    [Header("Look Limits (degrees from facing straight out)")]
    [SerializeField] private float maxYaw   = 80f;
    [SerializeField] private float maxPitch = 60f;
    [Tooltip("How quickly remote copies follow the synced angle.")]
    [SerializeField] private float remoteSmoothing = 15f;

    /// <summary>Where the owner's camera sits while viewing.</summary>
    public Transform ViewPoint => viewPoint != null ? viewPoint : head != null ? head : transform;

    private Quaternion restRotation;
    private float      yaw, pitch;
    private float      remoteYaw, remotePitch;

    private void Awake()
    {
        ObjectTunables.Apply(photonView); // Cheat values from the placer
        // Shootable: 50 damage breaks it (the owner's view drops back to them when it's gone). Added on every copy
        if (!TryGetComponent(out Shootable _)) gameObject.AddComponent<Shootable>().health = 50f;
        if (head != null) restRotation = head.localRotation;
        if (!photonView.ObservedComponents.Contains(this)) photonView.ObservedComponents.Add(this);
    }

    /// <summary>Owner: turns the head by a mouse delta (degrees), within the look limits.</summary>
    public void Look(float deltaYaw, float deltaPitch)
    {
        yaw   = Mathf.Clamp(yaw + deltaYaw, -maxYaw, maxYaw);
        pitch = Mathf.Clamp(pitch - deltaPitch, -maxPitch, maxPitch);
        ApplyHead();
    }

    private void Update()
    {
        if (photonView.IsMine) return;
        float t = 1f - Mathf.Exp(-remoteSmoothing * Time.deltaTime);
        yaw   = Mathf.Lerp(yaw, remoteYaw, t);
        pitch = Mathf.Lerp(pitch, remotePitch, t);
        ApplyHead();
    }

    // Yaw around the world's up, pitch around the head's own side axis, so it never rolls
    private void ApplyHead()
    {
        if (head == null) return;
        Quaternion rest  = (head.parent != null ? head.parent.rotation : Quaternion.identity) * restRotation;
        Quaternion world = Quaternion.AngleAxis(yaw, Vector3.up) * rest;
        world = Quaternion.AngleAxis(pitch, world * Vector3.right) * world;
        head.rotation = world;
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(yaw);
            stream.SendNext(pitch);
        }
        else
        {
            remoteYaw   = (float)stream.ReceiveNext();
            remotePitch = (float)stream.ReceiveNext();
        }
    }
}
