using UnityEngine;

/// <summary>
/// Last Hider Cam (Tag): once one hider is left, a hunter can hold <see cref="InputAction.Spectate"/> (default V) to
/// watch them from a chase camera behind them. Watching has a price: you stand still and can't use abilities or your
/// tool while you look (<see cref="Player.MovementLocked"/>, <see cref="Player.ViewingCamera"/>), and the hider's map
/// border pulses purple every so often so they know they're being watched (<see cref="PlayerCallouts.NotifyWatched"/>).
/// Added next to the GameModeManager.
/// </summary>
public class LastHiderCam : MonoBehaviour
{
    private const float NotifyEvery = 1.5f;

    /// <summary>True while the local hunter is watching the last hider.</summary>
    public static bool Watching { get; private set; }

    private Transform anchor;
    private Player target;
    private float nextNotify;

    private void Awake()
    {
        anchor = new GameObject("Last Hider Cam").transform;
        anchor.SetParent(transform, false);
    }

    private void OnDisable() => End();

    private void Update()
    {
        var manager = GameModeManager.Instance;
        var local = Player.Local;
        bool available = manager != null && manager.Mode is TagMode && manager.Phase == MatchPhase.Active
                         && local != null && local.Role == PlayerRole.Hunter && !local.IsCaught && !PauseMenuManager.IsPaused
                         && TagRoundSummary.HidersLeft() == 1 && (Watching || !local.ViewingCamera);
        target = available ? LastHider() : null;
        KeyCode key = Keybinds.Get(InputAction.Spectate);
        if (target != null && !Watching) HUDManager.Prompt(key, "Hold to watch the last hider (you'll stand still)");

        bool want = target != null && Input.GetKey(key);
        if (want && !Watching) Begin(local);
        else if (!want && Watching) End();

        if (Watching && Time.time >= nextNotify && target.TryGetComponent(out PlayerCallouts callouts))
        {
            nextNotify = Time.time + NotifyEvery;
            callouts.NotifyWatched();
        }
    }

    // Behind and above the hider, looking at them
    private void LateUpdate()
    {
        if (!Watching || target == null) return;
        Vector3 back = Vector3.ProjectOnPlane(-target.transform.forward, Vector3.up);
        if (back.sqrMagnitude < 0.01f) back = Vector3.back;
        Vector3 focus = target.transform.position + Vector3.up * 1.4f;
        Vector3 want = focus + back.normalized * 4.5f + Vector3.up * 1.8f;
        if (Physics.Linecast(focus, want, out RaycastHit hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            && hit.collider.GetComponentInParent<Player>() == null)
            want = hit.point + (focus - want).normalized * 0.3f; // Don't sit inside a wall
        anchor.position = Vector3.Lerp(anchor.position, want, 1f - Mathf.Exp(-10f * Time.deltaTime));
        anchor.rotation = Quaternion.LookRotation(focus - anchor.position);
    }

    private static Player LastHider()
    {
        foreach (var p in Player.All)
            if (p != null && p.Role == PlayerRole.Hider && !p.IsDownAfterTag) return p;
        return null;
    }

    private void Begin(Player local)
    {
        Watching = true;
        local.MovementLocked = true;
        local.ViewingCamera = true;
        anchor.position = target.transform.position + Vector3.up * 3f;
        if (PlayerCam.Local != null) PlayerCam.Local.SetViewOverride(anchor);
        nextNotify = 0f;
    }

    private void End()
    {
        if (!Watching) return;
        Watching = false;
        var local = Player.Local;
        if (local != null) { local.MovementLocked = false; local.ViewingCamera = false; }
        if (PlayerCam.Local != null) PlayerCam.Local.SetViewOverride(null);
    }
}
