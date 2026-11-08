using System.Collections;
using UnityEngine;

/// <summary>
/// Big moments of a match, on every screen (local only, from the synced phase):
///   - Countdown: a sweeping fly-over of the level for its first few seconds (when the countdown is long enough),
///     then the view drops back into your penguin;
///   - MatchEnd: the final moment plays in slow motion for a second and a half.
/// Added next to GameModeManager.
/// </summary>
public class MatchCinematics : MonoBehaviour
{
    private const float FlyoverSeconds = 3.2f;
    private const float MinCountdown   = 4.5f; // Shorter countdowns skip the fly-over

    private Transform  flyCam;
    private Coroutine  flyover, slowMo;
    private GameModeManager manager;

    private void Start()
    {
        manager = GetComponent<GameModeManager>();
        if (manager != null) manager.PhaseChanged += HandlePhaseChanged;
    }

    private void OnDestroy()
    {
        if (manager != null) manager.PhaseChanged -= HandlePhaseChanged;
        Time.timeScale = 1f;
        EndFlyover();
        if (flyCam != null) Destroy(flyCam.gameObject);
    }

    private void HandlePhaseChanged(MatchPhase phase)
    {
        bool skip = manager.Mode is SearchDestroyMode; // Players move and plan during its pre-round: no fly-over
        if (phase == MatchPhase.Countdown && !skip && manager.Clock != null && manager.Clock.HasLimit && manager.Clock.Remaining >= MinCountdown)
        {
            if (flyover != null) StopCoroutine(flyover);
            flyover = StartCoroutine(Flyover(Mathf.Min(FlyoverSeconds, manager.Clock.Remaining - 1.5f)));
        }
        if (phase == MatchPhase.MatchEnd)
        {
            if (slowMo != null) StopCoroutine(slowMo);
            slowMo = StartCoroutine(SlowMotion());
        }
    }

    // ─── Fly-over ─────────────────────────────────────────────────────────────

    private IEnumerator Flyover(float seconds)
    {
        // Wait for the respawn at the start of the countdown to settle
        yield return null;
        var local = Player.Local;
        var cam   = PlayerCam.Local;
        var b     = LevelInfo.Bounds;
        if (local == null || cam == null || !b.HasValue || Killcam.IsPlaying) yield break;

        if (flyCam == null) flyCam = new GameObject("Flyover Camera").transform;
        Vector3 centre = b.Value.center;
        float radius = Mathf.Clamp(Mathf.Max(b.Value.extents.x, b.Value.extents.z) * 0.85f, 20f, 120f);
        float height = b.Value.max.y + 8f;
        float startAngle = Random.Range(0f, 360f);

        cam.SetViewOverride(flyCam);
        SetOwnBodyVisible(local, true);
        flying = true;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            if (Player.Local == null || PlayerCam.Local == null) yield break;
            float k = t / seconds;
            float ease = k * k * (3f - 2f * k);
            // A quarter orbit while sinking from high above toward the player's spawn
            float angle = (startAngle + ease * 100f) * Mathf.Deg2Rad;
            Vector3 orbit = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius + Vector3.up * (height - centre.y);
            Vector3 home  = local.transform.position + Vector3.up * local.EyeHeight - local.transform.forward * 3f + Vector3.up * 1.5f;
            Vector3 pos   = Vector3.Lerp(orbit, home, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.65f, 1f, k)));
            Vector3 look  = Vector3.Lerp(centre, local.transform.position + Vector3.up * local.EyeHeight, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1f, k)));
            flyCam.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
            yield return null;
        }
        EndFlyover();
    }

    private bool flying;

    private void EndFlyover()
    {
        if (!flying) return; // Never touch a view someone else set (a Spy Camera, the killcam)
        flying = false;
        var cam = PlayerCam.Local;
        if (cam != null && cam.HasViewOverride && !Killcam.IsPlaying) cam.SetViewOverride(null);
        if (Player.Local != null) SetOwnBodyVisible(Player.Local, false);
        flyover = null;
    }

    private static void SetOwnBodyVisible(Player local, bool visible)
    {
        if (local.TryGetComponent(out PlayerInitializer init)) init.SetOwnBodyVisible(visible);
    }

    // ─── Final moment ─────────────────────────────────────────────────────────

    private IEnumerator SlowMotion()
    {
        Time.timeScale = 0.3f;
        yield return new WaitForSecondsRealtime(1.5f);
        for (float t = 0f; t < 0.5f; t += Time.unscaledDeltaTime)
        {
            Time.timeScale = Mathf.Lerp(0.3f, 1f, t / 0.5f);
            yield return null;
        }
        Time.timeScale = 1f;
        slowMo = null;
    }
}
