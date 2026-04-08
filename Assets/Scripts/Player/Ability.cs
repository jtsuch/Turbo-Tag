using UnityEngine;

[RequireComponent(typeof(JimmyMove))]
[RequireComponent(typeof(Rigidbody))]
public abstract class Ability : MonoBehaviour
{
    [Header("General Settings")]
    public string abilityName = "New Ability";
    public int numberOfUses = 1;

    [Header("References")]
    protected Rigidbody rb;
    protected JimmyMove pm;
    public enum AbilityType
    {
        Basic,
        Quick,
        Throw,
        Trap
    }

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody>();
        pm = GetComponent<JimmyMove>();
    }

    private readonly string[] basicAbilityList = { "BasicGrapple", "StiffGrapple", "SpringyGrapple", "Flappy" };
    private readonly string[] quickAbilityList = { "Dash" };
    private readonly string[] throwAbilityList = { "BoomBomb" };
    private readonly string[] trapAbilityList = { "Box", "Ladder", "Nuke" };

    public abstract void TryActivate(bool down);
}