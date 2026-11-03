using Photon.Pun;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Base for placeable objects: the ability key shows a local hologram that snaps to surfaces
/// (blue = valid, red = blocked), Action places the real networked object, and the ability key
/// again cancels. Cooldown starts only after a successful placement.
/// The object spawned is Resources/Object/{Id}; most traps need only an empty subclass.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public abstract class TrapAbility : Ability
{
    [Header("Placement")]
    [FormerlySerializedAs("cooldownTime")]
    [SerializeField] protected float cooldown;
    [TunableParam("Placing Distance", 2f, 50f)]
    [SerializeField] protected float placingDistance = 15f;
    [Tooltip("Multiplier passed to the placed object (strength, radius…).")]
    [TunableParam("Power", 0.1f, 10f)]
    [SerializeField] protected float power = 1f;
    [SerializeField] protected LayerMask placementLayers;

    [Header("Hologram")]
    [SerializeField] protected Material hologramMaterial;
    [SerializeField] private Color validColor   = new(0f, 0.5f, 1f, 0.5f);
    [SerializeField] private Color invalidColor = new(1f, 0f, 0f, 0.5f);

    [Header("Camera Reference")]
    [SerializeField] private GameObject cameraHolder;

    private GameObject  hologram;
    private Renderer    hologramRenderer;
    private BoxCollider hologramBox;
    private bool        placing;
    private bool        canPlace;
    private bool?       shownValid; // Last tint applied, so materials are only touched on change
    private Vector3     targetPosition;
    private Quaternion  targetRotation;

    private static readonly Collider[] overlapBuffer = new Collider[8];

    public override AbilitySlot Slot             => AbilitySlot.Trap;
    public override float       BaseCooldown     => cooldown;
    public override bool        UsesActionKey    => true;
    public override bool        IsAwaitingAction => placing;

    // ─── Input ────────────────────────────────────────────────────────────────

    public override void HandleInput(AbilityInputEvent inputEvent)
    {
        if (inputEvent != AbilityInputEvent.Down) return;

        if (placing)    Cancel();
        else if (IsReady) EnterPlacement();
    }

    public override void OnActionDown()
    {
        if (placing) TryPlace();
    }

    public override void Cancel()
    {
        placing = false;
        if (hologram != null) Destroy(hologram);
        hologram = null;
    }

    // ─── Placement ────────────────────────────────────────────────────────────

    private void EnterPlacement()
    {
        var prefab = Resources.Load<GameObject>(ResourcePath);
        if (prefab == null || cameraHolder == null)
        {
            Debug.LogWarning($"[{Id}] Missing prefab at Resources/{ResourcePath} or cameraHolder reference.");
            return;
        }

        hologram = VisualProxy.Create(prefab, null, keepDisabledColliders: true);
        VisualProxy.ApplyMaterial(hologram, hologramMaterial);
        hologramRenderer = hologram.GetComponentInChildren<Renderer>();
        hologramBox      = hologram.GetComponentInChildren<BoxCollider>(true);
        shownValid       = null;
        placing          = true;
        UpdateHologram();
    }

    private void Update()
    {
        if (placing) UpdateHologram();
    }

    private void UpdateHologram()
    {
        Transform view = cameraHolder.transform;
        var ray = new Ray(view.position, view.forward);

        if (!Physics.Raycast(ray, out RaycastHit hit, placingDistance, placementLayers, QueryTriggerInteraction.Ignore))
        {
            canPlace = false;
            hologram.transform.position = ray.origin + ray.direction * placingDistance;
            ShowValidity(false);
            return;
        }

        // Face away from the surface, rotated to match where the player is looking
        targetRotation = Quaternion.FromToRotation(Vector3.forward, hit.normal)
                       * Quaternion.Euler(0f, 0f, view.eulerAngles.y - 90f);
        targetPosition = hit.point;

        if (hologramRenderer != null)
        {
            // Measure at the hit point, then push out along the normal so it sits on the surface
            hologram.transform.SetPositionAndRotation(hit.point, targetRotation);
            Bounds bounds = hologramRenderer.bounds;
            Vector3 pivotOffset = hit.point - bounds.center;
            float   offset = Mathf.Abs(bounds.size.x * hit.normal.x) / 2f
                           + Mathf.Abs(bounds.size.y * hit.normal.y) / 2f
                           + Mathf.Abs(bounds.size.z * hit.normal.z) / 2f + 0.2f;
            targetPosition = hit.point + hit.normal * offset + pivotOffset;
        }

        hologram.transform.SetPositionAndRotation(targetPosition, targetRotation);
        canPlace = IsSpaceFree();
        ShowValidity(canPlace);
    }

    private void ShowValidity(bool valid)
    {
        if (shownValid == valid) return;
        shownValid = valid;
        VisualProxy.SetColor(hologram, valid ? validColor : invalidColor);
    }

    private bool IsSpaceFree()
    {
        if (hologramBox == null) return true;

        Transform box = hologramBox.transform;
        Vector3 center      = box.TransformPoint(hologramBox.center);
        Vector3 halfExtents = Vector3.Scale(hologramBox.size, box.lossyScale) * 0.45f; // 90% of half size
        int hits = Physics.OverlapBoxNonAlloc(center, halfExtents, overlapBuffer, box.rotation,
                                              Physics.AllLayers, QueryTriggerInteraction.Ignore);
        return hits == 0;
    }

    private void TryPlace()
    {
        if (!canPlace) return;

        PhotonNetwork.Instantiate(ResourcePath, targetPosition, targetRotation, 0,
            SpawnData.Create(Vector3.zero, power * PowerMultiplier, Owner.photonView.ViewID, ObjectTunableOverrides()));

        Cancel();
        StartCooldown();
    }
}
