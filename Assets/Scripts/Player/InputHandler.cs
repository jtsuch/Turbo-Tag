using Photon.Pun;
using UnityEngine;

/// <summary>
/// Polls keyboard/mouse input for the local player once per frame using the bindings in
/// <see cref="Keybinds"/>. Exposes movement intent for PlayerMovement and routes ability keys
/// to the AbilityHandler. Input is suppressed while paused, frozen by the game mode, or caught.
/// Attach to: ThePlayer prefab root — inert on remote player instances.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class InputHandler : MonoBehaviourPun
{
    // ─── Movement intent (read by PlayerMovement, camera, animation) ──────────
    public Vector2 MoveInput     { get; private set; }
    public bool    JumpPressed   { get; private set; }
    public bool    JumpHeld      { get; private set; }
    public bool    SprintHeld    { get; private set; }
    public bool    CrouchPressed { get; private set; }
    /// <summary>Crouch key held — used by grapples to reel out.</summary>
    public bool    CrouchHeld    { get; private set; }
    public bool    PronePressed  { get; private set; }
    public bool    GrabPressed   { get; private set; }

    /// <summary>Optional rewrite of the movement keys (Scrambled Keys effect); null = normal.</summary>
    public System.Func<Vector2, Vector2> MoveRemap { get; set; }

    private Player         player;
    private AbilityHandler abilities;
    private bool           abilitiesBlocked;

    private static readonly AbilitySlot[] Slots =
        { AbilitySlot.Basic, AbilitySlot.Quick, AbilitySlot.Throw, AbilitySlot.Trap };

    private void Awake()
    {
        player    = GetComponent<Player>();
        abilities = GetComponent<AbilityHandler>();
    }

    private void Update()
    {
        if (!photonView.IsMine) return;

        ClearFrameInput();

        // Escape while typing in the admin console closes the console, not opens the pause menu
        bool consoleHasEscape = AdminConsole.IsOpen || AdminConsole.ClosedFrame == Time.frameCount;
        if (!consoleHasEscape && Input.GetKeyDown(Keybinds.Get(InputAction.Pause)) && PauseMenuManager.Instance != null)
            PauseMenuManager.Instance.Toggle();

        bool paused = PauseMenuManager.IsPaused;

        if (!paused && player.CanMove)
            ReadMovement();

        UpdateAbilities(!paused && player.CanUseAbilities);
    }

    private void ClearFrameInput()
    {
        MoveInput     = Vector2.zero;
        JumpPressed   = JumpHeld = SprintHeld = CrouchPressed = CrouchHeld = PronePressed = GrabPressed = false;
    }

    private void ReadMovement()
    {
        // Raw keys: the input is exactly what's held this frame. (GetAxis smoothed keys over ~0.3 s, so the
        // player kept walking after letting go and quick taps barely moved them.) PlayerMovement accelerates.
        Vector2 keys = SimInput.MoveAxes; // The real axes, or the clip recorder's
        if (MoveRemap != null) keys = MoveRemap(keys);
        MoveInput = Vector2.ClampMagnitude(keys, 1f) * player.Modifiers.Get(Stat.ControlDirection);

        JumpPressed   = Pressed(InputAction.Jump);
        JumpHeld      = Held(InputAction.Jump);
        SprintHeld    = Held(InputAction.Sprint);
        CrouchPressed = Pressed(InputAction.Crouch);
        CrouchHeld    = Held(InputAction.Crouch);
        PronePressed  = Pressed(InputAction.Prone);
        GrabPressed   = Pressed(InputAction.Grab);
    }

    // ─── Abilities ────────────────────────────────────────────────────────────

    private void UpdateAbilities(bool allowed)
    {
        if (!allowed)
        {
            // Release anything held when input is taken away (pause menu, frozen, caught)
            if (!abilitiesBlocked) abilities.CancelAll();
            abilitiesBlocked = true;
            return;
        }
        abilitiesBlocked = false;

        KeyCode action = Keybinds.Get(InputAction.Action);
        if (SimInput.KeyDown(action)) abilities.ConfirmDown();
        if (SimInput.KeyUp(action))   abilities.ConfirmUp();

        foreach (var slot in Slots)
        {
            var ability = abilities.GetEquipped(slot);
            if (ability != null) Route(ability, Keybinds.ForSlot(slot));
        }

        foreach (var binding in abilities.CustomBindings)
        {
            var ability = abilities.Find(binding.Key);
            if (ability != null) Route(ability, binding.Value);
        }
    }

    private void Route(Ability ability, KeyCode key)
    {
        if (key == KeyCode.None) return;

        if      (SimInput.KeyDown(key)) abilities.Use(ability, AbilityInputEvent.Down);
        else if (SimInput.KeyUp(key))   abilities.Use(ability, AbilityInputEvent.Up);
        else if (SimInput.Key(key))     abilities.Use(ability, AbilityInputEvent.Held);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static bool Pressed(InputAction action) => SimInput.KeyDown(Keybinds.Get(action));
    private static bool Held(InputAction action)    => SimInput.Key(Keybinds.Get(action));
}
