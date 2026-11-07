using Photon.Pun;

/// <summary>
/// Tutorial: for teaching a group. Like Freeplay (no roles, no timer, no objective, out-of-bounds respawn, everyone
/// starts at the level's main spawn), but it goes through Pregame so everyone picks abilities, and everyone arrives
/// with abilities switched off (<see cref="AbilitiesStartOff"/>: the admin console's "ability 0") and cheats off.
/// The host turns abilities on with the console (<c>all ability 1</c>), then cheats from the pause menu's Match Rules.
/// Cooldowns are the real ones, so players learn the actual timings.
/// </summary>
public class TutorialMode : GameMode
{
    public override string DisplayName => "Tutorial";
    public override bool   ShowsTimer  => false;

    /// <summary>Everyone spawns with abilities off until an admin turns them on.</summary>
    public override bool AbilitiesStartOff => true;

    public override void StartMatch()
    {
        foreach (var player in PhotonNetwork.PlayerList)
            PlayerRoles.Set(player, PlayerRole.None);

        Manager.SetPhase(MatchPhase.Active, 0f);
    }

    // Everyone on the map, so the group can find each other
    public override bool ShowsOnMap(PlayerRole viewer, PlayerRole target) => true;

    // No objective text once playing
    public override string GetPhaseLabel(MatchPhase phase) =>
        phase == MatchPhase.Active ? "" : base.GetPhaseLabel(phase);
}
