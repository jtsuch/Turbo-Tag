using UnityEngine;

/// <summary>
/// Snow Screen (Resources/Object/SnowScreen): a curtain of falling snow <see cref="width"/> × <see cref="height"/> across
/// the placer's view (the prefab's "Curtain" panes hide what's behind it). Enemies walking through are slowed
/// (<see cref="ShockedEffect"/> while inside, a moment after).
/// </summary>
public class SnowScreenObject : TacticalTrapObject
{
    [SerializeField] private float width = 8f;
    [SerializeField] private float height = 5f;
    [SerializeField] private float depth = 2.4f;
    private float nextFlake;

    private void Reset() => lifetime = 14f;

    private void Update()
    {
        // Snow drifting down the curtain
        if (Time.time >= nextFlake)
        {
            nextFlake = Time.time + 0.1f;
            Vector3 at = transform.position + transform.right * Random.Range(-width * 0.5f, width * 0.5f) + Vector3.up * height;
            Vfx.Burst(at, new Color(0.95f, 0.97f, 1f), 3, 1.2f, 0.18f, 2.2f, gravity: true);
        }

        var enemy = LocalEnemy;
        if (enemy == null) return;
        Vector3 local = transform.InverseTransformPoint(enemy.transform.position + Vector3.up);
        if (Mathf.Abs(local.x) <= width * 0.5f && Mathf.Abs(local.z) <= depth * 0.5f && local.y >= -1f && local.y <= height + 1f)
            Keep(typeof(ShockedEffect), enemy, 1.5f);
    }
}
