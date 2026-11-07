using Photon.Pun;
using UnityEngine;

/// <summary>
/// Shared grapple implementation: raycast from the camera, attach a SpringJoint to the hit
/// point (following moving objects), and draw a rope while the key is held. There is no reeling:
/// the rope keeps its length unless a variant changes it (<see cref="WhileAttached"/>).
/// Swing state is synced so remote players see the rope from the third-person gun tip.
///
/// Variants: SwingGrapple (plain tether), SpringyGrapple (bouncy, starts short), ZipGrapple (pulls the
/// player to the anchor while held).
/// </summary>
[RequireComponent(typeof(PhotonView))]
public abstract class GrappleAbility : BasicAbility, IPunObservable
{
    [Header("References")]
    [SerializeField] protected Transform    cam;
    [SerializeField] protected Transform    gunTip;
    [SerializeField] protected Transform    remoteGunTip;
    [SerializeField] protected LayerMask    canGrapple;
    [SerializeField] protected GameObject   rope;
    [SerializeField] protected LineRenderer lr;

    [Header("Modifiers")]
    [TunableParam("Reach", 10f, 500f)]
    [SerializeField] protected float grappleReach = 200f;
    [TunableParam("Springiness", 0f, 50f)]
    [SerializeField] protected float springyness = 4.5f;

    /// <summary>True while attached, on every client (synced), so everyone sees the gun out.</summary>
    public bool IsSwinging => isSwinging;

    // The gun is out only while this grapple is the one in use (an unequipped grapple is disabled)
    public override bool ShowsGrappleGun => enabled && isSwinging;

    /// <summary>The joint while attached (local player only), for variants that change the rope.</summary>
    protected SpringJoint Joint => joint;

    /// <summary>Local player, every physics step while attached: variants pull or adjust the rope here.</summary>
    protected virtual void WhileAttached(Vector3 anchor) { }

    /// <summary>Rope length at attach time as a fraction of the distance to the anchor.</summary>
    protected virtual float InitialLengthFactor => 1f;

    private const float Damper    = 2f;
    private const float MassScale = 30f;

    private PhotonView  view;
    private SpringJoint joint;
    private bool        isSwinging;
    private bool        drawingRope;
    private Vector3     swingPoint;
    private Transform   attachedTransform;
    private Vector3     localAttachPoint;
    private float       startDistance;

    protected override void Awake()
    {
        base.Awake();
        view = GetComponent<PhotonView>();

        if (!view.ObservedComponents.Contains(this))
            view.ObservedComponents.Add(this);
    }

    // ─── Input ────────────────────────────────────────────────────────────────

    protected override void OnPress()
    {
        if (isSwinging || Owner.State == Player.MovementState.Hang) return;
        if (!Physics.Raycast(cam.position, cam.forward, out RaycastHit hit, grappleReach, canGrapple)) return;

        // Grappling stands the player up, except prone which allows steering a slide
        if (Owner.State != Player.MovementState.Prone)
            Owner.SetState(Player.MovementState.Idle);

        swingPoint        = hit.point;
        attachedTransform = hit.transform;
        localAttachPoint  = attachedTransform.InverseTransformPoint(hit.point);
        startDistance     = Vector3.Distance(transform.position, swingPoint);

        joint = gameObject.AddComponent<SpringJoint>();
        joint.autoConfigureConnectedAnchor = false;
        joint.connectedAnchor = swingPoint;
        joint.maxDistance     = startDistance * InitialLengthFactor;
        joint.minDistance     = 0f;
        joint.spring          = springyness;
        joint.damper          = Damper;
        joint.massScale       = MassScale;

        isSwinging       = true;
        IsActive         = true;
        Owner.IsSwinging = true;
    }

    protected override void Deactivate()
    {
        if (joint != null) Destroy(joint);
        joint             = null;
        attachedTransform = null;
        isSwinging        = false;
        IsActive          = false;
        if (Owner != null && IsLocal) Owner.IsSwinging = false;
    }

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    protected override void FixedUpdate()
    {
        base.FixedUpdate();
        if (!IsLocal || joint == null) return;

        joint.spring = springyness; // Follows the Cheats slider
        WhileAttached(swingPoint);
    }

    private void LateUpdate()
    {
        // Follow moving anchors
        if (isSwinging && joint != null && attachedTransform != null)
        {
            swingPoint = attachedTransform.TransformPoint(localAttachPoint);
            joint.connectedAnchor = swingPoint;
        }

        DrawRope();
    }

    // An unequipped grapple must not leave its rope on screen (drawing stops while disabled)
    protected override void OnDisable()
    {
        base.OnDisable();
        isSwinging = false;
        if (!drawingRope || rope == null || lr == null) return;
        drawingRope      = false;
        lr.positionCount = 0;
        rope.SetActive(false);
    }

    // The rope object is shared by every grapple, so only touch it on state changes
    /// <summary>The rope's colour for this variant (null: the rope's own look).</summary>
    protected virtual Color? RopeColor => null;

    // The rope is shared by every grapple on the player: each sets (or puts back) its colour when it starts drawing
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId     = Shader.PropertyToID("_Color");
    private Color? ropeOriginal;
    private Gradient ropeGradient;

    private void ApplyRopeColor()
    {
        var mat = lr.material; // An instance per player's rope
        int id = mat.HasProperty(BaseColorId) ? BaseColorId : mat.HasProperty(ColorId) ? ColorId : -1;
        ropeOriginal ??= id >= 0 ? mat.GetColor(id) : Color.white;
        ropeGradient ??= lr.colorGradient;
        if (RopeColor.HasValue)
        {
            if (id >= 0) mat.SetColor(id, RopeColor.Value);
            lr.startColor = lr.endColor = RopeColor.Value;
        }
        else
        {
            if (id >= 0) mat.SetColor(id, ropeOriginal.Value);
            lr.colorGradient = ropeGradient;
        }
    }

    private void DrawRope()
    {
        if (rope == null || lr == null) return;

        if (!isSwinging)
        {
            if (!drawingRope) return;
            drawingRope      = false;
            lr.positionCount = 0;
            rope.SetActive(false);
            return;
        }

        if (!drawingRope)
        {
            drawingRope = true;
            rope.SetActive(true);
            ApplyRopeColor();
            lr.positionCount = 2;
        }

        Transform tip = view.IsMine ? gunTip : remoteGunTip;
        if (tip != null) lr.SetPosition(0, tip.position);
        lr.SetPosition(1, swingPoint);
    }

    // ─── Network Sync ─────────────────────────────────────────────────────────

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            // An unequipped grapple never reports a swing, so other clients can't show its rope or gun
            stream.SendNext(enabled && isSwinging);
            stream.SendNext(swingPoint);
        }
        else
        {
            isSwinging = (bool)stream.ReceiveNext() && enabled;
            swingPoint = (Vector3)stream.ReceiveNext();
        }
    }
}
