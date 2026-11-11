using UnityEngine;

/// <summary>
/// Ice Wall (Resources/Object/IceWall): a solid block of ice across the placer's view (the prefab's box collider) that
/// blocks players and bullets until it melts (<see cref="lifetime"/>) or is shot down (<see cref="Shootable"/>). It
/// rises out of the floor when placed (<see cref="riseSeconds"/>), and anyone standing where it comes up rides up on
/// top of it (each screen lifts its own player). It may be placed into side walls or under players: only its centre
/// has to be on a floor (<see cref="IceWall"/>).
/// </summary>
public class IceWallObject : TacticalTrapObject
{
    private Vector3 fullScale;
    [SerializeField] private float riseSeconds = 0.45f;
    private BoxCollider box;

    private void Reset() => lifetime = 20f;

    protected override void Start()
    {
        base.Start();
        fullScale = transform.localScale;
        box = GetComponent<BoxCollider>();
        transform.localScale = new Vector3(fullScale.x, fullScale.y * 0.1f, fullScale.z);
        Sfx.At(SfxKind.Ice, transform.position, 1f, 40f, 0.8f);
        Vfx.Burst(transform.position, new Color(0.7f, 0.9f, 1f), 30, 5f, 0.3f, 0.6f, direction: Vector3.up, spread: 0.5f);
    }

    private void Update()
    {
        if (fullScale == Vector3.zero) return;
        float t = Mathf.Clamp01((Time.time - PlacedAt) / riseSeconds);
        transform.localScale = new Vector3(fullScale.x, fullScale.y * Mathf.Lerp(0.1f, 1f, t), fullScale.z);
    }

    private bool lifting;

    // While it rises (and a moment after): the local player standing over its footprint is carried up on its top,
    // instead of the growing block shoving them out sideways
    private void FixedUpdate()
    {
        if (box == null || fullScale == Vector3.zero) return;
        float age = Time.time - PlacedAt;
        if (age > riseSeconds + 0.15f) { lifting = false; return; }
        var local = Player.Local;
        if (local == null || local.Body == null || local.IsCaught) return;

        Vector3 feet = local.Body.position;
        Vector3 inWall = transform.InverseTransformPoint(feet);
        Vector3 half = box.size * 0.5f, c = box.center;
        float margin = 0.6f / Mathf.Max(0.01f, transform.lossyScale.x); // The body's own width counts
        bool over = Mathf.Abs(inWall.x - c.x) <= half.x + margin && Mathf.Abs(inWall.z - c.z) <= half.z + 0.6f / Mathf.Max(0.01f, transform.lossyScale.z);
        float top = box.bounds.max.y;
        bool inside = over && feet.y < top + 0.05f && feet.y > transform.position.y - 0.5f;
        if (!inside && !lifting) return;
        if (!over) { lifting = false; return; }
        lifting = true;
        if (feet.y < top)
        {
            local.Body.MovePosition(new Vector3(feet.x, top + 0.02f, feet.z));
            var v = local.Body.linearVelocity;
            local.Body.linearVelocity = new Vector3(v.x, Mathf.Max(0f, v.y), v.z);
            local.NotePushed();
        }
    }
}
