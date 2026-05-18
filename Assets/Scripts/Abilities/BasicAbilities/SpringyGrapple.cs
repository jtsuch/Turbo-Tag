using UnityEngine;
using Photon.Pun;

[RequireComponent(typeof(PhotonView))]
public class SpringyGrapple : BasicAbility, IPunObservable
{
    [Header("References")]
    public Player player;
    public Transform cam, playerTransform, gunTip, remoteGunTip;
    public LayerMask canGrapple;
    public GameObject rope;
    public LineRenderer lr;
    public InputHandler inputHandler;
    private PhotonView view;
    // InputHandler is available through Ability.inputHandler (inherited)

    [Header("Modifiers")]
    public float grappleReach = 200f;
    public float pushPullSpeed = 15f;
    public float springyness = 10f;
    public Color startRopeColor;
    public Color endRopeColor;

    private bool isSwinging = false;
    private SpringJoint joint;
    private Vector3 swingPoint;
    private Transform attachedTransform;
    private Vector3 localAttachPoint;
    private float distanceFromPoint;
    
    protected override void Awake()
    {
        base.Awake();
        inputHandler = GetComponent<InputHandler>();

        rope.SetActive(false);
        lr.positionCount = 0;
        view = GetComponent<PhotonView>();

        if (view != null && !view.ObservedComponents.Contains(this))
            view.ObservedComponents.Add(this);
    }

    protected override void OnKeyDown()
    {   
        StartSwing();
    }
        
    protected override void OnKeyUp()
    {
        StopAbility();
    }
    
    private void Update()
    {
        if (joint == null || !view.IsMine || pm == null) return;

        if (inputHandler != null && inputHandler.Sprint)
        {
            PullUp();
        }
        else if (inputHandler != null && inputHandler.Down)
        {
            PushDown();
        }
    }

    private void LateUpdate()
    {
        UpdateGrapplePoint();
        DrawRope();
    }

    private void UpdateGrapplePoint()
    {
        if (!isSwinging || joint == null) return;

        if (attachedTransform != null)
        {
            swingPoint = attachedTransform.TransformPoint(localAttachPoint);
            joint.connectedAnchor = swingPoint;
        }
    }

    private void StartSwing()
    {
        if (player == null) return;
        if (player.currentState == Player.MovementState.Hang) return;
        player.IsSwinging = true;

        if (player.currentState != Player.MovementState.Prone)
            player.SetState(Player.MovementState.Idle);

        if (Physics.Raycast(cam.position, cam.forward, out RaycastHit hit, grappleReach, canGrapple))
        {
            swingPoint = hit.point;
            attachedTransform = hit.transform;
            localAttachPoint = attachedTransform.InverseTransformPoint(hit.point);

            rope.SetActive(true);
            isSwinging = true;
            isActive = true;

            joint = player.gameObject.AddComponent<SpringJoint>();
            joint.autoConfigureConnectedAnchor = false;
            joint.connectedAnchor = swingPoint;

            distanceFromPoint = Vector3.Distance(playerTransform.position, swingPoint);

            joint.maxDistance = distanceFromPoint * 0.8f;
            joint.minDistance = 0f;

            joint.spring = springyness;
            joint.damper = 2f;
            joint.massScale = 30f;

            lr.positionCount = 2;
        }
    }

    protected override void StopAbility()
    {
        if (player != null) player.IsSwinging = false;
        lr.positionCount = 0;
        rope.SetActive(false);
        Destroy(joint);
        isSwinging = false;
        isActive = false;
        attachedTransform = null;
    }

    public void PullUp()
    {
        if (distanceFromPoint < joint.maxDistance)
            joint.maxDistance = distanceFromPoint;
        joint.maxDistance -= pushPullSpeed * Time.deltaTime;
    }

    public void PushDown()
    {
        joint.maxDistance += pushPullSpeed * Time.deltaTime;
    }

    private void DrawRope()
    {
        if (!isSwinging)
        {
            if (lr.positionCount != 0) lr.positionCount = 0;
            if (rope.activeSelf) rope.SetActive(false);
            return;
        }

        if (!rope.activeSelf) rope.SetActive(true);
        if (lr.positionCount != 2) lr.positionCount = 2;

        lr.SetPosition(0, view.IsMine ? gunTip.position : remoteGunTip.position);
        lr.SetPosition(1, swingPoint);
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo _)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(isSwinging);
            stream.SendNext(swingPoint);
        }
        else
        {
            isSwinging = (bool)stream.ReceiveNext();
            swingPoint = (Vector3)stream.ReceiveNext();
        }
    }
}
