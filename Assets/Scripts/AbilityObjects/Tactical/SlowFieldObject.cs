using UnityEngine;

/// <summary>
/// Slow Field (Resources/Object/SlowField): a glowing disc <see cref="radius"/> across; enemies standing in it move at
/// 60% (<see cref="ShockedEffect"/>, held while inside).
/// </summary>
public class SlowFieldObject : TacticalTrapObject
{
    [TunableParam("Radius", 1f, 15f)]
    [SerializeField] private float radius = 5f;
    private float nextPulse;

    private void Reset() => lifetime = 10f;

    private void Update()
    {
        if (Time.time >= nextPulse)
        {
            nextPulse = Time.time + 1f;
            Vfx.Ring(transform.position + Vector3.up * 0.1f, new Color(0.7f, 0.45f, 1f), 36, radius * 1.6f, 0.25f, 0.8f);
        }
        var enemy = LocalEnemy;
        if (enemy == null) return;
        Vector3 off = enemy.transform.position - transform.position;
        if (Mathf.Abs(off.y) < 3f && new Vector2(off.x, off.z).magnitude <= radius * Power)
            Keep(typeof(ShockedEffect), enemy, 0.8f);
    }
}
