using System;
using System.Collections.Generic;
using Photon.Pun;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>One scoreboard column: a header and how to show a player's value.</summary>
public readonly struct ScoreboardColumn
{
    public readonly string                     Header;
    public readonly Func<PhotonPlayer, string> Value;

    public ScoreboardColumn(string header, Func<PhotonPlayer, string> value)
    {
        Header = header;
        Value  = value;
    }
}

/// <summary>
/// Rules and flow for one game mode. Plain C# (no MonoBehaviour): GameModeManager creates the
/// instance from <see cref="GameModeRegistry"/> and forwards scene and network events to it.
///
/// Authority model:
///   - Methods marked "master" run only on the master client and drive the match by writing
///     room/player properties through GameModeManager.
///   - Everything else runs on every client and must be a pure function of synced state
///     (phase, roles, scores) so all clients agree without extra messages.
/// Because all match state lives in room properties, a new master client picks up seamlessly.
/// </summary>
public abstract class GameMode
{
    protected GameModeManager       Manager { get; private set; }
    protected MatchTimerController  Clock   => Manager.Clock;
    protected ScoreController       Scores  => Manager.Scores;
    protected MatchPhase            Phase   => Manager.Phase;

    internal void Attach(GameModeManager manager) => Manager = manager;

    // ─── Description ──────────────────────────────────────────────────────────

    public abstract string DisplayName { get; }

    /// <summary>True if the mode assigns hider/hunter roles (drives HUD role text and highlights).</summary>
    public virtual bool UsesRoles => false;

    /// <summary>True if the HUD should show a running score.</summary>
    public virtual bool ShowsScores => false;

    /// <summary>True if the match is several rounds (the HUD shows "ROUND n" under the clock).</summary>
    public virtual bool HasRounds => false;

    /// <summary>True if the HUD should show the match clock.</summary>
    public virtual bool ShowsTimer => true;

    /// <summary>Show the map intro (a pan round the level with its name) before the match starts (`MapIntro`).</summary>
    public virtual bool PlaysMapIntro => true;

    /// <summary>The local player spawns with abilities off (the admin console's "ability 0"; "ability 1" turns them on). Tutorial.</summary>
    public virtual bool AbilitiesStartOff => false;

    /// <summary>
    /// True if a tagged player comes back after the respawn delay (Tag: as a hunter). False keeps them caught,
    /// watching their ragdoll from the caught camera, until the scene changes (the tag ended the match).
    /// </summary>
    public virtual bool RespawnsAfterTag => true;

    /// <summary>
    /// While true, a dead player stays down even once their respawn delay is over (Search &amp; Destroy: one life per
    /// round, back at the next round's countdown). Checked on every client, so must follow synced state.
    /// </summary>
    public virtual bool KeepsDead(MatchPhase phase) => false;

    /// <summary>
    /// The same for one player (<paramref name="actor"/>): lets a mode sit some players out whatever the phase (Last
    /// Man Standing: Sudden Death bystanders and late joiners). Default: <see cref="KeepsDead(MatchPhase)"/>.
    /// </summary>
    public virtual bool KeepsDead(MatchPhase phase, int actor) => KeepsDead(phase);

    /// <summary>Dead players watch a living player from a chase camera (<see cref="DeadSpectator"/>; S&amp;D has its own).</summary>
    public virtual bool SpectatesWhenDead => false;

    /// <summary>
    /// Team spawns at the start of a round: the spawn points this team uses, or null for the default (the level's
    /// points split in half along its longer side). Search &amp; Destroy: the attack and defence ends.
    /// </summary>
    public virtual System.Collections.Generic.List<SpawnPoint> TeamSpawns(System.Collections.Generic.List<SpawnPoint> points, PlayerRole role) => null;

    /// <summary>
    /// Seconds a tagged/killed player waits before respawning. <paramref name="tagger"/> is who got them
    /// (the victim themself for a fall). The wait can be cut short (<see cref="Player.EndRespawnWait"/>, killcam skip).
    /// </summary>
    public virtual float RespawnDelayAfter(PhotonPlayer tagger, PhotonPlayer victim) => MatchRules.RespawnDelay;

