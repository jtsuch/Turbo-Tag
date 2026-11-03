using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Moves remote copies of a player smoothly. The owner simulates its own Rigidbody and sends
/// timestamped snapshots (position, rotation, velocity). Remote copies are kinematic: they never
/// simulate physics themselves, they replay the owner's motion slightly in the past by
/// interpolating between the two snapshots around that time, and briefly extrapolate with the last
/// velocity if a packet is late. This replaces PhotonRigidbodyView, whose remote bodies kept
/// simulating gravity and contacts while being corrected by the network, which made players
/// standing still vibrate on other screens.
///
/// Other systems read <see cref="Velocity"/> for remote players (animation, footsteps), because a
/// kinematic body reports zero velocity.
/// Attach to: ThePlayer prefab root, and list it in the PhotonView's Observed Components.
/// </summary>
[RequireComponent(typeof(PhotonView))]
[RequireComponent(typeof(Rigidbody))]
public class PlayerNetworkSync : MonoBehaviourPun, IPunObservable
{
    [Tooltip("Extra delay on top of one send interval, so the next snapshot has usually arrived (seconds).")]
    [SerializeField] private float extraDelay = 0.05f;

    [Tooltip("How long a remote copy keeps moving on its last velocity when packets are late (seconds).")]
    [SerializeField] private float maxExtrapolation = 0.25f;

    [Tooltip("A jump larger than this between snapshots is a teleport (respawn): snap instead of sliding (m).")]
    [SerializeField] private float teleportDistance = 6f;

    private struct Snapshot
    {
        public double     Time;
        public Vector3    Position;
        public Quaternion Rotation;
        public Vector3    Velocity;
    }

    private const int MaxSnapshots = 20;

    private readonly List<Snapshot> snapshots = new();
    private Rigidbody body;
    private Vector3   remoteVelocity;

    /// <summary>The player's velocity on any client: the real one for the owner, the sent one for remote copies.</summary>
    public Vector3 Velocity => photonView.IsMine ? body.linearVelocity : remoteVelocity;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();

        if (!photonView.ObservedComponents.Contains(this))
            photonView.ObservedComponents.Add(this);

        if (!photonView.IsMine)
        {
            // Remote copies are moved only by snapshots. Interpolate smooths rendering between FixedUpdates.
            body.isKinematic   = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.detectCollisions = true;
        }
    }

    // ─── Remote playback ──────────────────────────────────────────────────────

    private void FixedUpdate()
    {
        if (photonView.IsMine || snapshots.Count == 0) return;

        double sendInterval = 1.0 / Mathf.Max(1, PhotonNetwork.SerializationRate);
        double renderTime   = PhotonNetwork.Time - sendInterval - extraDelay;

        Sample(renderTime, out Vector3 position, out Quaternion rotation, out remoteVelocity);
        body.MovePosition(position);
        body.MoveRotation(rotation);
    }

    private void Sample(double time, out Vector3 position, out Quaternion rotation, out Vector3 velocity)
    {
        // Before the oldest snapshot: hold it
        Snapshot first = snapshots[0];
        if (time <= first.Time)
        {
            position = first.Position;
            rotation = first.Rotation;
            velocity = first.Velocity;
            return;
        }

        // Between two snapshots: blend them
        for (int i = 0; i < snapshots.Count - 1; i++)
        {
            Snapshot a = snapshots[i];
            Snapshot b = snapshots[i + 1];
            if (time > b.Time) continue;

            float t = (float)((time - a.Time) / System.Math.Max(0.0001, b.Time - a.Time));
            position = Hermite(a, b, t);
            rotation = Quaternion.Slerp(a.Rotation, b.Rotation, t);
            velocity = Vector3.Lerp(a.Velocity, b.Velocity, t);
            return;
        }

        // After the newest snapshot: continue on its velocity for a short while, then hold
        Snapshot last = snapshots[snapshots.Count - 1];
        float ahead = Mathf.Min((float)(time - last.Time), maxExtrapolation);
        position = last.Position + last.Velocity * ahead;
        rotation = last.Rotation;
        velocity = last.Velocity;
    }

    // Curved blend that uses both snapshots' velocities, so arcs (jumps, swings) stay round.
    private static Vector3 Hermite(Snapshot a, Snapshot b, float t)
    {
        float span = (float)(b.Time - a.Time);
        float t2 = t * t;
        float t3 = t2 * t;
        return (2f * t3 - 3f * t2 + 1f) * a.Position
             + (t3 - 2f * t2 + t) * span * a.Velocity
             + (-2f * t3 + 3f * t2) * b.Position
             + (t3 - t2) * span * b.Velocity;
    }

    // ─── Network Sync ─────────────────────────────────────────────────────────

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(body.position);
            stream.SendNext(body.rotation);
            stream.SendNext(body.linearVelocity);
            return;
        }

        var snapshot = new Snapshot
        {
            Time     = info.SentServerTime,
            Position = (Vector3)stream.ReceiveNext(),
            Rotation = (Quaternion)stream.ReceiveNext(),
            Velocity = (Vector3)stream.ReceiveNext(),
        };

        // Out-of-order packets are dropped
        if (snapshots.Count > 0 && snapshot.Time <= snapshots[snapshots.Count - 1].Time) return;

        // A big jump is a teleport: forget the old path so the copy does not slide across the map
        if (snapshots.Count > 0 && Vector3.Distance(snapshot.Position, snapshots[snapshots.Count - 1].Position) > teleportDistance)
        {
            snapshots.Clear();
            body.position = snapshot.Position;
            body.rotation = snapshot.Rotation;
        }

        snapshots.Add(snapshot);
        if (snapshots.Count > MaxSnapshots) snapshots.RemoveAt(0);
    }
}
