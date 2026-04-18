using Photon.Pun;
using UnityEngine;

/// <summary>
/// Base for placement-style abilities: player aims with a hologram preview, then confirms
/// to place the real object or cancels to dismiss it.
/// State machine: Idle → PlacementMode (ability key down) → place on LMB / cancel on key again.
/// Hologram colour updates each frame to indicate valid (blue) or invalid (red) placement.
/// Child classes override EnterPlacementMode or add post-placement logic via AttemptPlacement.
/// Attach to: ThePlayer prefab — requires a cameraHolder reference and a hologramMaterial in
/// the Inspector, plus a matching prefab at Resources/Object/[abilityName].
/// </summary>
public abstract class TrapAbility : Ability
{
    protected override void Awake()
    {
        base.Awake();
        abilityType = AbilityType.Trap;
    }

    // ─── Inspector ────────────────────────────────────────────────────────────
    [Header("Object Settings")]
    protected GameObject objectPrefab;
    [SerializeField] protected float placingDistance = 15f;
    [SerializeField] protected LayerMask placementLayers;

    [Header("Hologram Settings")]
    [SerializeField] protected Material hologramMaterial;
    [SerializeField] private Color validColor = new Color(0, 0.5f, 1f, 0.5f);
    [SerializeField] private Color invalidColor = new Color(1f, 0, 0, 0.5f);

    [Header("Camera Reference")]
    [SerializeField] private GameObject cameraHolder;

    [Header("Modifiers")]
    public float cooldownTime;

    // ─── State ────────────────────────────────────────────────────────────────
    protected GameObject hologramObject;
    protected bool isPlacementMode = false;
    protected bool canPlace = false;
    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private float lastUseTime;
    protected bool isLocalPlayer = false;
    public override bool IsAwaitingAction => isPlacementMode;

    public override void TryActivate(AbilityInputEvent inputEvent)
    {
        if (!isLocalPlayer) return;
        if (inputEvent != AbilityInputEvent.Down) return;

        if (!isPlacementMode)
        {
            if (CanActivate()) EnterPlacementMode();
            else Debug.Log($"{abilityName} on cooldown ({CooldownRemaining():0.0}s left)");
        }
        else
        {
            // X pressed again while hologram is out = cancel
            CancelPlacement();
        }

        lastUseTime = Time.time;
    }

    public override void OnActionConfirm()
    {
        if (isPlacementMode)
            AttemptPlacement();
    }

    public override void OnActionCancel()
    {
        CancelPlacement();
    }

    private void CancelPlacement()
    {
        if (hologramObject != null)
        {
            PhotonNetwork.Destroy(hologramObject);
            hologramObject = null;
        }
        isPlacementMode = false;
    }

    // --- Logic Gate ---
    public bool CanActivate()
    {
        return Time.time >= lastUseTime + cooldownTime;
    }

    // --- Cooldown Helper ---
    public float CooldownRemaining()
    {
        return Mathf.Max(0, (lastUseTime + cooldownTime) - Time.time);
    }

    private void Update()
    {
        if (isLocalPlayer && isPlacementMode)
        {
            UpdateHologramPosition();
        }
    }

