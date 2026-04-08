using Photon.Pun;
using UnityEngine;

public class MovingPlatform : MonoBehaviourPun
{
    [Header("Movement Settings")]
    [SerializeField] private Transform pointA;
    [SerializeField] private Transform pointB;
    [SerializeField] private float speed = 4f;
    [SerializeField] private bool startAtA = true;
    [SerializeField] private bool waitAtEnds = false;
    [SerializeField] private float waitTime = 1f;

    private Vector3 targetPosition;
    private bool isWaiting;
    private PhotonView view;

    // Networked position for remote clients
    private Vector3 networkPosition;
    private Vector3 networkTargetPosition;
    private float networkSmoothing = 10f;
    private bool isAuthoritative;

    private void Start()
    {
        view = GetComponent<PhotonView>();
        // Owner decides the initial target and position. Remote clients will receive updates.
        targetPosition = startAtA ? pointB.position : pointA.position;
        networkPosition = transform.position;
        networkTargetPosition = targetPosition;
        // Determine who drives the platform: make the MasterClient authoritative for consistent behavior
        isAuthoritative = PhotonNetwork.IsMasterClient;
    }

    private void FixedUpdate()
    {
        // Only the MasterClient should drive authoritative movement. Non-master clients will lerp toward the last received network position.
        if (view == null) return;

        if (isAuthoritative)
        {
            if (isWaiting) return;

            // Move toward the target (owner)
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, speed * Time.deltaTime);

            // When we arrive, switch target
            if (Vector3.Distance(transform.position, targetPosition) < 0.05f)
            {
                if (waitAtEnds)
                    StartCoroutine(WaitThenSwitch());
                else
                    SwitchTarget();
            }
            // Broadcast authoritative position to others
            if (PhotonNetwork.IsConnected)
            {
                // Send position and target to remote clients
                photonView.RPC("RPC_UpdateState", RpcTarget.Others, transform.position, targetPosition);
            }
        }
        else
        {
            // Remote clients: smooth towards the last received network position
            transform.position = Vector3.Lerp(transform.position, networkPosition, Mathf.Clamp01(Time.deltaTime * networkSmoothing));
        }
    }

    private System.Collections.IEnumerator WaitThenSwitch()
    {
        isWaiting = true;
        yield return new WaitForSeconds(waitTime);
        SwitchTarget();
        isWaiting = false;
    }

    private void SwitchTarget()
    {
        targetPosition = (targetPosition == pointA.position) ? pointB.position : pointA.position;
        // When owner switches target, also update the networkTargetPosition so remotes can know intent if needed
        networkTargetPosition = targetPosition;
    }

    

    private void OnDrawGizmos()
    {
        if (pointA != null && pointB != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(pointA.position, pointB.position);
            Gizmos.DrawSphere(pointA.position, 0.5f);
            Gizmos.DrawSphere(pointB.position, 0.5f);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        // When player lands on the platform, parent them
        if (collision.collider.CompareTag("Player"))
        {
            // Always use a follower component rather than parenting. Parenting networked players often causes desyncs.
            var playerRoot = collision.collider.transform.root;
            var follower = playerRoot.GetComponent<PlatformFollower>();
            if (follower == null)
                follower = playerRoot.gameObject.AddComponent<PlatformFollower>();
            follower.AttachToPlatform(this.transform);
        }
    }

    private void OnCollisionExit(Collision collision)
    {
        // When player leaves, unparent them
        if (collision.collider.CompareTag("Player"))
        {
            var playerRoot = collision.collider.transform.root;
            var follower = playerRoot.GetComponent<PlatformFollower>();
            if (follower != null)
            {
                follower.DetachFromPlatform();
                // Optionally destroy the follower component to keep the hierarchy clean
                Destroy(follower);
            }
        }
    }
    [PunRPC]
    private void RPC_UpdateState(Vector3 pos, Vector3 targ)
    {
        networkPosition = pos;
        networkTargetPosition = targ;
        // Snap if too far
        if (Vector3.Distance(transform.position, networkPosition) > 2f)
        {
            transform.position = networkPosition;
        }
    }

    // A small helper component attached to players that need to follow platform movement (works for CharacterController and Rigidbody)
    private class PlatformFollower : MonoBehaviour
    {
        private Transform platform;
        private Vector3 lastPlatformPos;
        private bool attached = false;
        private CharacterController controller;
        private Rigidbody rb;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            rb = GetComponent<Rigidbody>();
        }

        public void AttachToPlatform(Transform plat)
        {
            platform = plat;
            lastPlatformPos = platform.position;
            attached = true;
        }

        public void DetachFromPlatform()
        {
            attached = false;
            platform = null;
        }

        private void LateUpdate()
        {
            if (!attached || platform == null) return;
            Vector3 delta = platform.position - lastPlatformPos;
            if (delta != Vector3.zero)
            {
                if (controller != null)
                {
                    // CharacterController expects Move for proper collision handling
                    controller.Move(delta);
                }
                else if (rb != null)
                {
                    // Rigidbody: use MovePosition to respect physics; approximate here (fine for small deltas)
                    rb.MovePosition(rb.position + delta);
                }
                else
                {
                    // Fallback: move transform
                    transform.position += delta;
                }
            }
            lastPlatformPos = platform.position;
        }
    }
}
