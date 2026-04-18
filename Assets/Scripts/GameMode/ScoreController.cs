using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Tracks cumulative hide time per player across all rounds. Master-client authoritative —
/// only ticks and fires events on the master client; other clients receive score data via
/// GameModeManager RPCs when needed.
///
/// API:
///   BeginRound(hiderActorNumbers) — register hiders and start accumulation
///   FreezePlayerTimer(actorNumber) — lock a player's time (called when tagged)
///   EndRound()                     — pause all accumulation until the next round
///   GetWinner()                    — actor number with the highest accumulated hide time
///
/// Attach to: the same GameObject as GameModeManager.
/// </summary>
public class ScoreController : MonoBehaviour
{
    public event Action OnScoreLimitReached;

    [Header("Settings (overridden by room settings)")]
    [SerializeField] private float maxTotalTime = 0f;  // Per-player cap. 0 = no limit.

    public float MaxTotalTime { get => maxTotalTime; set => maxTotalTime = value; }

    // actorNumber → accumulated hide time
    private readonly Dictionary<int, float> hideTimes  = new();
    private readonly HashSet<int>           frozen      = new();
    private readonly HashSet<int>           activeHiders = new();
    private bool isRunning = false;

    // -------------------------------------------------------------------------
    // Public control API
    // -------------------------------------------------------------------------

    /// <summary>Register hiders and start accumulation for this round.</summary>
    public void BeginRound(IEnumerable<int> hiderActorNumbers)
    {
        frozen.Clear();
        activeHiders.Clear();
        foreach (int actor in hiderActorNumbers)
        {
            activeHiders.Add(actor);
            if (!hideTimes.ContainsKey(actor))
                hideTimes[actor] = 0f;
        }
        isRunning = true;
    }

    /// <summary>Stop all accumulation until the next BeginRound.</summary>
    public void EndRound()
    {
        isRunning = false;
    }

    /// <summary>Stop accumulating time for a player (called when they are tagged).</summary>
    public void FreezePlayerTimer(int actorNumber)
    {
        frozen.Add(actorNumber);
    }

    /// <summary>Returns accumulated hide time for the given actor, or 0 if unknown.</summary>
    public float GetHideTime(int actorNumber) =>
        hideTimes.TryGetValue(actorNumber, out float t) ? t : 0f;

    public IReadOnlyDictionary<int, float> GetAllHideTimes() => hideTimes;

    /// <summary>Returns the actor number of the player with the most hide time, or -1 if no data.</summary>
    public int GetWinner()
    {
        int   winner = -1;
        float best   = -1f;
        foreach (var kvp in hideTimes)
        {
            if (kvp.Value > best) { best = kvp.Value; winner = kvp.Key; }
        }
        return winner;
    }

    // -------------------------------------------------------------------------
    // Unity Lifecycle
    // -------------------------------------------------------------------------

    private void Update()
    {
        if (!isRunning) return;
        if (!Photon.Pun.PhotonNetwork.IsMasterClient) return;

        // Iterate over a snapshot to allow modifications inside the loop
        var keys = new List<int>(activeHiders);
        foreach (int actor in keys)
        {
            if (frozen.Contains(actor)) continue;

            hideTimes[actor] += Time.deltaTime;

            if (maxTotalTime > 0f && hideTimes[actor] >= maxTotalTime)
            {
                hideTimes[actor] = maxTotalTime;
                frozen.Add(actor);
                OnScoreLimitReached?.Invoke();
            }
        }
    }
}
