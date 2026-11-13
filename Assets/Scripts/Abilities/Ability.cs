using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base for every player ability. Concrete abilities derive from one of the slot bases —
/// BasicAbility, QuickAbility, ThrowAbility or TrapAbility — which define the input flow.
///
/// Identity is the class name (<see cref="Id"/>): it is what loadouts store, what the Pregame
/// toggles reference, and the name of any prefab at Resources/Object/{Id}.
///
/// All abilities live as components on the player prefab; AbilityHandler enables only the
/// ones in the owner's loadout and routes input to them.
/// Attach to: ThePlayer prefab — one component per ability.
/// </summary>
[RequireComponent(typeof(Player))]
public abstract class Ability : MonoBehaviour
{
    public const string ObjectResourceFolder = "Object/";

    // ─── Identity ─────────────────────────────────────────────────────────────
    public string Id => GetType().Name;
    public abstract AbilitySlot Slot { get; }

    /// <summary>Resources path of this ability's spawned object (throwables and traps).</summary>
    public string ResourcePath => ObjectResourceFolder + Id;

    // ─── References ───────────────────────────────────────────────────────────
    protected Player         Owner     { get; private set; }
    protected Rigidbody      Body      { get; private set; }
    protected AbilityHandler Handler   { get; private set; }
    protected bool           IsLocal   => Owner != null && Owner.IsLocal;

    // ─── Cooldown ─────────────────────────────────────────────────────────────

    /// <summary>Unscaled cooldown in seconds. Slot bases expose this as a tunable field.</summary>
    public virtual float BaseCooldown => 0f;

    /// <summary>Cooldown after cheats, game mode and rule multipliers.</summary>
    public float CooldownDuration => BaseCooldown * (Owner != null ? Owner.CooldownMultiplier : 1f);

    // ─── Charges ──────────────────────────────────────────────────────────────
    // Quick, throw and trap abilities store up to MaxCharges uses; each use spends one and they come back
    // one at a time, a cooldown apart. One charge is the classic "use, then wait out the cooldown".

    [Tooltip("Uses stored at once (quick, throw and trap abilities). They recharge one per cooldown.")]
    [SerializeField, Min(1)] private int maxCharges = 1;

    public int MaxCharges => Mathf.Max(1, maxCharges);

    private int   charges = -1;   // -1 until first read (then full)
    private float rechargeStart;  // When the charge currently refilling started

    /// <summary>Uses available right now.</summary>
    public int Charges
    {
        get
        {
            Recharge();
            return charges;
        }
    }

    private void Recharge()
    {
        if (charges < 0) { charges = MaxCharges; rechargeStart = Time.time; }
        charges = Mathf.Min(charges, MaxCharges);

        float duration = CooldownDuration;
        if (duration <= 0f) { charges = MaxCharges; return; }
        if (!Regenerating) { if (charges >= MaxCharges) rechargeStart = Time.time; return; } // Spent charges stay spent
        while (charges < MaxCharges && Time.time - rechargeStart >= duration)
        {
            charges++;
            rechargeStart += duration;
        }
        if (charges >= MaxCharges) rechargeStart = Time.time; // Nothing refilling: the next use starts the clock
    }

    /// <summary>
    /// Whether spent charges and Basic meters come back over time right now (<see cref="GameMode.AbilitiesRegenerate"/>:
    /// Search &amp; Destroy's live rounds don't). Cheats with no cooldowns still refill.
    /// </summary>
    public static bool Regenerating
    {
        get
        {
            var manager = GameModeManager.Instance;
            return manager == null || manager.Mode == null || manager.Mode.AbilitiesRegenerate(manager.Phase);
        }
    }

    /// <summary>Seconds until a use is available (0 while any charge is left, or when nothing regenerates).</summary>
    public float CooldownRemaining => Charges > 0 || !Regenerating ? 0f : Mathf.Max(0f, rechargeStart + CooldownDuration - Time.time);

    /// <summary>Seconds until the next charge comes back (0 when full, or when nothing regenerates).</summary>
    public float NextChargeIn => Charges >= MaxCharges || !Regenerating ? 0f : Mathf.Max(0f, rechargeStart + CooldownDuration - Time.time);

    public bool IsReady => Charges > 0;

    /// <summary>0 = just used up, 1 = ready. Drives the HUD.</summary>
    public virtual float Readiness =>
        CooldownDuration <= 0f || Charges > 0 ? 1f : !Regenerating ? 0f : 1f - CooldownRemaining / CooldownDuration;

    /// <summary>Spends a charge (its cooldown starts), and counts a use for the scoreboard (quick, throw and trap abilities).</summary>
    protected void StartCooldown()
    {
        Recharge();
        if (charges > 0) charges--;
        CountUse();
    }

