using UnityEngine;

[RequireComponent(typeof(JimmyMove))]
[RequireComponent(typeof(Rigidbody))]
public abstract class Ability : MonoBehaviour
{
    [Header("General Settings")]
    public string abilityName = "New Ability";
    public int numberOfUses = 1;
    public enum AbilityType
    {
        Basic,
        Quick,
        Throw,
        Trap
    }
    public virtual bool IsAwaitingAction => false;

    [Header("References")]
    protected Rigidbody rb;
    protected JimmyMove pm;

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody>();
        pm = GetComponent<JimmyMove>();
    }

    //public abstract void TryActivate(bool down);

    public abstract void TryActivate(AbilityInputEvent inputEvent);
    public virtual void OnActionConfirm() { }   // LMB pressed
    public virtual void OnActionCancel() { }    // Ability key pressed while active = cancel
    public virtual void OnActionConfirmUp() { } // Shows when LMB is released
}