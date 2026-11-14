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
    public override bool   PlaysMapIntro => false; // No countdown: straight in

    public override void StartMatch()
    {
        foreach (var player in PhotonNetwork.PlayerList)
            PlayerRoles.Set(player, PlayerRole.None);

        Manager.SetPhase(MatchPhase.Active, 0f);
    }

    public override float CooldownMultiplier(PlayerRole role) => 0f;

    // The Guns switch (Match Rules → Combat): guns and health for practice; the shop on B at any time. Turned on or
    // off mid-session, GameModeManager adds or removes the gun systems.
    public override bool   UsesGuns          => MatchRules.FreeplayGuns;
    public override bool   ShopAnytime       => true;
    public override bool   AllowsHealthDrops => false;
    public override bool   AnnouncesTags     => false;
    public override string CaughtBanner      => "";
    public override string RespawnBanner     => "";

    // Practice: everyone on the map
    public override bool ShowsOnMap(PlayerRole viewer, PlayerRole target) => true;

    // The sandbox: every ability can be tried here, health-mode ones included (damage only lands in modes with health)
    public override bool AllowsEveryAbility => true;

    // No objective text once playing
    public override string GetPhaseLabel(MatchPhase phase) =>
        phase == MatchPhase.Active ? "" : base.GetPhaseLabel(phase);
}
