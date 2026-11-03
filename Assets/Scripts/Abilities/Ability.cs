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
    private float lastUseTime = float.NegativeInfinity;

    /// <summary>Unscaled cooldown in seconds. Slot bases expose this as a tunable field.</summary>
    public virtual float BaseCooldown => 0f;

    /// <summary>Cooldown after cheats, game mode and rule multipliers.</summary>
    public float CooldownDuration => BaseCooldown * (Owner != null ? Owner.CooldownMultiplier : 1f);

    public float CooldownRemaining => Mathf.Max(0f, lastUseTime + CooldownDuration - Time.time);
    public bool  IsReady           => CooldownRemaining <= 0f;

    /// <summary>0 = just used, 1 = ready. Drives the HUD.</summary>
    public virtual float Readiness =>
        CooldownDuration <= 0f ? 1f : 1f - CooldownRemaining / CooldownDuration;

    protected void StartCooldown() => lastUseTime = Time.time;

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

    /// <summary>The Self "Ability Power" cheat (1 = normal).</summary>
    protected float PowerMultiplier => Owner != null ? Owner.Modifiers.Get(Stat.AbilityPower) : 1f;
}
