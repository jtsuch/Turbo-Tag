using UnityEngine;

/// <summary>
/// Updraft Vent (Resources/Object/UpdraftVent): a floor vent blowing a column of air <see cref="height"/> m up; anyone
/// standing in it (placer, teammates and enemies alike: it's a route, not a trap) is carried upward, like a fan.
/// Each screen pushes its own player. Wind streaks rise to show the column.
/// </summary>
public class UpdraftVentObject : TacticalTrapObject
{
    [TunableParam("Column Height", 2f, 40f)]
    [SerializeField] private float height = 14f;
    [TunableParam("Lift", 10f, 400f)]
    [SerializeField] private float lift = 120f;
    [SerializeField] private float radius = 1.4f;
    private float nextStreak;

    private void Reset() => lifetime = 20f;

    private void Update()
    {
        if (Time.time >= nextStreak)
        {
            nextStreak = Time.time + 0.08f;
            Vector2 r = Random.insideUnitCircle * radius;
            Vfx.Burst(transform.position + new Vector3(r.x, 0.3f, r.y), new Color(0.85f, 0.95f, 1f, 0.6f), 2, 12f, 0.12f, 0.9f, direction: Vector3.up, spread: 0.05f);
        }
    }

    private AoeDebug aoe;

    private void FixedUpdate()
    {
        if (AoeDebug.On && aoe == null) aoe = AoeDebug.Cylinder(transform.position, radius, 1f, height * Power, new Color(0.4f, 1f, 0.55f), "Updraft Vent", 0f, transform);
        var local = Player.Local;
        if (local == null || local.IsCaught) return;
        Vector3 off = local.transform.position - transform.position;
        if (off.y < -1f || off.y > height * Power || new Vector2(off.x, off.z).magnitude > radius) return;
        local.NotePushed(); // An outside force: movement mustn't brake it
        // Rising fast near the vent, slower near the top, so you float up and hang there a moment. Sets the upward
        // speed directly (like a bounce pad): a force alone was held down by the ground snap.
        float t = Mathf.Clamp01(off.y / (height * Power));
        float rise = Mathf.Lerp(16f, 4f, t);
        Vector3 v = local.Body.linearVelocity;
        if (v.y < rise) v.y = Mathf.MoveTowards(Mathf.Max(v.y, 0f), rise, lift * Time.fixedDeltaTime);
        local.Body.linearVelocity = v;
    }
}
