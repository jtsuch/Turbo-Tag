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

    protected override bool UpgradeScalesLifetime => true;   // S&D: 6 / 8 / 10 s

    private GroundRing ring;
    private LineRenderer[] circles;

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
        dome = Glass(new Color(1f, 0.35f, 0.4f, 0.12f));
        var r = sphere.GetComponent<Renderer>();
        r.sharedMaterial = dome;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Clearer edges (Justin, Nov 14: the reach was hard to see): a bright ring on the floor where the dome meets it,
        // and three glowing great circles round the dome
        float reach = radius * Power;
        ring = GroundRing.Held(transform.position, reach, new Color(1f, 0.3f, 0.35f));
        var lineMat = new Material(Shader.Find("Sprites/Default"));
        circles = new LineRenderer[3];
        for (int c = 0; c < 3; c++)
        {
            var lr = new GameObject("Edge").AddComponent<LineRenderer>();
            lr.transform.SetParent(transform, false);
            lr.useWorldSpace = true;
            lr.loop = true;
            lr.positionCount = 64;
            lr.widthMultiplier = 0.08f;
            lr.sharedMaterial = lineMat;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Quaternion turn = Quaternion.Euler(c == 1 ? 90f : 0f, c * 60f, c == 2 ? 90f : 0f);
            for (int i = 0; i < 64; i++)
            {
                float a = i / 64f * Mathf.PI * 2f;
                lr.SetPosition(i, transform.position + turn * new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * reach);
            }
            circles[c] = lr;
        }
    }

    private void OnDestroy()
    {
        if (ring != null) Destroy(ring.gameObject);
    }

    private AoeDebug aoe;

    private void Update()
    {
        if (AoeDebug.On && aoe == null) aoe = AoeDebug.Sphere(transform.position, radius * Power, new Color(0.75f, 0.45f, 1f), "Suppression (chest)", 0f, transform);
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 4f);
        if (dome != null) dome.color = new Color(1f, 0.35f, 0.4f, 0.09f + 0.05f * pulse);
        if (circles != null)
            foreach (var lr in circles) if (lr != null) lr.startColor = lr.endColor = new Color(1f, 0.35f, 0.4f, 0.45f + 0.35f * pulse);
        var enemy = LocalEnemy;
        if (enemy == null) return;
        if (Vector3.Distance(AbilityDamage.Chest(enemy), transform.position) <= radius * Power)
            Keep(typeof(SuppressedEffect), enemy, 0.8f);
    }
}
