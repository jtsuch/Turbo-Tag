using UnityEngine;

/// <summary>
/// Slick (Snowball, Ice Trap, Donut ice: <see cref="Player.IsIced"/>): the player's feet stuck in translucent blue ice
/// cubes, on every screen (<see cref="Player.IsSlick"/> is synced). They pop in, ride along with the feet, and melt
/// away when it ends. Added to every player copy by <see cref="Player"/>.
/// </summary>
public class SlickFeet : MonoBehaviour
{
    private Player player;
    private Collider body;
    private Transform cubes;
    private float amount;
    private static Material ice;

    // Two big blocks (one per foot) and a few chips, in units of the player's size
    private static readonly (Vector3 at, Vector3 size, float spin)[] Blocks =
    {
        (new Vector3(-0.17f, 0.17f,  0.02f), new Vector3(0.36f, 0.34f, 0.40f),  8f),
        (new Vector3( 0.18f, 0.15f, -0.03f), new Vector3(0.34f, 0.30f, 0.38f), -11f),
        (new Vector3( 0.02f, 0.08f,  0.24f), new Vector3(0.20f, 0.16f, 0.18f),  27f),
        (new Vector3(-0.05f, 0.07f, -0.24f), new Vector3(0.18f, 0.14f, 0.16f), -34f),
        (new Vector3( 0.34f, 0.05f,  0.12f), new Vector3(0.12f, 0.10f, 0.12f),  45f),
    };

    private void Awake()
    {
        player = GetComponent<Player>();
        body = GetComponent<Collider>();
    }

    private void LateUpdate()
    {
        if (player == null) return;
        amount = Mathf.MoveTowards(amount, player.IsSlick && !player.IsDownAfterTag ? 1f : 0f, 6f * Time.deltaTime);
        if (amount <= 0f) { if (cubes != null) cubes.gameObject.SetActive(false); return; }
        if (cubes == null) Build();
        cubes.gameObject.SetActive(true);

        float floor = body != null && body.enabled ? body.bounds.min.y : transform.position.y;
        Vector3 at = transform.position;
        Vector3 flat = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        cubes.SetPositionAndRotation(new Vector3(at.x, floor, at.z),
            Quaternion.LookRotation(flat.sqrMagnitude > 0.01f ? flat : Vector3.forward, Vector3.up));
        float pop = amount < 1f ? Mathf.SmoothStep(0f, 1f, amount) : 1f;
        cubes.localScale = Vector3.one * Mathf.Max(0.3f, player.Size) * pop;
    }

    private void Build()
    {
        if (ice == null)
        {
            var lit = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Slick Ice" };
            ice = CosmeticWearer.SeeThroughCopy(lit);
            ice.SetColor("_BaseColor", new Color(0.6f, 0.85f, 1f, 0.45f));
            ice.SetFloat("_Smoothness", 0.95f);
            ice.EnableKeyword("_EMISSION");
            ice.SetColor("_EmissionColor", new Color(0.08f, 0.2f, 0.35f));
        }
        cubes = new GameObject("Slick Ice Cubes").transform;
        cubes.SetParent(transform, true);
        foreach (var b in Blocks)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(cube.GetComponent<Collider>());
            cube.name = "Ice Cube";
            cube.layer = gameObject.layer;
            cube.transform.SetParent(cubes, false);
            cube.transform.localPosition = b.at;
            cube.transform.localRotation = Quaternion.Euler(b.spin * 0.2f, b.spin, b.spin * -0.15f);
            cube.transform.localScale = b.size;
            var r = cube.GetComponent<MeshRenderer>();
            r.sharedMaterial = ice;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    private void OnDestroy()
    {
        if (cubes != null) Destroy(cubes.gameObject);
    }
}

/// <summary>Curse (Snowball, Ice Trap): Slick, sliding about with almost no grip.</summary>
public class SlickEffect : PlayerEffect
{
    public override string DisplayName => "Slick";
    public override string Description => "Feet frozen: almost no grip";
    public override bool   IsGood      => false;

    protected override void OnEffectStart() { }
    protected override void OnEffectEnd()   { }
}
