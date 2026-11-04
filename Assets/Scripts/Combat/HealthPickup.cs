using UnityEngine;

/// <summary>
/// The look of a health drop: a green cross that floats, bobs and spins, with a soft green light. Built in
/// code; local only (CombatManager networks who takes it). Has no collider: pickup is a distance check.
/// </summary>
public class HealthPickup : MonoBehaviour
{
    private static Material material;

    private Transform cross;
    private Vector3   basePosition;

    public static HealthPickup Create(Vector3 ground)
    {
        var root = new GameObject("Health Drop");
        root.transform.position = ground + Vector3.up * 0.9f;
        var pickup = root.AddComponent<HealthPickup>();
        pickup.Build();
        return pickup;
    }

    private void Build()
    {
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var green = new Color(0.2f, 0.95f, 0.35f);
            material.SetColor("_BaseColor", green);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", green * 1.5f);
        }

        cross = new GameObject("Cross").transform;
        cross.SetParent(transform, false);
        Bar(new Vector3(0.6f, 0.2f, 0.2f));
        Bar(new Vector3(0.2f, 0.6f, 0.2f));

        var light = gameObject.AddComponent<Light>();
        light.type      = LightType.Point;
        light.color     = new Color(0.3f, 1f, 0.45f);
        light.range     = 4f;
        light.intensity = 2f;
        light.shadows   = LightShadows.None;

        basePosition = transform.position;
    }

    private void Bar(Vector3 size)
    {
        var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(bar.GetComponent<Collider>());
        bar.transform.SetParent(cross, false);
        bar.transform.localScale = size;
        var r = bar.GetComponent<MeshRenderer>();
        r.sharedMaterial    = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private void Update()
    {
        transform.position = basePosition + Vector3.up * (Mathf.Sin(Time.time * 2f) * 0.15f);
        cross.Rotate(0f, 90f * Time.deltaTime, 0f, Space.World);
    }
}
