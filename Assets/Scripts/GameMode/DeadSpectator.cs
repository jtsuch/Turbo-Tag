using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Spectating while dead, for modes that keep dead players out (<see cref="GameMode.SpectatesWhenDead"/>: Last Man
/// Standing). After the killcam and a moment on your own ragdoll, the view follows a living player from a chase
/// camera behind and above them, looking where they look; Action or Jump moves to the next one. A line at the bottom
/// says who you're watching. Local only; added next to the <see cref="GameModeManager"/>. (Search &amp; Destroy has its
/// own, teammates only, in <see cref="SearchDestroyController"/>.)
/// </summary>
public class DeadSpectator : MonoBehaviour
{
    private Transform anchor;
    private TMP_Text line;
    private Player watching;
    private int index;
    private bool on;
    private float deadSince = -1f;

    private void Start()
    {
        anchor = new GameObject("Spectate Cam").transform;
        anchor.SetParent(transform, false);
        var canvas = UIKit.Canvas("Spectate", 3);
        line = UIKit.OneLine(UIKit.Text(canvas.transform, "", 24f, UIKit.Font.Bold, UITheme.Current.text, TextAlignmentOptions.Center, "Spectate"));
        UIKit.Place(line.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(900f, 34f));
    }

    private void Update()
    {
        var manager = GameModeManager.Instance;
        var local = Player.Local;
        bool dead = manager != null && manager.Mode != null && manager.Mode.SpectatesWhenDead && local != null && local.IsCaught
                    && manager.Mode.KeepsDead(manager.Phase, local.ActorNumber);
        if (!dead) { deadSince = -1f; Stop(); line.text = ""; return; }
        if (deadSince < 0f) deadSince = Time.time;
        if (Killcam.IsPlaying || Time.time - deadSince < 2.5f) { Stop(); line.text = ""; return; }

        var alive = new List<Player>();
        foreach (var p in Player.All)
            if (p != null && p != local && !p.IsCaught && !p.IsDownAfterTag && p.Role != PlayerRole.Ghost) alive.Add(p);
        alive.Sort((a, b) => a.ActorNumber.CompareTo(b.ActorNumber));
        if (alive.Count == 0) { Stop(); line.text = "<color=#B9B2E0>Waiting for the next round…</color>"; return; }

        if (!PauseMenuManager.IsPaused && (Input.GetKeyDown(Keybinds.Get(InputAction.Action)) || Input.GetKeyDown(Keybinds.Get(InputAction.Jump))))
            index++;
        watching = alive[index % alive.Count];
        if (!on)
        {
            on = true;
            anchor.position = watching.transform.position + Vector3.up * 3f;
            if (PlayerCam.Local != null) PlayerCam.Local.SetViewOverride(anchor);
        }
        line.text = $"<color=#B9B2E0>SPECTATING</color>  {watching.DisplayName}   <size=75%><color=#B9B2E0>{Keybinds.Label(Keybinds.Get(InputAction.Action))}: next player</color></size>";
    }

    // Behind and above them, looking where they look (pulled in front of walls)
    private void LateUpdate()
    {
        if (!on || watching == null) return;
        Vector3 back = Vector3.ProjectOnPlane(-watching.transform.forward, Vector3.up);
        if (back.sqrMagnitude < 0.01f) back = Vector3.back;
        Vector3 focus = watching.transform.position + Vector3.up * 1.6f * Mathf.Max(0.5f, watching.Size);
        Vector3 want = focus + back.normalized * 4f + Vector3.up * 1.4f;
        if (Physics.Linecast(focus, want, out RaycastHit hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            && hit.collider.GetComponentInParent<Player>() == null)
            want = hit.point + (focus - want).normalized * 0.3f;
        anchor.position = Vector3.Lerp(anchor.position, want, 1f - Mathf.Exp(-10f * Time.deltaTime));
        anchor.rotation = Quaternion.LookRotation(focus + watching.transform.forward * 6f - anchor.position);
    }

    private void Stop()
    {
        if (!on) return;
        on = false;
        watching = null;
        if (PlayerCam.Local != null && !Killcam.IsPlaying) PlayerCam.Local.SetViewOverride(null);
    }

    private void OnDestroy()
    {
        Stop();
        if (line != null) Destroy(line.canvas.gameObject);
    }
}