    /// <summary>Every ability is offered, health-mode ones included (Freeplay: the sandbox for trying them out).</summary>
    public virtual bool AllowsEveryAbility => false;

    /// <summary>The player strictly in the lead wears a crown (Free for All).</summary>
    public virtual bool CrownsLeader => false;

    /// <summary>True for gun modes: health, guns, the gun picker, kill feed, health drops and killcam (CombatManager).</summary>
    public virtual bool UsesGuns => false;

    /// <summary>
    /// Gun modes: the gun a role gets on every spawn instead of the picker. Null = the picker (every gun),
    /// "" = no gun at all (Hunter: only the hunter is armed, with a pistol).
    /// </summary>
    public virtual string FixedGun(PlayerRole role) => null;

    /// <summary>Gun modes: whether <paramref name="attacker"/>'s bullets hurt <paramref name="target"/> (no friendly fire; bullets pass through).</summary>
    public virtual bool CanDamage(PlayerRole attacker, PlayerRole target) => true;

    /// <summary>Gun modes: whether guns fire in <paramref name="phase"/> (default: only while the match is live; Search &amp; Destroy also in its pre-round).</summary>
    public virtual bool CanShoot(MatchPhase phase) => phase == MatchPhase.Active;

    /// <summary>Gun modes: shot players drop a health pack (with the Health Drops rule).</summary>
    public virtual bool AllowsHealthDrops => true;

    /// <summary>
    /// Same side: teammates are never outlined as enemies, read each other's names, show blue in X-ray.
    /// Default: both hunters or both not, in modes with roles; nobody in modes without.
    /// </summary>
    public virtual bool SameTeam(PlayerRole a, PlayerRole b) =>
        UsesRoles && a != PlayerRole.None && b != PlayerRole.None && (a == PlayerRole.Hunter) == (b == PlayerRole.Hunter);

    /// <summary>Outline a <paramref name="viewer"/> sees on <paramref name="target"/>: 0 none, 1 red (enemy/hunter), 2 blue (ally).</summary>
    public virtual int OutlineFor(PlayerRole viewer, PlayerRole target) =>
        UsesRoles && target == PlayerRole.Hunter ? 1 : 0;

    /// <summary>Team modes: the HUD's score card text (both teams' scores); null shows the player's own score.</summary>
    public virtual string TeamScoreText => null;

    /// <summary>True to spawn on the level's SpawnPoints (random, away from others) instead of the Spawner's ring.</summary>
    public virtual bool UsesSpawnPoints => false;

    /// <summary>True to show "X tagged Y" banners to everyone else.</summary>
    public virtual bool AnnouncesTags => true;

    /// <summary>Banner for the local player when tagged ("" for none).</summary>
    public virtual string CaughtBanner => "Caught!";

    /// <summary>Banner for the local player after respawning ("" for none).</summary>
    public virtual string RespawnBanner => "You're a hunter now!";

    /// <summary>How the HUD writes a score (Tag: survival time).</summary>
    public virtual string FormatScore(float score) => MatchTimerController.FormatTime(score);

    // ─── Master-client hooks ──────────────────────────────────────────────────

    /// <summary>Master: called once every player has spawned into the level.</summary>
    public abstract void StartMatch();

    /// <summary>Master: called every frame while the match is running.</summary>
    public virtual void MasterUpdate() { }

    /// <summary>Every client, every frame once the mode is running (Last Man Standing: sitting out a round).</summary>
    public virtual void ClientUpdate() { }

    /// <summary>Master: a client reported that <paramref name="tagger"/> touched <paramref name="target"/>.</summary>
    public virtual void HandleTagRequest(PhotonPlayer tagger, PhotonPlayer target) { }

    /// <summary>
    /// Master, modes without guns: <paramref name="player"/> fell off the map (they've already been put back on a
    /// spawn). Default: nothing more. Tag catches a falling hider (they become a hunter).
    /// </summary>
    public virtual void HandleFall(PhotonPlayer player) { }

    /// <summary>
    /// Master, gun modes: <paramref name="victim"/> died in the Active phase. <paramref name="killer"/> is null
    /// (or the victim) for a fall. Scoring only: CombatManager handles the respawn, kill feed and drops.
    /// </summary>
    public virtual void OnPlayerKilled(PhotonPlayer victim, PhotonPlayer killer, string weapon, bool headshot) { }

