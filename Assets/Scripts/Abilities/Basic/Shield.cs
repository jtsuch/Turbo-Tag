using Photon.Pun;
using UnityEngine;

/// <summary>
/// Hold to raise a bubble: while it's up, other players' abilities can't touch you (blasts, pulls, ice, traps,
/// flashes, Grow Ray, Honk, Fish Bait). Your own still work. The flag is <see cref="Player.IsShielded"/>, synced
/// here so every screen draws the bubble and every effect can check it (<see cref="Player.AffectedBy"/>).
/// Guns are not abilities: bullets still hit.
/// </summary>
[AbilityInfo("Hold to raise a bubble: other players' abilities can't touch you while it's up.")]
public class Shield : BasicAbility, IPunObservable
{
    [Header("Bubble")]
    [SerializeField] private Color bubbleColor = new(0.35f, 0.85f, 1f, 0.22f);

    private bool       held;
    private GameObject bubble;
    private Material   bubbleMaterial;
    private float      shown; // 0–1 pop in/out

    protected override void Awake()
    {
        base.Awake();
        var view = GetComponent<PhotonView>();
        if (!view.ObservedComponents.Contains(this)) view.ObservedComponents.Add(this);
    }

    protected override void OnPress()
    {
        if (CurrentDuration <= 0.01f) return;
        held = true;
        IsActive = true;
        Owner.IsShielded = true;
    }

    protected override void OnRelease() => Deactivate();

    protected override void Deactivate()
    {
        held     = false;
        IsActive = false;
        if (Owner != null && IsLocal) Owner.IsShielded = false;
    }

    protected override void FixedUpdate()
    {
        if (IsLocal && !held && Owner.IsShielded) Owner.IsShielded = false;
        base.FixedUpdate();
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting) stream.SendNext(Owner != null && Owner.IsShielded);
        else if (Owner != null) Owner.IsShielded = (bool)stream.ReceiveNext();
        else stream.ReceiveNext();
    }

    // ─── Bubble (every client) ────────────────────────────────────────────────

    private void LateUpdate()
    {
        if (Owner == null) return;
        bool on = Owner.IsShielded && !Owner.IsDownAfterTag;
        shown = Mathf.MoveTowards(shown, on ? 1f : 0f, Time.deltaTime * 6f);
        if (shown <= 0f)
        {
            if (bubble != null) bubble.SetActive(false);
            return;
        }

        if (bubble == null) BuildBubble();
        bubble.SetActive(true);
        float pop = shown < 1f ? Mathf.Sin(shown * Mathf.PI * 0.5f) : 1f + Mathf.Sin(Time.time * 4f) * 0.02f;
        float size = Mathf.Max(2.2f, Owner.Height * 1.7f) * pop;
        bubble.transform.position   = transform.position + transform.up * (Owner.Height * 0.8f);
        bubble.transform.localScale = Vector3.one * size;

        // Your own bubble is seen from inside: fainter, so it tints rather than blocks the view
        Color c = bubbleColor;
        c.a *= IsLocal ? 0.45f : 1f;
        bubbleMaterial.color = c;
    }

    private void BuildBubble()
    {
        bubble = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bubble.name = "Shield Bubble";
        Destroy(bubble.GetComponent<Collider>());
        bubbleMaterial = new Material(Shader.Find("Sprites/Default")); // Two-sided, transparent, always in builds
        var r = bubble.GetComponent<Renderer>();
        r.sharedMaterial = bubbleMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    private void OnDestroy()
    {
        if (bubble != null) Destroy(bubble);
        if (bubbleMaterial != null) Destroy(bubbleMaterial);
    }
}
