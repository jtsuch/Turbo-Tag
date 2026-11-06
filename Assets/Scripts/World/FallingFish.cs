using UnityEngine;

/// <summary>
/// A purely visual fish (Fish Rain donut): drops from the sky, lands, flops about and fades. No collider or
/// network object; each screen rains its own. The model is the Fish Bait prefab's "Model" child.
/// </summary>
public class FallingFish : MonoBehaviour
{
    private const float Gravity = 25f;
    private const float Life    = 5f;

    private static GameObject model;
    private float   speed;
    private float   groundY = float.NegativeInfinity;
    private float   born;
    private bool    landed;
    private Vector3 spin;

    /// <summary>Drops <paramref name="count"/> fish around <paramref name="centre"/> over a moment.</summary>
    public static void Rain(Vector3 centre, int count)
    {
        if (model == null)
        {
            var bait = Resources.Load<GameObject>("Object/FishBait");
            var child = bait != null ? bait.transform.Find("Model") : null;
            model = child != null ? child.gameObject : null;
        }
        for (int i = 0; i < count; i++)
        {
            Vector2 offset = Random.insideUnitCircle * 4f;
            Vector3 at = centre + new Vector3(offset.x, 6f + Random.Range(0f, 6f), offset.y);
            var go = model != null ? Instantiate(model, at, Random.rotation) : CreateStandIn(at);
            go.name = "Falling Fish";
            go.AddComponent<FallingFish>();
        }
        Sfx.At(SfxKind.Splat, centre + Vector3.up * 2f, 0.8f, 40f, 1.3f);
    }

    private static GameObject CreateStandIn(Vector3 at)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Destroy(go.GetComponent<Collider>());
        go.transform.position = at;
        go.transform.localScale = new Vector3(0.2f, 0.3f, 0.2f);
        return go;
    }

    private void Start()
    {
        born = Time.time;
        spin = Random.insideUnitSphere * 360f;
        if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 40f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            groundY = hit.point.y + 0.15f;
    }

    private void Update()
    {
        float age = Time.time - born;
        if (!landed)
        {
            speed += Gravity * Time.deltaTime;
            transform.position += Vector3.down * speed * Time.deltaTime;
            transform.Rotate(spin * Time.deltaTime, Space.World);
            if (transform.position.y <= groundY)
            {
                landed = true;
                transform.position = new Vector3(transform.position.x, groundY, transform.position.z);
                Sfx.At(SfxKind.Plop, transform.position, 0.5f, 25f, Random.Range(0.9f, 1.3f));
                Vfx.Burst(transform.position, new Color(0.6f, 0.85f, 1f, 0.7f), 5, 2f, 0.12f, 0.3f);
            }
        }
        else
        {
            // Flop on its side
            float flop = Mathf.Sin(Time.time * 20f) * 25f;
            transform.rotation = Quaternion.Euler(flop, transform.eulerAngles.y, 90f);
        }

        if (age > Life - 0.5f) transform.localScale = Vector3.one * Mathf.Max(0f, (Life - age) / 0.5f);
        if (age > Life || transform.position.y < groundY - 50f) Destroy(gameObject);
    }
}
