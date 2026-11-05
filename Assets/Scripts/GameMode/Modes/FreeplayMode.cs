using Photon.Pun;

/// <summary>
/// Freeplay: no roles, no timer, no objectives and no cooldowns. Everyone spawns straight into
/// the Active phase and respawns after falling out of the world — the same experience as
/// opening a level directly in the editor. For practice and for testing abilities.
/// </summary>
public class FreeplayMode : GameMode
{
    public override string DisplayName => "Freeplay";
    public override bool   ShowsTimer  => false;

    public override void StartMatch()
    {
        foreach (var player in PhotonNetwork.PlayerList)
            PlayerRoles.Set(player, PlayerRole.None);

        Manager.SetPhase(MatchPhase.Active, 0f);
    }

    public override float CooldownMultiplier(PlayerRole role) => 0f;

    // Practice: everyone on the map
    public override bool ShowsOnMap(PlayerRole viewer, PlayerRole target) => true;

    // No objective text once playing
    public override string GetPhaseLabel(MatchPhase phase) =>
        phase == MatchPhase.Active ? "" : base.GetPhaseLabel(phase);
}
