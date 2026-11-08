using Photon.Pun;
using UnityEngine;

/// <summary>
/// Tag's points for living dangerously, on the local player's own machine: while they're a hider in the hunt and a
/// hunter is within <see cref="GameMode.DangerRange"/>, they earn <see cref="GameMode.DangerPointsPerSecond"/> extra
/// points a second (written to their own <see cref="PlayerKeys.DangerPoints"/>, added to the score everywhere).
/// Also notes the round's closest call: the nearest a hunter came without catching them in the next two seconds
/// (<see cref="PlayerKeys.RoundClose"/>, for the round summary).
/// Added next to the GameModeManager.
/// </summary>
public class DangerScore : MonoBehaviour
{
    private const float Tick        = 0.25f;
    private const float Flush       = 1f;    // How often the earned points are written
    private const float CloseCall   = 3f;    // A hunter this near counts as a close call
    private const float EscapeAfter = 2f;    // ...once you're still free this long after

    private float nextTick, nextFlush, pending;
    private float closestThisRound = float.MaxValue, candidate = float.MaxValue, candidateAt;
    private MatchPhase lastPhase;

    private void Update()
    {
        if (Time.time < nextTick) return;
        nextTick = Time.time + Tick;

        var manager = GameModeManager.Instance;
        var mode = manager != null ? manager.Mode : null;
        var local = Player.Local;
        if (mode == null || local == null || !PhotonNetwork.InRoom) return;

        var phase = manager.Phase;
        if (phase != lastPhase && phase == MatchPhase.Countdown) ResetRound();
        lastPhase = phase;

        bool hiding = phase == MatchPhase.Active && local.Role == PlayerRole.Hider && !local.IsCaught;
        if (!hiding) { candidate = float.MaxValue; FlushPoints(); return; }

        float nearest = float.MaxValue;
        foreach (var p in Player.All)
            if (p != null && p != local && p.Role == PlayerRole.Hunter && !p.IsDownAfterTag)
                nearest = Mathf.Min(nearest, Vector3.Distance(p.transform.position, local.transform.position));

        if (mode.DangerPointsPerSecond > 0f && nearest <= mode.DangerRange) pending += Tick * mode.DangerPointsPerSecond;
        if (Time.time >= nextFlush) FlushPoints();

        // Close calls: remember the nearest pass, and count it once you've stayed free a moment after
        if (nearest < CloseCall && nearest < candidate) { candidate = nearest; candidateAt = Time.time; }
        if (candidate < float.MaxValue && Time.time - candidateAt > EscapeAfter)
        {
            if (candidate < closestThisRound)
            {
                closestThisRound = candidate;
                NetProps.SetLocal(PlayerKeys.RoundClose, Mathf.Max(0.1f, candidate));
            }
            candidate = float.MaxValue;
        }
    }

    // Read-modify-write the own property, so a reset by the host (new match) carries on from zero
    private void FlushPoints()
    {
        nextFlush = Time.time + Flush;
        if (pending <= 0f) return;
        float current = NetProps.Of(PhotonNetwork.LocalPlayer, PlayerKeys.DangerPoints, 0f);
        NetProps.SetLocal(PlayerKeys.DangerPoints, current + pending);
        pending = 0f;
    }

    private void ResetRound()
    {
        closestThisRound = candidate = float.MaxValue;
        NetProps.SetLocal(PlayerKeys.RoundClose, 0f);
    }
}
