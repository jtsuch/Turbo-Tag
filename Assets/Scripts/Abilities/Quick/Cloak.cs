using UnityEngine;

/// <summary>
/// Turns the player nearly invisible (to others) while they stay still, for up to
/// <see cref="cloakDuration"/> seconds. Moving at all (keys, jumping, being pushed) or shooting ends it early.
/// Looking around is fine. The look is synced by <see cref="PlayerAppearance"/>.
/// </summary>
[AbilityInfo("Turn nearly invisible for 5 seconds. Moving or shooting breaks it, so pick your spot first.")]
public class Cloak : QuickAbility
{
    // Search & Destroy upgrade path: the value at levels 0, 1 and 2 (AbilityUpgrades reads it; Justin's table, Nov 14)
    private static readonly AbilityUpgrades.Entry Upgrades = AbilityUpgrades.Power("lasts", 0.8f, 1f, 1.2f, v => $"{v * 5f:0.#} s");
    public override AbilityUpgrades.Entry UpgradePath => Upgrades;
    public override bool LoudUse => false;   // Sneaky: not on others' sound visualizers
    // What every screen plays when this is used (each ability owns its own: Ability.PlayUseFx)
    public override void PlayUseFx(Player player, Vector3 feet, Vector3 chest, bool mine)
    {
        Sfx.At(SfxKind.Shimmer, chest, 0.9f);
        Vfx.Burst(chest, new Color(1f, 1f, 1f, 0.6f), 24, 2.5f, 0.3f, 0.8f);
    }

    [Header("Cloak")]
    [TunableParam("Duration", 1f, 30f)]
    [SerializeField] private float cloakDuration = 5f;
    [Tooltip("Speed that counts as moving (m/s), so tiny physics drift doesn't break the cloak.")]
    [SerializeField] private float moveTolerance = 0.6f;
    [Tooltip("Grace after activating, for leftover momentum to settle (s).")]
    [SerializeField] private float settleTime = 0.25f;

    private float startTime = float.NegativeInfinity;
    private bool  cloaked;

    protected override void Activate()
    {
        if (Owner.Appearance == null) return;
        startTime = Time.time;
        cloaked   = true;
        Owner.Appearance.Cloaked = true;
        Owner.Caught += EndCloak;
        if (Owner.Gun != null) Owner.Gun.Fired += EndCloak;
    }

    private void FixedUpdate()
    {
        if (!cloaked || !IsLocal) return;

        float elapsed = Time.time - startTime;
        if (elapsed > cloakDuration * UpgradePower) { EndCloak(); return; }   // S&D: 4 → 5 → 6 s
        if (elapsed < settleTime) return;

        var input = Owner.Input;
        Vector3 v = Body.linearVelocity;
        bool moving = input.MoveInput.sqrMagnitude > 0.01f || input.JumpPressed
                   || new Vector2(v.x, v.z).magnitude > moveTolerance || Mathf.Abs(v.y) > moveTolerance * 3f;
        if (moving) EndCloak();
    }

    // The cloak survives pauses and other abilities; only moving, the timer, being caught or unequipping end it
    public override void Cancel() { }

    protected override void OnDisable()
    {
        base.OnDisable();
        EndCloak();
    }

    private void EndCloak()
    {
        if (!cloaked) return;
        cloaked = false;
        if (Owner == null) return;
        Owner.Caught -= EndCloak;
        if (Owner.Gun != null) Owner.Gun.Fired -= EndCloak;
        if (Owner.Appearance != null) Owner.Appearance.Cloaked = false;
    }
}