    /// <summary>Master: a player disconnected mid-match.</summary>
    public virtual void OnPlayerLeft(PhotonPlayer player) { }

    // ─── All-client hooks ─────────────────────────────────────────────────────

    public virtual void OnPhaseChanged(MatchPhase phase) { }

    /// <summary>The local player physically touched another player.</summary>
    public virtual void OnLocalPlayerContact(Player local, Player other) { }

    // ─── Queries (must be deterministic from synced state) ────────────────────

    public virtual bool CanMove(PlayerRole role, MatchPhase phase) => true;

    public virtual bool CanUseAbilities(PlayerRole role, MatchPhase phase) => CanMove(role, phase);

    /// <summary>Mode-specific multiplier on movement speed for a role (1 = normal).</summary>
    public virtual float SpeedMultiplier(PlayerRole role) => 1f;

    /// <summary>Mode-specific multiplier on ability cooldowns (0 = no cooldowns).</summary>
    public virtual float CooldownMultiplier(PlayerRole role) => 1f;

    /// <summary>True while a player in this role is earning score.</summary>
    public virtual bool IsScoring(PlayerRole role, MatchPhase phase) => false;

    public virtual string GetPhaseLabel(MatchPhase phase) => phase switch
    {
        MatchPhase.Waiting   => "Waiting for players",
        MatchPhase.Countdown => "Get Ready",
        MatchPhase.Hiding    => "Hide!",
        MatchPhase.Active    => "Go!",
        MatchPhase.RoundEnd  => "Round Over",
        MatchPhase.MatchEnd  => "Match Over",
        _                    => "",
    };

    /// <summary>Short instruction shown on the HUD for the local player.</summary>
    public virtual string GetObjective(PlayerRole role, MatchPhase phase) => "";

    /// <summary>
    /// True when a <paramref name="viewer"/> shouldn't be able to tell who <paramref name="target"/> is: the
    /// target shows as a black silhouette with no name tag or accessories (e.g. hunters during setup).
    /// </summary>
    public virtual bool HidesIdentity(PlayerRole viewer, PlayerRole target, MatchPhase phase) => false;

    /// <summary>
    /// Whether a <paramref name="viewer"/> can read <paramref name="target"/>'s name tag. Default: teammates
    /// always (hunters with hunters, everyone else together; modes without roles: everyone), plus the
    /// non-hunter side can read hunters' names; hunters never see the other side's names.
    /// </summary>
    public virtual bool ShowsName(PlayerRole viewer, PlayerRole target)
    {
        bool viewerHunts = viewer == PlayerRole.Hunter;
        bool targetHunts = target == PlayerRole.Hunter;
        return viewerHunts == targetHunts || !viewerHunts;
    }

    /// <summary>
    /// Whether the minimap shows <paramref name="target"/> to <paramref name="viewer"/> (it never shows bodies, only
    /// these markers). Default: teammates (same role; nobody when roles aren't used).
    /// </summary>
    public virtual bool ShowsOnMap(PlayerRole viewer, PlayerRole target) => viewer == target && viewer != PlayerRole.None;

    /// <summary>
    /// Who <paramref name="viewer"/> sees through walls (PlayerAppearance's X-ray pass): 0 nobody, 1 an ally (blue;
    /// the President and the disc carrier show yellow), 2 an enemy (red). Default: teammates, in every mode with teams.
    /// </summary>
    public virtual int SeesThroughWalls(PlayerRole viewer, PlayerRole target) => SameTeam(viewer, target) ? 1 : 0;

    /// <summary>
    /// Tag-like modes: every <see cref="Minimap"/> radar interval during the hunt, hunters see a red circle on the map
    /// around each hider (somewhere inside it), or a glow on the map's edge toward one too far away.
    /// </summary>
    public virtual bool HunterRadar => false;

    /// <summary>The radar starts with few, vague hints and builds up (Tag): the first sweeps come every
    /// <see cref="Minimap"/> 30 s with circles twice as wide, easing to the usual 20 s / 14 m over the first two minutes.</summary>
    public virtual bool RadarRampsUp => false;