    protected virtual void EnterPlacementMode()
    {
        isPlacementMode = true;
        
        // Create hologram object
        hologramObject = PhotonNetwork.Instantiate("Object/"+abilityName, new(0, 0, 0), Quaternion.identity);
        
        // Apply hologram material to all renderers
        Renderer[] renderers = hologramObject.GetComponentsInChildren<Renderer>();
        foreach (Renderer renderer in renderers)
        {
            Material[] mats = new Material[renderer.materials.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                mats[i] = hologramMaterial;
            }
            renderer.materials = mats;
        }
        
        // Disable colliders on hologram
        Collider[] colliders = hologramObject.GetComponentsInChildren<Collider>();
        foreach (Collider col in colliders)
        {
            col.enabled = false;
        }
        
        // Disable any rigidbodies
        Rigidbody rb = hologramObject.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
        }
    }

    private void UpdateHologramPosition()
    {
        if (cameraHolder == null)
        {
            Debug.LogError("Player camera is null in UpdateHologramPosition!");
            return;
        }
        
        // Alternative approach: Use camera's transform forward direction
        Ray ray = new Ray(cameraHolder.transform.position, cameraHolder.transform.forward);
        RaycastHit hit;
        
        if (Physics.Raycast(ray, out hit, placingDistance, placementLayers))
        {
            // Orient the rotations with respect to the camera. Account for incorrect nominal rotation
            Quaternion normalRotation = Quaternion.FromToRotation(Vector3.forward, hit.normal);
            Quaternion offsetRotation = Quaternion.Euler(0, 0, cameraHolder.transform.eulerAngles.y - 90f);
            targetRotation = normalRotation * offsetRotation;

            // Get the object bounds to offset it properly
            Renderer renderer = hologramObject.GetComponent<Renderer>();
            if (renderer == null)
            {
                renderer = hologramObject.GetComponentInChildren<Renderer>();
            }
            
            if (renderer != null)
            {
                // First, temporarily position and rotate the hologram to get accurate bounds
                hologramObject.transform.rotation = targetRotation;
                hologramObject.transform.position = hit.point;
                
                // Get the object's local size 
                Bounds localBounds = renderer.bounds;
                
                // The transform's nominal is on the side of the object
                // This offset center's the object's nominal
                Vector3 pivotOffset = hit.point - localBounds.center;
                Debug.DrawRay(pivotOffset, Vector3.up, Color.blue, 1f);
                
                // Calculate the offset distance along the surface normal so the object is not inside the object
                Vector3 objectSize = localBounds.size; // (will be (2, 2, 2) for this object)
                float offset = Mathf.Abs(objectSize.x * Vector3.Dot(Vector3.right, hit.normal)) / 2f +
                               Mathf.Abs(objectSize.y * Vector3.Dot(Vector3.up, hit.normal)) / 2f +
                               Mathf.Abs(objectSize.z * Vector3.Dot(Vector3.forward, hit.normal)) / 2f + 0.2f;
                
                // Position the object on the surface, offset along the normal and compensate for pivot
                targetPosition = hit.point + hit.normal * offset + pivotOffset;
            }
            else
            {
                targetPosition = hit.point;
            }
            
            // Check if placement is valid (no overlapping objects)
            canPlace = IsValidPlacement(targetPosition);
            
            // Update hologram position and color
            hologramObject.transform.position = targetPosition;
            hologramObject.transform.rotation = targetRotation;
            UpdateHologramColor(canPlace);
        }
        else
        {
            // No valid surface in range
            canPlace = false;
            
            // Position hologram at max distance
            targetPosition = ray.origin + ray.direction * placingDistance;
            hologramObject.transform.position = targetPosition;
            UpdateHologramColor(false);
        }
    }

    private bool IsValidPlacement(Vector3 position)
    {
        // Get the bounds of the object
        Renderer renderer = hologramObject.GetComponent<Renderer>();
        if (renderer == null)
        {
            renderer = hologramObject.GetComponentInChildren<Renderer>();
        }
        
        if (renderer != null)
        {
            //Bounds bounds = renderer.bounds;
            BoxCollider boxCol = hologramObject.GetComponent<BoxCollider>();
            if (boxCol == null)
            {
                boxCol = hologramObject.GetComponentInChildren<BoxCollider>();
            }
            
            Vector3 halfExtents;
            Vector3 center;
            
            if (boxCol != null)
            {
                // Use the collider's actual size (in local space)
                halfExtents = boxCol.size / 2f * 0.9f; // Slight reduction for better detection
                center = hologramObject.transform.TransformPoint(boxCol.center);
            }
            else
            {
                // Fallback: Use your hardcoded values
                halfExtents = new Vector3(0.9f, 5.9f, 0.1f);
                center = position;
            }
            
            // Check for overlapping colliders
            Collider[] overlaps = Physics.OverlapBox(center, halfExtents, targetRotation);
            
            // Filter out the hologram itself
            foreach (Collider col in overlaps)
            {
                if (col.gameObject != hologramObject && !col.gameObject.transform.IsChildOf(hologramObject.transform))
                {
                    return false;
                }
            }
        }
        
        return true;
    }

    private void UpdateHologramColor(bool valid)
    {
        Color targetColor = valid ? validColor : invalidColor;
        
        Renderer[] renderers = hologramObject.GetComponentsInChildren<Renderer>();
        foreach (Renderer renderer in renderers)
        {
            foreach (Material mat in renderer.materials)
            {
                mat.color = targetColor;
            }
        }
    }

    protected void AttemptPlacement()
    {
        if (canPlace)
        {
            // Place the actual object
            GameObject placedObject = PhotonNetwork.Instantiate("Object/"+abilityName, targetPosition, targetRotation);
            
            // Destroy hologram and exit placement mode
            Destroy(hologramObject);
            isPlacementMode = false;
            hologramObject = null;
        }
        // If can't place, stay in placement mode (don't change states)
    }

    private void OnDisable()
    {
        // Clean up if ability is disabled while in placement mode
        if (hologramObject != null)
        {
            Destroy(hologramObject);
            isPlacementMode = false;
        }
    }

    // --- To be defined by children ---
    protected virtual void OnKeyDown() { }   
    //protected virtual void OnKeyUp() { }
}
