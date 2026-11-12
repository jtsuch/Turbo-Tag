using UnityEngine;

/// <summary>
/// A zip line between two cable ends (a level builder sets them). Stand under the high end and press Grab (F) to
/// ride it down, hanging just under the cable; Jump lets go (keeping the speed), and you drop off at the far end.
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
    [Tooltip("How close (sideways) to the high end you must stand to get on (m).")]
    [SerializeField] private float reach = 3.5f;
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

        // Under the high end: close sideways, and the cable 2–7 m above the feet
        Vector3 feet = local.transform.position;
        Vector3 flat = feet - from;
        flat.y = 0f;
        float below = from.y - feet.y;
        if (flat.magnitude > reach || below < 2f || below > 7f) return;

        KeyCode key = Keybinds.Get(InputAction.Grab);
        local.ClaimGrabKey(); // F rides the line here; it must not also grab a ledge
        HUDManager.Prompt(key, "Ride the zip line");
        if (Input.GetKeyDown(key)) Attach(local);
    }

    private void Attach(Player player)
    {
        rider = player;
        body = player.Body;
        if (body == null) { rider = null; return; }
        length = Vector3.Distance(from, to);
        along = 0f;
        hang = player.TopY - player.transform.position.y + 0.15f; // The head just under the cable
        player.OnZipLine = true;
        Sfx.At(SfxKind.Whoosh, from, 0.8f, 30f, 0.8f);
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