    /// <summary>Gun modes where every shot from an enemy shows as a red dot on the minimap for a moment.</summary>
    public virtual bool ShowsShotsOnMap => false;

    /// <summary>Hunters pick a class and a left-click tool while the hiders hide (HunterClasses, HunterKit).</summary>
    public virtual bool HasHunterClasses => false;

    /// <summary>Extra points per second a hider earns while a hunter is within <see cref="DangerRange"/> (Tag: 1, so 2 a second in all).</summary>
    public virtual float DangerPointsPerSecond => 0f;
    public virtual float DangerRange => 12f;

    // ─── Roles dealt in Pregame ───────────────────────────────────────────────

    /// <summary>Master, when Pregame opens: deals the starting roles now, so everyone picks abilities knowing them.</summary>
    public void DealPregameRoles()
    {
        if (!PhotonNetwork.IsMasterClient || !DealsRolesInPregame) return;
        AssignStartingRoles();
        NetProps.SetRoom(RoomKeys.RolesDealt, true);
    }

    /// <summary>Modes with starting roles (Tag, Protect the President, Hunter, team modes) deal them in Pregame.</summary>
    protected virtual bool DealsRolesInPregame => false;

    /// <summary>Master: the mode's starting roles (also used by StartMatch when Pregame didn't deal them).</summary>
    protected virtual void AssignStartingRoles() { }

    /// <summary>
    /// Master, in StartMatch: true when Pregame dealt the roles and every player still has one (they're kept);
    /// otherwise the mode deals afresh (someone joined since, or the match skipped Pregame). Clears the flag.
    /// </summary>
    protected bool TakeDealtRoles()
    {
        bool dealt = NetProps.Room(RoomKeys.RolesDealt, false);
        if (!dealt) return false;
        NetProps.SetRoom(RoomKeys.RolesDealt, false);
        foreach (var p in PhotonNetwork.PlayerList)
            if (PlayerRoles.Get(p) == PlayerRole.None) return false;
        return true;
    }

    /// <summary>True when a player in <paramref name="role"/> wears a crown (seen by anyone their identity isn't hidden from).</summary>
    public virtual bool WearsCrown(PlayerRole role, MatchPhase phase) => false;

    /// <summary>The role's name on the HUD (modes rename them, e.g. hiders are "defenders").</summary>
    public virtual string RoleName(PlayerRole role) => role switch
    {
        PlayerRole.Hunter    => "HUNTER",
        PlayerRole.Hider     => "HIDER",
        PlayerRole.President => "PRESIDENT",
        _                    => "",
    };

    // ─── Scoreboard (read in the Scoreboard scene: no Manager there, only synced properties) ───

    /// <summary>Columns after the player's name.</summary>
    public virtual IReadOnlyList<ScoreboardColumn> ScoreboardColumns => new[]
    {
        new ScoreboardColumn("Abilities Used", p => MatchStats.Count(p, PlayerKeys.StatAbilitiesUsed)),
    };

    /// <summary>Columns for the in-match standings (hold Tab). Default: the scoreboard's.</summary>
    public virtual IReadOnlyList<ScoreboardColumn> LiveScoreColumns => ScoreboardColumns;

    /// <summary>Order of the rows (negative: <paramref name="a"/> first).</summary>
    public virtual int CompareForScoreboard(PhotonPlayer a, PhotonPlayer b) => 0;

    /// <summary>The headline, given the rows in order.</summary>
    public virtual string ScoreboardTitle(IReadOnlyList<PhotonPlayer> ranked) => "Match Over";

    /// <summary>"X wins!", or a tie when the top players can't be told apart by <see cref="CompareForScoreboard"/>.</summary>
    protected string WinnerTitle(IReadOnlyList<PhotonPlayer> ranked)
    {
        if (ranked.Count == 0) return "Match Over";
        var tied = new List<string>();
        foreach (var p in ranked)
            if (CompareForScoreboard(ranked[0], p) == 0) tied.Add(string.IsNullOrEmpty(p.NickName) ? $"Player {p.ActorNumber}" : p.NickName);
        if (tied.Count == 1) return $"{tied[0]} wins!";
        if (tied.Count == 2) return $"It's a tie! {tied[0]} and {tied[1]}";
        return "It's a draw!";
    }
}
