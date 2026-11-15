using UnityEngine;

/// <summary>
/// Slick (Snowball, Ice Trap, Donut ice: <see cref="Player.IsIced"/>): each foot frozen in its own translucent blue
/// block of ice, on every screen (<see cref="Player.IsSlick"/> is synced). The penguin's feet are wide flat paddles
/// under each leg (about 0.6 m across and 0.7 m long, measured from the mesh), so each block is centred on its foot
/// (from the LeftFoot / RightFoot bone) and big enough to hold the whole foot and the ankle; the blocks follow the
/// feet as they move. They pop in, and melt away when it ends. Added to every player copy by <see cref="Player"/>.
/// </summary>
public class SlickFeet : MonoBehaviour
{
    private Player player;
    private Transform leftFoot, rightFoot;
    private Transform left, right;
    private float amount;
    private static Material ice;

    // Measured on the model at normal size, in the body's frame: the foot's middle sits ~0.15 m in front of its bone
    // and the block reaches from the floor to just over the ankle
    private static readonly Vector3 FromBone = new(0f, 0.18f, 0.14f);
    private static readonly Vector3 BlockSize = new(0.78f, 0.46f, 0.92f);

    private void Awake()
    {
        player = GetComponent<Player>();
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "LeftFoot") leftFoot = t;
            else if (t.name == "RightFoot") rightFoot = t;
        }
    }

    private void LateUpdate()
    {
        if (player == null) return;
        amount = Mathf.MoveTowards(amount, player.IsSlick && !player.IsDownAfterTag ? 1f : 0f, 6f * Time.deltaTime);
        bool show = amount > 0f;
        if (!show) { if (left != null) { left.gameObject.SetActive(false); right.gameObject.SetActive(false); } return; }
        if (left == null) { left = Block("Ice Block L"); right = Block("Ice Block R"); }
        float pop = Mathf.SmoothStep(0f, 1f, amount) * Mathf.Max(0.3f, player.Size);
        Place(left, leftFoot, -1f, pop);
        Place(right, rightFoot, 1f, pop);
    }

    private void Place(Transform block, Transform foot, float side, float scale)
    {
        block.gameObject.SetActive(true);
        Vector3 flat = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        var facing = Quaternion.LookRotation(flat.sqrMagnitude > 0.01f ? flat.normalized : Vector3.forward, Vector3.up);
        // Without the bone (shouldn't happen), under where the foot would be
        Vector3 bone = foot != null ? foot.position : transform.position + facing * new Vector3(side * 0.33f * scale, 0f, -0.15f * scale);
        block.SetPositionAndRotation(bone + facing * (FromBone * scale), facing);
        block.localScale = BlockSize * scale;
    }

    private Transform Block(string name)
    {
        if (ice == null)
        {
            var lit = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Slick Ice" };
            ice = CosmeticWearer.SeeThroughCopy(lit);
            ice.SetColor("_BaseColor", new Color(0.6f, 0.85f, 1f, 0.42f));
            ice.SetFloat("_Smoothness", 0.95f);
            ice.EnableKeyword("_EMISSION");
            ice.SetColor("_EmissionColor", new Color(0.08f, 0.2f, 0.35f));
        }
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(cube.GetComponent<Collider>());
        cube.name = name;
        cube.layer = gameObject.layer;
        cube.transform.SetParent(transform, true);
        var r = cube.GetComponent<MeshRenderer>();
        r.sharedMaterial = ice;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return cube.transform;
    }

    private void OnDestroy()
    {
        if (left != null) Destroy(left.gameObject);
        if (right != null) Destroy(right.gameObject);
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
