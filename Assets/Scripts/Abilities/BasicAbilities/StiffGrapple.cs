using UnityEngine;
using Photon.Pun;

[RequireComponent(typeof(PhotonView))]
public class StiffGrapple : BasicAbility
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
    public float pushPullSpeed = 50f;
    public float upwardBoostForce = 20f;  // Upward force applied when pushing from below
    public float springyness = 0.5f;  // Low spring for stiff feel while allowing pull/push control
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
        if (player.currentState == Player.MovementState.Prone || player.currentState == Player.MovementState.Hang) return;
        player.IsSwinging = true;
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

            joint.maxDistance = distanceFromPoint;
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
        
        // Apply force away from the joint to actively lift
        if (rb != null && playerTransform != null)
        {
            Vector3 directionFromJoint = (playerTransform.position - swingPoint).normalized;
            rb.AddForce(directionFromJoint * upwardBoostForce, ForceMode.Acceleration);
        }
    }

    private string GetTransformPath(Transform target)
    {
        if (target == null) return "";
        
        string path = target.name;
        Transform current = target.parent;
        
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }
        
        return path;
    }

    private Transform FindTransformByPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        
        string[] pathParts = path.Split('/');
        GameObject[] rootObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
        
        Transform current = null;
        foreach (GameObject root in rootObjects)
        {
            if (root.name == pathParts[0])
            {
                current = root.transform;
                break;
            }
        }
        
        if (current == null) return null;
        
        for (int i = 1; i < pathParts.Length; i++)
        {
            current = current.Find(pathParts[i]);
            if (current == null) return null;
        }
        
        return current;
    }

    private void DrawRope()
    {
        if (!isSwinging) return;

        Vector3 startPos;
        if (view.IsMine)
            startPos = gunTip.position;
        else
            startPos = remoteGunTip.position;
        
        Vector3 endPos = swingPoint;

        lr.SetPosition(0, startPos);
        lr.SetPosition(1, endPos);
    }

    private void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
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