    /// <summary>Owner: every charge back at once (the Refill donut). Basic abilities also fill their meter.</summary>
    public virtual void RefillNow()
    {
        charges       = MaxCharges;
        rechargeStart = Time.time;
    }

    /// <summary>
    /// Gives a spent charge back <paramref name="delay"/> seconds from now (Spy Camera picked back up), or sooner
    /// if the normal recharge would bring it back first.
    /// </summary>
    protected void RefundCharge(float delay)
    {
        Recharge();
        if (charges >= MaxCharges) return;
        if (delay <= 0f || CooldownDuration <= 0f) { charges++; return; }
        rechargeStart = Mathf.Min(rechargeStart, Time.time + delay - CooldownDuration);
    }

    /// <summary>Owner: one more use of an ability, for the scoreboard's "Abilities Used".</summary>
    protected void CountUse()
    {
        if (IsLocal && Handler != null) Handler.CountUse(Id);
    }

    // ─── Input contract ───────────────────────────────────────────────────────

    /// <summary>True for abilities that enter an aim/place mode confirmed with the Action key.
    /// Only one such ability can be mid-flow at a time.</summary>
    public virtual bool UsesActionKey => false;

    /// <summary>True while the ability is mid-flow and waiting for the Action key (aiming/placing).</summary>
    public virtual bool IsAwaitingAction => false;

    public abstract void HandleInput(AbilityInputEvent inputEvent);

    /// <summary>Action key (LMB by default) pressed while this ability is awaiting action.</summary>
    public virtual void OnActionDown() { }

    /// <summary>Action key released while this ability is awaiting action.</summary>
    public virtual void OnActionUp() { }

    /// <summary>
    /// True while this ability has the grapple gun out (pistol, aiming arm and rope). Only grapples use the
    /// gun; other abilities have their own animations or objects and leave this false.
    /// </summary>
    public virtual bool ShowsGrappleGun => false;

    /// <summary>True while the right arm should be raised and aimed forward (the gun, or a held tool like the leaf blower).</summary>
    public virtual bool RaisesArm => ShowsGrappleGun;

    /// <summary>Immediately ends any in-progress use (pause, caught, conflicting ability).</summary>
    public virtual void Cancel() { }

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    protected virtual void Awake()
    {
        Owner   = GetComponent<Player>();
        Body    = GetComponent<Rigidbody>();
        Handler = GetComponent<AbilityHandler>();
        CaptureTunableDefaults();
    }

    protected virtual void OnDisable() => Cancel();

    // ─── Tunable parameters (cheats) ──────────────────────────────────────────
    private readonly Dictionary<string, float> tunableDefaults = new();

    private void CaptureTunableDefaults()
    {
        foreach (var (field, _) in TunableParamAttribute.FieldsOf(GetType()))
            tunableDefaults[field.Name] = (float)field.GetValue(this);
    }

    /// <summary>Restores every [TunableParam] field to its prefab value (cheats switched off).</summary>
    public void ResetTunables()
    {
        foreach (var (field, _) in TunableParamAttribute.FieldsOf(GetType()))
            if (tunableDefaults.TryGetValue(field.Name, out float value))
                field.SetValue(this, value);
        objectValues.Clear();
    }

    // ─── Object tunables (cheats for the spawned object) ──────────────────────
    private List<ObjectTunables.Entry> objectEntries;
    private readonly Dictionary<string, float> objectValues = new();

    /// <summary>[TunableParam] fields on this ability's spawned object (empty when it has none).</summary>
    public IReadOnlyList<ObjectTunables.Entry> ObjectTunableEntries =>
        objectEntries ??= ObjectTunables.Discover(Resources.Load<GameObject>(ResourcePath));

    public float GetObjectTunable(ObjectTunables.Entry entry) =>
        objectValues.TryGetValue(entry.Key, out float value) ? value : entry.Default;

    public void SetObjectTunable(ObjectTunables.Entry entry, float value) => objectValues[entry.Key] = value;

    /// <summary>The cheat values that differ from the prefab, for SpawnData (null when none).</summary>
    protected ExitGames.Client.Photon.Hashtable ObjectTunableOverrides()
    {
        ExitGames.Client.Photon.Hashtable table = null;
        foreach (var entry in ObjectTunableEntries)
        {
            if (!objectValues.TryGetValue(entry.Key, out float value) || Mathf.Approximately(value, entry.Default)) continue;
            table ??= new ExitGames.Client.Photon.Hashtable();
            table[entry.Key] = value;
        }
        return table;
    }

}
