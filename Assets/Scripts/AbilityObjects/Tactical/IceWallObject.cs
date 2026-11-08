using UnityEngine;

/// <summary>
/// Ice Wall (Resources/Object/IceWall): a solid block of ice across the placer's view (the prefab's box collider) that
/// blocks players and bullets until it melts (<see cref="lifetime"/>) or is shot down (<see cref="Shootable"/>). It
/// rises out of the floor when placed.
/// </summary>
public class IceWallObject : TacticalTrapObject
{
    private Vector3 fullScale;

    private void Reset() => lifetime = 20f;

    protected override void Start()
    {
        base.Start();
        fullScale = transform.localScale;
        transform.localScale = new Vector3(fullScale.x, fullScale.y * 0.1f, fullScale.z);
        Sfx.At(SfxKind.Ice, transform.position, 1f, 40f, 0.8f);
        Vfx.Burst(transform.position, new Color(0.7f, 0.9f, 1f), 30, 5f, 0.3f, 0.6f, direction: Vector3.up, spread: 0.5f);
    }

    private void Update()
    {
        if (fullScale == Vector3.zero) return;
        float t = Mathf.Clamp01((Time.time - PlacedAt) / 0.35f);
        transform.localScale = new Vector3(fullScale.x, fullScale.y * Mathf.Lerp(0.1f, 1f, t), fullScale.z);
    }
}
