using PhotonPlayer = Photon.Realtime.Player;

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

    /// <summary>True if the HUD should show the match clock.</summary>
    public virtual bool ShowsTimer => true;

    // ─── Master-client hooks ──────────────────────────────────────────────────

    /// <summary>Master: called once every player has spawned into the level.</summary>
    public abstract void StartMatch();

    /// <summary>Master: called every frame while the match is running.</summary>
    public virtual void MasterUpdate() { }

    /// <summary>Master: a client reported that <paramref name="tagger"/> touched <paramref name="target"/>.</summary>
    public virtual void HandleTagRequest(PhotonPlayer tagger, PhotonPlayer target) { }

    /// <summary>Master: a player disconnected mid-match.</summary>
    public virtual void OnPlayerLeft(PhotonPlayer player) { }

    // ─── All-client hooks ─────────────────────────────────────────────────────

    public virtual void OnPhaseChanged(MatchPhase phase) { }

    /// <summary>The local player physically touched another player.</summary>
    public virtual void OnLocalPlayerContact(Player local, Player other) { }

    // ─── Queries (must be deterministic from synced state) ────────────────────

    public virtual bool CanMove(PlayerRole role, MatchPhase phase) => true;

    public virtual bool CanUseAbilities(PlayerRole role, MatchPhase phase) => CanMove(role, phase);

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
}
