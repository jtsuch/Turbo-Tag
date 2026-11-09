using UnityEngine;

/// <summary>
/// Suppression (Resources/Object/Suppressor): a humming device with a see-through dome <see cref="radius"/> around it
/// (built at start, so the placing hologram stays small); enemies inside can't use abilities
/// (<see cref="SuppressedEffect"/>, held while inside).
/// </summary>
public class SuppressorObject : TacticalTrapObject
{
    [TunableParam("Radius", 1f, 30f)]
    [SerializeField] private float radius = 12f;
    private Material dome;

    private void Reset() => lifetime = 8f;

    protected override void Start()
    {
        base.Start();
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(sphere.GetComponent<Collider>());
        sphere.name = "Dome";
        sphere.transform.SetParent(transform, false);
        sphere.transform.position = transform.position;
        float size = radius * 2f * Power;
        sphere.transform.localScale = new Vector3(size / transform.lossyScale.x, size / transform.lossyScale.y, size / transform.lossyScale.z);
        dome = Glass(new Color(1f, 0.35f, 0.4f, 0.06f));
        var r = sphere.GetComponent<Renderer>();
        r.sharedMaterial = dome;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private void Update()
    {
        if (dome != null) dome.color = new Color(1f, 0.35f, 0.4f, 0.05f + 0.025f * Mathf.Sin(Time.time * 4f));
        var enemy = LocalEnemy;
        if (enemy == null) return;
        if (Vector3.Distance(AbilityDamage.Chest(enemy), transform.position) <= radius * Power)
            Keep(typeof(SuppressedEffect), enemy, 0.8f);
    }
}
