using UnityEngine;

/// <summary>
/// Records the local player's match into their profile (<see cref="ProfileStats.RecordMatch"/>) once per level, a moment
/// after MatchEnd begins, so the last kill or tag of the match has reached everyone's stats first. Added next to the
/// GameModeManager.
/// </summary>
public class ProfileRecorder : MonoBehaviour
{
    private const float SettleSeconds = 1.5f;

    private GameModeManager manager;
    private float levelStart;
    private float matchEndSeen = -1f;
    private bool recorded;

    private void Start()
    {
        manager = GetComponent<GameModeManager>();
        levelStart = Time.time;
    }

    private void Update()
    {
        if (recorded || manager == null || manager.Mode == null || Player.Local == null) return;
        if (manager.Phase != MatchPhase.MatchEnd) { matchEndSeen = -1f; return; }
        if (matchEndSeen < 0f) matchEndSeen = Time.time;
        if (Time.time - matchEndSeen < SettleSeconds) return;

        recorded = true;
        ProfileStats.RecordMatch(manager.Mode, MatchRules.GameModeKey, Time.time - levelStart);
    }
}
