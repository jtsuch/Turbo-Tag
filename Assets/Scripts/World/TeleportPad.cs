using UnityEngine;

/// <summary>
/// One of a pair of teleport pads (the TeleportPad model): stepping onto it sends the player to its
/// <see cref="partner"/>, facing the way that pad faces, keeping none of their speed. A short cooldown (and arriving
/// on the partner doesn't count) stops ping-ponging. Each client moves only its own player; every screen sees the
/// flash at both ends. Adds its own trigger above the pad in Awake.
/// Attach to: both TeleportPad instances, each pointing at the other (level builders link them).
/// </summary>
public class TeleportPad : MonoBehaviour
{
    [SerializeField] private TeleportPad partner;
    [SerializeField] private float cooldown = 1.5f;
    [Tooltip("Which way you face arriving here (degrees, world yaw); the model's own rotation is tilted, so it's kept here.")]
    [SerializeField] private float exitYaw;

    private static float lastTeleport = -10f; // The local player's last trip, any pad

    public TeleportPad Partner { get => partner; set => partner = value; }
    public float ExitYaw { get => exitYaw; set => exitYaw = value; }

    private void Awake()
    {
        // In world space (the model itself may be imported tilted or scaled), upright over the pad
        var zone = new GameObject("Teleport Zone").AddComponent<BoxCollider>();
        zone.transform.SetPositionAndRotation(transform.position, Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
        zone.transform.SetParent(transform, true);
        Vector3 s = zone.transform.lossyScale;
        zone.isTrigger = true;
        zone.center = new Vector3(0f, 1.2f / s.y, 0f);
        zone.size = new Vector3(2.6f / s.x, 2f / s.y, 2.6f / s.z);
        zone.gameObject.AddComponent<TeleportZone>().Pad = this;
    }

    internal void Touched(Collider other)
    {
        var player = other.GetComponentInParent<Player>();
        if (player == null || partner == null || !player.IsLocal || player.IsCaught) return;
        if (Time.time - lastTeleport < cooldown) return;
        lastTeleport = Time.time;

        Vector3 from = player.transform.position;
        Vector3 to = partner.transform.position + Vector3.up * 0.6f;
        var facing = Quaternion.Euler(0f, partner.exitYaw, 0f);
        player.Teleport(to, facing);
        if (PlayerCam.Local != null) PlayerCam.Local.SnapTo(facing.eulerAngles.y);
        Fx.Respawn(from);
        Fx.Respawn(to);
        Sfx.At(SfxKind.Warp, to, 1f, 40f);
    }
}

/// <summary>The trigger above a <see cref="TeleportPad"/> (made by it).</summary>
public class TeleportZone : MonoBehaviour
{
    public TeleportPad Pad;
    private void OnTriggerEnter(Collider other) { if (Pad != null) Pad.Touched(other); }
}
