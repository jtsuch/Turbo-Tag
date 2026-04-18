using UnityEngine;

/// <summary>
/// Abstract base for all player abilities. Subclass via BasicAbility, QuickAbility,
/// ThrowAbility, or TrapAbility — never attach this class directly.
/// AbilityHandler discovers abilities on the same GameObject by their abilityName at runtime.
/// Attach to: ThePlayer prefab — alongside AbilityHandler; one component per ability.
/// </summary>
[RequireComponent(typeof(JimmyMove))]
[RequireComponent(typeof(Rigidbody))]
public abstract class Ability : MonoBehaviour
{
    // ─── Identity ─────────────────────────────────────────────────────────────
    [Header("General Settings")]
    public string abilityName = "New Ability";
    public int numberOfUses = 1;
    public enum AbilityType { Basic, Quick, Throw, Trap }
    [HideInInspector] public AbilityType abilityType = AbilityType.Basic;

    // True while the ability is mid-flow and expects a confirm or cancel input
    public virtual bool IsAwaitingAction => false;

    // ─── References ───────────────────────────────────────────────────────────
    [Header("References")]
    protected Rigidbody rb;
    protected JimmyMove pm;

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody>();
        pm = GetComponent<JimmyMove>();
    }

    // ─── Input Interface ──────────────────────────────────────────────────────
    public abstract void TryActivate(AbilityInputEvent inputEvent);
    public virtual void OnActionConfirm() { }    // Called when Action key (LMB) is pressed
    public virtual void OnActionCancel() { }     // Called when a conflicting ability interrupts this one
    public virtual void OnActionConfirmUp() { }  // Called when Action key is released
}