using UnityEngine;

/// <summary>
/// A zip line between two cable ends (a level builder sets them). Get under the cable anywhere along it (standing
/// under the high end, or jumping up to it mid-way) and press Grab (F) to ride it down from there, hanging just
/// under the cable; Jump lets go (keeping the speed), and you drop off at the far end.
/// Each client rides its own player: the body is moved here while <see cref="Player.OnZipLine"/> (PlayerMovement
/// stands aside: no movement, no gravity); others see the ride through the normal movement sync.
/// Attach to: the high end's anchor (level builders add it).
/// </summary>
public class ZipLine : MonoBehaviour
{
    [Tooltip("The high end of the cable, where you get on (world).")]
    [SerializeField] private Vector3 from;
    [Tooltip("The low end of the cable, where you drop off (world).")]
    [SerializeField] private Vector3 to;
    [Tooltip("Riding speed along the cable (m/s).")]
    [SerializeField] private float speed = 20f;
    [Tooltip("How close to the cable your head must be to grab it (m).")]
    [SerializeField] private float reach = 3f;
    [Tooltip("Upward speed added when you let go with Jump (m/s).")]
    [SerializeField] private float jumpOff = 6f;

    private Player rider;
    private Rigidbody body;
    private float along, length, hang;

    private Vector3 Direction => (to - from).normalized;

    private void Update()
    {
        var local = Player.Local;
        if (local == null || PauseMenuManager.IsPaused) return;
        if (rider != null)
        {
            if (Input.GetKeyDown(Keybinds.Get(InputAction.Jump))) Detach(Direction * speed + Vector3.up * jumpOff);
            return;
        }
        if (!local.CanMove || local.IsCaught || local.OnZipLine) return;

        // Anywhere along it: the nearest point of the cable to your head, within reach and not below your head,
        // with enough cable left to ride
        Vector3 head = new(local.transform.position.x, local.TopY, local.transform.position.z);
        float len = Vector3.Distance(from, to);
        float at = Mathf.Clamp(Vector3.Dot(head - from, Direction), 0f, len);
        Vector3 onCable = from + Direction * at;
        if (at > len - 2f || (onCable - head).magnitude > reach || onCable.y < head.y - 0.5f) return;

        KeyCode key = Keybinds.Get(InputAction.Grab);
        local.ClaimGrabKey(); // F rides the line here; it must not also grab a ledge
        HUDManager.Prompt(key, "Ride the zip line");
        if (Input.GetKeyDown(key)) Attach(local, at);
    }

    private void Attach(Player player, float at)
    {
        rider = player;
        body = player.Body;
        if (body == null) { rider = null; return; }
        length = Vector3.Distance(from, to);
        along = at;
        hang = player.TopY - player.transform.position.y + 0.15f; // The head just under the cable
        player.OnZipLine = true;
        body.position = from + Direction * along - Vector3.up * hang; // Onto the cable where you grabbed it
        body.linearVelocity = Direction * speed;
        Sfx.At(SfxKind.Whoosh, body.position, 0.8f, 30f, 0.8f);
    }

    private void FixedUpdate()
    {
        if (rider == null) return;
        // Caught, respawned or teleported away: let go
        Vector3 target = from + Direction * along - Vector3.up * hang;
        if (rider != Player.Local || rider.IsCaught || (body.position - target).sqrMagnitude > 9f)
        {
            Detach(Vector3.zero);
            return;
        }
        along += speed * Time.fixedDeltaTime;
        if (along >= length - 0.5f)
        {
            Detach(Vector3.ProjectOnPlane(Direction, Vector3.up).normalized * speed * 0.5f); // Drop off, still moving on
            return;
        }
        body.position = from + Direction * along - Vector3.up * hang;
        body.linearVelocity = Direction * speed;
    }

    private void Detach(Vector3 velocity)
    {
        if (rider == null) return;
        rider.OnZipLine = false;
        if (body != null)
        {
            body.linearVelocity = velocity;
            rider.NotePushed(); // An outside push: movement doesn't brake it straight away
        }
        rider = null;
        body = null;
    }

    private void OnDisable() => Detach(Vector3.zero);
}
