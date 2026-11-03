using Photon.Pun;
using TMPro;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Networked match clock. The master client stamps each phase with a start time (PhotonNetwork.Time)
/// and duration in room properties; every client derives the same elapsed/remaining time from
/// those values, so no per-frame sync is needed and late joiners see the correct time.
/// Supports pausing via the "Timer Paused" rule. Also renders the timer and phase HUD text.
/// Attach to: the same GameObject as GameModeManager.
/// </summary>
public class MatchTimerController : MonoBehaviour
{
    [Header("HUD")]
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text phaseText;

    private bool lastPauseRequest;

    // ─── Clock state (derived from room properties) ───────────────────────────

    public double PhaseStart => NetProps.Room(RoomKeys.PhaseStart, PhotonNetwork.Time);
    public float  Duration   => NetProps.Room(RoomKeys.PhaseDuration, 0f);
    public bool   IsPaused   => NetProps.Room(RoomKeys.PausedAt, -1d) >= 0d;
    public bool   HasLimit   => Duration > 0f;

    public float Elapsed
    {
        get
        {
            double now = IsPaused ? NetProps.Room(RoomKeys.PausedAt, PhotonNetwork.Time) : PhotonNetwork.Time;
            return Mathf.Max(0f, (float)(now - PhaseStart));
        }
    }

    public float Remaining  => HasLimit ? Mathf.Max(0f, Duration - Elapsed) : 0f;
    public bool  IsExpired  => HasLimit && Elapsed >= Duration;

    /// <summary>Counts down when the phase has a limit, otherwise counts up.</summary>
    public float DisplayTime => HasLimit ? Remaining : Elapsed;

    // ─── Master-client API ────────────────────────────────────────────────────

    /// <summary>Room properties that restart the clock with a new duration (0 = count up).</summary>
    public Hashtable BuildRestart(float duration)
    {
        double now = PhotonNetwork.Time;
        return new Hashtable
        {
            [RoomKeys.PhaseStart]    = now,
            [RoomKeys.PhaseDuration] = duration,
            [RoomKeys.PausedAt]      = MatchRules.TimerPaused ? now : -1d,
        };
    }

    private void SetPaused(bool paused)
    {
        if (paused == IsPaused) return;

        double now = PhotonNetwork.Time;
        if (paused)
        {
            NetProps.SetRoom(RoomKeys.PausedAt, now);
        }
        else
        {
            // Shift the start forward by the paused span so no time is lost
            double pausedAt = NetProps.Room(RoomKeys.PausedAt, now);
            NetProps.SetRoom(new Hashtable
            {
                [RoomKeys.PhaseStart] = PhaseStart + (now - pausedAt),
                [RoomKeys.PausedAt]   = -1d,
            });
        }
    }

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Update()
    {
        if (PhotonNetwork.CurrentRoom == null) return;

        // Master mirrors the host's pause rule into the clock (only when the rule changes)
        if (PhotonNetwork.IsMasterClient)
        {
            bool wantPaused = MatchRules.TimerPaused;
            if (wantPaused != lastPauseRequest)
            {
                lastPauseRequest = wantPaused;
                SetPaused(wantPaused);
            }
        }

        UpdateHud();
    }

    private void UpdateHud()
    {
        var manager = GameModeManager.Instance;
        MatchPhase phase = manager != null ? manager.Phase : MatchPhase.Waiting;
        bool showClock = phase != MatchPhase.Waiting
                         && (manager == null || manager.Mode == null || manager.Mode.ShowsTimer);

        if (timerText != null)
        {
            timerText.gameObject.SetActive(showClock);
            timerText.text = FormatTime(DisplayTime, roundUp: HasLimit);
        }

        if (phaseText != null)
        {
            string label = manager != null ? manager.PhaseLabel : "";
            phaseText.text = IsPaused && showClock ? $"{label} (Paused)" : label;
        }
    }

    /// <summary>Formats seconds as mm:ss. Countdowns round up so "00:00" only shows at expiry.</summary>
    public static string FormatTime(float seconds, bool roundUp = false)
    {
        int total = roundUp ? Mathf.CeilToInt(seconds) : Mathf.FloorToInt(seconds);
        total = Mathf.Max(0, total);
        return $"{total / 60:D2}:{total % 60:D2}";
    }
}
