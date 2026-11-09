using Photon.Pun;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Base for placeable objects: the ability key shows a local hologram that snaps to surfaces
/// (blue = valid, red = blocked), Action places the real networked object, and the ability key
/// again cancels. Cooldown starts only after a successful placement.
/// The object spawned is Resources/Object/{Id}; most traps need only an empty subclass.
/// Hooks for special traps: <see cref="IsValidSurface"/> (e.g. walls only), <see cref="PlacementRotation"/>
/// and <see cref="OnPlaced"/> (keep a reference to what was placed).
/// Limits: at most <see cref="maxPlaced"/> of this player's objects exist at once (placing another removes
/// the oldest), and they are all cleared when a new round starts (the Countdown phase).
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

    [Tooltip("Most of this player's objects that can exist at once; placing another removes the oldest. 0 = no limit.")]
    [SerializeField, Min(0)] protected int maxPlaced = 0;

    [Header("Hologram")]
    [SerializeField] protected Material hologramMaterial;
    [SerializeField] private Color validColor   = new(0f, 0.5f, 1f, 0.5f);
    [SerializeField] private Color invalidColor = new(1f, 0f, 0f, 0.5f);

    [Header("Camera Reference")]
    [SerializeField] private GameObject cameraHolder;

    private GameObject  hologram;
    private Renderer[]  hologramRenderers;
    private BoxCollider hologramBox;
    private bool        placing;
    private bool        canPlace;
    private bool?       shownValid; // Last tint applied, so materials are only touched on change
    private Vector3     targetPosition;
    private Quaternion  targetRotation;

    private static readonly Collider[] overlapBuffer = new Collider[8];

    // ─── Placed objects (owner) ───────────────────────────────────────────────
    private readonly System.Collections.Generic.List<GameObject> placedObjects = new();
    private GameModeManager watchedManager;

    /// <summary>The most that can be out at once (0 = no limit), for descriptions.</summary>
    public int MaxPlaced => maxPlaced;

    private void Start()
    {
        watchedManager = GameModeManager.Instance;
        if (watchedManager != null) watchedManager.PhaseChanged += OnPhaseChanged;
    }

    private void OnDestroy()
    {
        if (watchedManager != null) watchedManager.PhaseChanged -= OnPhaseChanged;
    }

    // A new round starts clean: this player's traps from the last round go
    private void OnPhaseChanged(MatchPhase phase)
    {
        if (phase != MatchPhase.Countdown || !IsLocal) return;
        foreach (var placed in placedObjects)
            if (placed != null) PhotonNetwork.Destroy(placed);
        placedObjects.Clear();
    }

    private void Track(GameObject placed)
    {
        placedObjects.RemoveAll(o => o == null); // Used up or removed already (mouse trap, ice trap…)
        placedObjects.Add(placed);
        while (maxPlaced > 0 && placedObjects.Count > maxPlaced)
        {
            if (placedObjects[0] != null) PhotonNetwork.Destroy(placedObjects[0]);
            placedObjects.RemoveAt(0);
        }
    }

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
        hologramRenderers = hologram.GetComponentsInChildren<Renderer>();
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

        if (!FirstSurface(ray, placingDistance, placementLayers, out RaycastHit hit))
        {
            canPlace = false;
            hologram.transform.position = ray.origin + ray.direction * placingDistance;
            ShowValidity(false);
            return;
        }

        targetRotation = PlacementRotation(hit, view);
        targetPosition = hit.point;

        if (hologramRenderers.Length > 0)
        {
            // Measure every part at the hit point, then push out along the normal so it sits on the surface
            hologram.transform.SetPositionAndRotation(hit.point, targetRotation);
            Bounds bounds = hologramRenderers[0].bounds;
            foreach (var r in hologramRenderers) bounds.Encapsulate(r.bounds);
            Vector3 pivotOffset = hit.point - bounds.center;
            float   offset = Mathf.Abs(bounds.size.x * hit.normal.x) / 2f
                           + Mathf.Abs(bounds.size.y * hit.normal.y) / 2f
                           + Mathf.Abs(bounds.size.z * hit.normal.z) / 2f + SurfaceGap;
            targetPosition = hit.point + hit.normal * offset + pivotOffset;
        }

        hologram.transform.SetPositionAndRotation(targetPosition, targetRotation);
        canPlace = IsValidSurface(hit.normal) && IsSpaceFree();
        ShowValidity(canPlace);
    }

    // The first surface along the ray that isn't a player (traps never stick to people, nor to the placer)
    private static bool FirstSurface(Ray ray, float distance, LayerMask mask, out RaycastHit surface)
    {
        surface = default;
        float nearest = float.MaxValue;
        foreach (var h in Physics.RaycastAll(ray, distance, mask, QueryTriggerInteraction.Ignore))
        {
            if (h.distance >= nearest || h.collider.GetComponentInParent<Player>() != null) continue;
            nearest = h.distance;
            surface = h;
        }
        return nearest < float.MaxValue;
    }

    // A hair off the surface, so the free-space check never counts the surface itself
    private const float SurfaceGap = 0.02f;

    /// <summary>Whether the trap may go on a surface facing <paramref name="normal"/> (all surfaces by default).</summary>
    protected virtual bool IsValidSurface(Vector3 normal) => true;

    /// <summary>Default: faces away from the surface (+Z along the normal), turned to match where the player looks.</summary>
    protected virtual Quaternion PlacementRotation(RaycastHit hit, Transform view) =>
        Quaternion.FromToRotation(Vector3.forward, hit.normal) * Quaternion.Euler(0f, 0f, view.eulerAngles.y - 90f);

    /// <summary>Owner, right after placing: the networked object that was spawned.</summary>
    protected virtual void OnPlaced(GameObject placed) { }

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

        var placed = PhotonNetwork.Instantiate(ResourcePath, targetPosition, targetRotation, 0,
            SpawnData.Create(Vector3.zero, power * Owner.AbilityPower, Owner.photonView.ViewID, ObjectTunableOverrides()));

        Cancel();
        StartCooldown();
        if (placed != null) Track(placed);
        OnPlaced(placed);
    }
}
