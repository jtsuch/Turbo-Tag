using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Hashtable    = ExitGames.Client.Photon.Hashtable;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// Search &amp; Destroy: Red against Blue in rounds, one life each. One team attacks: the bomb starts on the floor in
/// front of the attackers' spawn (walk over it to pick it up, the Drop Bomb key drops it), and it must be planted at a bomb site (<see cref="BombSite"/>, hold the Grab key for <see cref="MatchRules.PlantTime"/>);
/// the other defends and can defuse a planted bomb (<see cref="MatchRules.DefuseTime"/>). A round ends when:
///   - before a plant: a team is wiped out (the other wins), or the round clock runs out (defenders win);
///   - after a plant: the defenders are wiped out or the bomb goes off (attackers win), or it's defused (defenders win).
/// The bomb drops where its carrier dies; any attacker picks it up by touching it. Dead players watch a living
/// teammate until the next round. Teams swap sides after <see cref="MatchRules.RoundsToWin"/> − 1 rounds; the first
/// to <see cref="MatchRules.RoundsToWin"/> round wins takes the match. Guns (picker every round), no health drops,
/// abilities on (all of them, for now), teammates through walls and on the map (TeamMode).
///
/// Flow per round: pre-round (the Countdown phase, 40 s: pick a gun and plan; everyone can move and use abilities but
/// stays in their spawn area, SearchDestroyController) → time to plant (Active, <see cref="MatchRules.RoundTime"/>, 2 min)
/// → time to defuse (still Active: a plant restarts the clock at <see cref="MatchRules.BombTimer"/>) → RoundEnd (a short
/// look at what happened) → next round, or MatchEnd → Scoreboard. Search &amp; Destroy maps are their own category
/// (Assets/Scenes/Levels/SearchDestroy); choosing the mode switches the lobby to one.
/// The bomb, plant/defuse input, HUD and spectating live in <see cref="SearchDestroyController"/>.
/// </summary>
public class SearchDestroyMode : TeamMode
{
    public enum BombState { Carried = 0, Dropped = 1, Planted = 2, Defused = 3, Exploded = 4 }

    public const string ReasonEliminated = "elim";
    public const string ReasonTime       = "time";
    public const string ReasonDetonated  = "boom";
    public const string ReasonDefused    = "defused";

    public const float PickupRange     = 2.2f;
    public const float DefuseRange     = 2.6f;
    public const float ExplosionRadius = 30f; // Everyone inside dies, whichever team

    private const float RoundEndSeconds = 6f;
    private const float MatchEndSeconds = 6f;

    // ─── Master state (mirrors the room, which only updates on Photon's echo) ──

    private string attack = Red, firstAttack = Red, history = "";
    private int redWins, blueWins, round;
    private BombState bomb;
    private int carrier = -1;
    private Vector3 bombPos, carrierPos;
    private readonly HashSet<int> dead = new();
    private float deadline;      // Time.time the round clock / bomb runs out
    private bool bothTeams;      // Both sides had players when the round started (else wipe-outs don't end it: solo practice)
    private bool matchOver;
    private bool synced = true;  // False on a new master until it reads the room
    private bool wasMaster;

    public override string DisplayName => "Search & Destroy";
    public override bool   UsesGuns    => true;
    public override bool   HasRounds   => true;
    public override bool   AllowsHealthDrops => false;
    public override bool   ShowsScores => true;

    public override bool CanDamage(PlayerRole attacker, PlayerRole target) => !SameTeam(attacker, target);

    // Enemy names are never shown (only your own team's)
    public override bool ShowsName(PlayerRole viewer, PlayerRole target) => SameTeam(viewer, target);

    // ─── Synced reads (every client) ──────────────────────────────────────────

    public static string     AttackTeam => NetProps.Room(RoomKeys.SnDAttack, Red);
    public static PlayerRole AttackRole => AttackTeam == Blue ? PlayerRole.TeamBlue : PlayerRole.TeamRed;
    public static PlayerRole DefendRole => PlayerRoles.Opponent(AttackRole);
    public static bool       IsAttacker(PlayerRole role) => PlayerRoles.IsTeam(role) && role == AttackRole;
    public static BombState  Bomb       => (BombState)NetProps.Room(RoomKeys.SnDBomb, 0);
    public static int        Carrier    => NetProps.Room(RoomKeys.SnDCarrier, -1);
    public static Vector3    BombPos    => NetProps.Room(RoomKeys.SnDBombPos, Vector3.zero);
    public static string     Site       => NetProps.Room(RoomKeys.SnDSite, "");
    public static int        RedWins    => NetProps.Room(RoomKeys.SnDRedWins, 0);
    public static int        BlueWins   => NetProps.Room(RoomKeys.SnDBlueWins, 0);
    public static string     RoundWinner => NetProps.Room(RoomKeys.SnDRoundWinner, "");
    public static string     RoundReason => NetProps.Room(RoomKeys.SnDReason, "");
    /// <summary>Each finished round's winner in order: 'R' or 'B'.</summary>
    public static string     History    => NetProps.Room(RoomKeys.SnDHistory, "");
    public static string     FirstAttack => NetProps.Room(RoomKeys.SnDFirstAttack, Red);
    /// <summary>Most rounds a match can last (first to N: 2N − 1).</summary>
    public static int        MaxRounds  => MatchRules.RoundsToWin * 2 - 1;
    /// <summary>Rounds played before the teams swap sides.</summary>
    public static int        HalfTime   => MatchRules.RoundsToWin - 1;

    /// <summary>Which team attacks in round <paramref name="round"/> (1-based).</summary>
    public static string AttackerIn(int round)
    {
        string first = FirstAttack;
        bool swapped = HalfTime > 0 && round > HalfTime;
        return swapped ? (first == Red ? Blue : Red) : first;
    }

    /// <summary>True if <paramref name="actor"/> is out for the rest of this round.</summary>
    public static bool IsDead(int actor)
    {
        var list = NetProps.Room(RoomKeys.SnDDead, System.Array.Empty<int>());
        foreach (int a in list) if (a == actor) return true;
        return false;
    }

    /// <summary>Players of <paramref name="team"/> still alive this round.</summary>
    public static int AliveCount(PlayerRole team)
    {
        int n = 0;
        foreach (var p in PhotonNetwork.PlayerList)
            if (PlayerRoles.Get(p) == team && !IsDead(p.ActorNumber)) n++;
        return n;
    }

    public static string TeamName(string team) => team == Blue ? "Blue" : "Red";
    public static string TeamColor(string team) => team == Blue ? "#33A6FF" : "#FF4D6D";

    // ─── Master: match flow ───────────────────────────────────────────────────

    public override void StartMatch()
    {
        Scores.ResetAll();
        if (!TakeDealtRoles()) AssignStartingRoles();
        attack = firstAttack = Random.value < 0.5f ? Red : Blue;
        history = "";
        redWins = blueWins = 0;
        matchOver = false;
        StartRound(1);
    }

    private void StartRound(int number)
    {
        round = number;
        dead.Clear();
        // The bomb waits on the floor in front of the attackers' spawn
        bomb = BombState.Dropped;
        carrier = -1;
        bombPos = StartingBombSpot();
        var attackers = PlayerRoles.With(AttackingRole);
        var defenders = PlayerRoles.With(PlayerRoles.Opponent(AttackingRole));
        bothTeams = attackers.Count > 0 && defenders.Count > 0;
        Manager.SetPhase(MatchPhase.Countdown, Mathf.Max(MatchRules.CountdownTime, 3f), new Hashtable
        {
            [RoomKeys.Round]          = number,
            [RoomKeys.SnDAttack]      = attack,
            [RoomKeys.SnDRedWins]     = redWins,
            [RoomKeys.SnDBlueWins]    = blueWins,
            [RoomKeys.SnDBomb]        = (int)bomb,
            [RoomKeys.SnDCarrier]     = -1,
            [RoomKeys.SnDBombPos]     = bombPos,
            [RoomKeys.SnDHistory]     = history,
            [RoomKeys.SnDFirstAttack] = firstAttack,
            [RoomKeys.SnDSite]        = "",
            [RoomKeys.SnDDead]        = new int[0],
            [RoomKeys.SnDRoundWinner] = "",
            [RoomKeys.SnDReason]      = "",
        });
    }

    private PlayerRole AttackingRole => attack == Blue ? PlayerRole.TeamBlue : PlayerRole.TeamRed;

    // A few metres in front of the middle of the attackers' spawn points, on the floor
    private Vector3 StartingBombSpot()
    {
        var points = new List<SpawnPoint>(SpawnPoint.All);
        points.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        var spawns = points.Count >= 2 ? SpawnsFor(points, attacking: true) : points;
        Vector3 centre = Spawner.Instance != null ? Spawner.Instance.transform.position : Vector3.zero, facing = Vector3.zero;
        if (spawns.Count > 0)
        {
            centre = Centre(spawns);
            foreach (var p in spawns) facing += p.transform.forward;
        }
        facing = Vector3.ProjectOnPlane(facing, Vector3.up);
        Vector3 spot = centre + (facing.sqrMagnitude > 0.01f ? facing.normalized * 3f : Vector3.zero);
        return Physics.Raycast(spot + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            ? hit.point : centre;
    }

    public override void MasterUpdate()
    {
        bool master = PhotonNetwork.IsMasterClient;
        if (master && !wasMaster && Phase != MatchPhase.Waiting) synced = false; // Took over from another host
        wasMaster = master;
        if (!synced) SyncFromRoom();

        PlaceNewcomers();

        switch (Phase)
        {
            case MatchPhase.Countdown:
                if (Clock.IsExpired)
                {
                    deadline = Time.time + MatchRules.RoundTime;
                    Manager.SetPhase(MatchPhase.Active, MatchRules.RoundTime);
                }
                break;

            case MatchPhase.Active:
                ActiveUpdate();
                break;

            case MatchPhase.RoundEnd:
                if (!Clock.IsExpired) break;
                if (matchOver)
                    Manager.SetPhase(MatchPhase.MatchEnd, MatchEndSeconds,
                        new Hashtable { [RoomKeys.WinningTeam] = redWins > blueWins ? Red : Blue });
                else
                {
                    if (round == HalfTime) attack = attack == Red ? Blue : Red; // Half time: swap sides
                    StartRound(round + 1);
                }
                break;

            case MatchPhase.MatchEnd:
                if (Clock.IsExpired) Manager.GoToScoreboard();
                break;
        }
    }

    // A new host rebuilds the match from the room
    private void SyncFromRoom()
    {
        synced = true;
        attack    = AttackTeam;
        firstAttack = FirstAttack;
        history   = History;
        redWins   = RedWins;
        blueWins  = BlueWins;
        round     = Manager.Round;
        bomb      = Bomb;
        carrier   = Carrier;
        bombPos   = BombPos;
        dead.Clear();
        foreach (int a in NetProps.Room(RoomKeys.SnDDead, System.Array.Empty<int>())) dead.Add(a);
        deadline  = Time.time + Clock.Remaining;
        bothTeams = PlayerRoles.Count(PlayerRole.TeamRed) > 0 && PlayerRoles.Count(PlayerRole.TeamBlue) > 0;
        matchOver = Mathf.Max(redWins, blueWins) >= MatchRules.RoundsToWin;
    }

    // Anyone without a team (joined mid-match) goes to the smaller one, and sits out the round in progress
    private void PlaceNewcomers()
    {
        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (PlayerRoles.IsTeam(PlayerRoles.Get(p))) continue;
            int red = PlayerRoles.Count(PlayerRole.TeamRed), blue = PlayerRoles.Count(PlayerRole.TeamBlue);
            PlayerRoles.Set(p, red <= blue ? PlayerRole.TeamRed : PlayerRole.TeamBlue);
            if (Phase == MatchPhase.Active && dead.Add(p.ActorNumber)) WriteDead();
        }
    }

    protected override void ActiveUpdate()
    {
        PlayerRole attackers = AttackingRole, defenders = PlayerRoles.Opponent(attackers);

        // The carrier is gone (left, or swapped team): the bomb drops where they were last seen
        if (bomb == BombState.Carried)
        {
            var holder = PhotonNetwork.CurrentRoom?.GetPlayer(carrier);
            var body = Player.Find(carrier);
            if (body != null && !body.IsDownAfterTag) carrierPos = body.transform.position;
            if (holder == null || PlayerRoles.Get(holder) != attackers || dead.Contains(carrier)) DropBomb(carrierPos);
        }

        int att = AliveMaster(attackers), def = AliveMaster(defenders);
        if (bomb == BombState.Planted)
        {
            if (bothTeams && def == 0) EndRound(attack, ReasonEliminated);
            else if (Time.time >= deadline) Detonate();
        }
        else
        {
            if (bothTeams && att == 0) EndRound(Other(attack), ReasonEliminated);
            else if (bothTeams && def == 0) EndRound(attack, ReasonEliminated);
            else if (Time.time >= deadline) EndRound(Other(attack), ReasonTime);
        }
    }

    private static string Other(string team) => team == Red ? Blue : Red;

    private int AliveMaster(PlayerRole team)
    {
        int n = 0;
        foreach (var p in PhotonNetwork.PlayerList)
            if (PlayerRoles.Get(p) == team && !dead.Contains(p.ActorNumber)) n++;
        return n;
    }

    private void WriteDead()
    {
        var list = new int[dead.Count];
        dead.CopyTo(list);
        NetProps.SetRoom(RoomKeys.SnDDead, list);
    }

    private void EndRound(string winner, string reason)
    {
        if (Phase != MatchPhase.Active) return;
        if (winner == Red) redWins++; else blueWins++;
        history += winner == Red ? "R" : "B";
        matchOver = Mathf.Max(redWins, blueWins) >= MatchRules.RoundsToWin;
        var winners = winner == Red ? PlayerRole.TeamRed : PlayerRole.TeamBlue;
        foreach (var p in PhotonNetwork.PlayerList)
            if (PlayerRoles.Get(p) == winners && !dead.Contains(p.ActorNumber)) MatchStats.Add(p, PlayerKeys.StatRoundsSurvived);
        Manager.SetPhase(MatchPhase.RoundEnd, RoundEndSeconds, new Hashtable
        {
            [RoomKeys.SnDRedWins]     = redWins,
            [RoomKeys.SnDBlueWins]    = blueWins,
            [RoomKeys.SnDRoundWinner] = winner,
            [RoomKeys.SnDHistory]     = history,
            [RoomKeys.SnDReason]      = reason,
            [RoomKeys.SnDBomb]        = (int)bomb,
        });
    }

    // ─── Master: the bomb ─────────────────────────────────────────────────────

    // Dropped on the floor under where the carrier was; over the void (or nowhere sensible) it goes to a living attacker
    private void DropBomb(Vector3 at)
    {
        if (Physics.Raycast(at + Vector3.up, Vector3.down, out RaycastHit hit, 30f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            bomb = BombState.Dropped;
            bombPos = hit.point;
            carrier = -1;
            NetProps.SetRoom(new Hashtable { [RoomKeys.SnDBomb] = (int)bomb, [RoomKeys.SnDCarrier] = -1, [RoomKeys.SnDBombPos] = bombPos });
            return;
        }
        var alive = PlayerRoles.With(AttackingRole).FindAll(p => !dead.Contains(p.ActorNumber));
        carrier = alive.Count > 0 ? alive[Random.Range(0, alive.Count)].ActorNumber : -1;
        bomb = BombState.Carried;
        NetProps.SetRoom(new Hashtable { [RoomKeys.SnDBomb] = (int)bomb, [RoomKeys.SnDCarrier] = carrier });
    }

    /// <summary>Master: an attacker touched the dropped bomb.</summary>
    public void MasterPickup(PhotonPlayer who)
    {
        if ((Phase != MatchPhase.Active && Phase != MatchPhase.Countdown) || bomb != BombState.Dropped || who == null) return;
        if (PlayerRoles.Get(who) != AttackingRole || dead.Contains(who.ActorNumber)) return;
        var body = Player.Find(who.ActorNumber);
        if (body != null && Vector3.Distance(body.transform.position, bombPos) > PickupRange + 3f) return; // Slack for lag
        bomb = BombState.Carried;
        carrier = who.ActorNumber;
        NetProps.SetRoom(new Hashtable { [RoomKeys.SnDBomb] = (int)bomb, [RoomKeys.SnDCarrier] = carrier });
    }

    /// <summary>Master: the carrier dropped the bomb (the Drop Bomb key) at <paramref name="at"/>.</summary>
    public void MasterDrop(PhotonPlayer who, Vector3 at)
    {
        if ((Phase != MatchPhase.Active && Phase != MatchPhase.Countdown) || bomb != BombState.Carried || who == null || who.ActorNumber != carrier) return;
        DropBomb(at);
    }

    /// <summary>Master: the carrier finished planting at <paramref name="at"/> on site <paramref name="letter"/>.</summary>
    public void MasterPlant(PhotonPlayer who, Vector3 at, string letter)
    {
        if (Phase != MatchPhase.Active || bomb != BombState.Carried || who == null || who.ActorNumber != carrier) return;
        if (dead.Contains(who.ActorNumber) || Time.time >= deadline) return;
        bomb = BombState.Planted;
        bombPos = at;
        carrier = -1;
        float timer = MatchRules.BombTimer;
        deadline = Time.time + timer;
        MatchStats.Add(who, PlayerKeys.StatPlants);
        Add(who, PlayerKeys.Score, 1f);
        var props = Clock.BuildRestart(timer); // The clock now counts the bomb down (same phase: no "GO!" again)
        props[RoomKeys.SnDBomb]    = (int)bomb;
        props[RoomKeys.SnDCarrier] = -1;
        props[RoomKeys.SnDBombPos] = bombPos;
        props[RoomKeys.SnDSite]    = letter ?? "";
        NetProps.SetRoom(props);
    }

    /// <summary>Master: a defender finished defusing.</summary>
    public void MasterDefuse(PhotonPlayer who)
    {
        if (Phase != MatchPhase.Active || bomb != BombState.Planted || who == null) return;
        if (PlayerRoles.Get(who) == AttackingRole || dead.Contains(who.ActorNumber) || Time.time >= deadline) return;
        var body = Player.Find(who.ActorNumber);
        if (body != null && Vector3.Distance(body.transform.position, bombPos) > DefuseRange + 3f) return;
        bomb = BombState.Defused;
        MatchStats.Add(who, PlayerKeys.StatDefuses);
        Add(who, PlayerKeys.Score, 2f);
        NetProps.SetRoom(RoomKeys.SnDBomb, (int)bomb);
        EndRound(Other(attack), ReasonDefused);
    }

    private void Detonate()
    {
        bomb = BombState.Exploded;
        NetProps.SetRoom(RoomKeys.SnDBomb, (int)bomb);
        if (SearchDestroyController.Instance != null) SearchDestroyController.Instance.MasterExplode(bombPos);
        EndRound(attack, ReasonDetonated);
    }

    // ─── Master: deaths ───────────────────────────────────────────────────────

    public override void OnPlayerKilled(PhotonPlayer victim, PhotonPlayer killer, string weapon, bool headshot)
    {
        if (!dead.Add(victim.ActorNumber)) return;
        WriteDead();
        MatchStats.Add(victim, PlayerKeys.StatDeaths);
        if (killer == null) MatchStats.Add(victim, PlayerKeys.StatSelfDestructs);
        else if (!SameTeam(PlayerRoles.Get(killer), PlayerRoles.Get(victim)))
        {
            Add(killer, PlayerKeys.Score, 1f);
            MatchStats.Add(killer, PlayerKeys.StatKills);
            if (headshot) MatchStats.Add(killer, PlayerKeys.StatHeadshots);
        }

        if (bomb == BombState.Carried && victim.ActorNumber == carrier)
        {
            var body = Player.Find(victim.ActorNumber);
            DropBomb(body != null ? body.transform.position : carrierPos);
        }
    }

    // ─── All clients ──────────────────────────────────────────────────────────

    public override bool KeepsDead(MatchPhase phase) =>
        phase == MatchPhase.Active || phase == MatchPhase.RoundEnd || phase == MatchPhase.MatchEnd;

    // Pre-round: guns fire too (damage only counts once the round is live)
    public override bool CanShoot(MatchPhase phase) =>
        phase == MatchPhase.Countdown || phase == MatchPhase.Active || phase == MatchPhase.RoundEnd; // Post-round: shoot freely, nobody takes damage

    // Pre-round: everyone moves (within their spawn area) and can set up with abilities
    public override bool CanMove(PlayerRole role, MatchPhase phase) => true;

    public override bool CanUseAbilities(PlayerRole role, MatchPhase phase) =>
        (phase == MatchPhase.Countdown || phase == MatchPhase.Active || phase == MatchPhase.RoundEnd) && MatchRules.GunModeAbilities;

    public override float RespawnDelayAfter(PhotonPlayer tagger, PhotonPlayer victim) =>
        MatchRules.Killcam && tagger != null && tagger != victim ? Killcam.Length : 2f;

    // Attackers start far from the sites, defenders near them: tagged spawn points if the level has them
    public override List<SpawnPoint> TeamSpawns(List<SpawnPoint> points, PlayerRole role) => SpawnsFor(points, IsAttacker(role));

    /// <summary>The attackers' or defenders' spawn points.</summary>
    public static List<SpawnPoint> SpawnsFor(List<SpawnPoint> points, bool attacking)
    {
        var side = attacking ? SpawnPoint.Side.Attack : SpawnPoint.Side.Defend;
        var tagged = points.FindAll(p => p.side == side);
        if (tagged.Count > 0) return tagged;

        var low = Spawner.TeamHalf(points, PlayerRole.TeamRed);
        var high = Spawner.TeamHalf(points, PlayerRole.TeamBlue);
        Vector3 sites = BombSite.Middle();
        bool lowDefends = Vector3.Distance(Centre(low), sites) <= Vector3.Distance(Centre(high), sites);
        return attacking == lowDefends ? high : low;
    }

    private static Vector3 Centre(List<SpawnPoint> points)
    {
        Vector3 sum = Vector3.zero;
        foreach (var p in points) sum += p.transform.position;
        return points.Count > 0 ? sum / points.Count : sum;
    }

    public override string TeamScoreText =>
        $"<size=80%><color=#FF4D6D>{RedWins}</color><color=#B9B2E0> - </color><color=#33A6FF>{BlueWins}</color></size>";

    public override string RoleName(PlayerRole role) => role switch
    {
        PlayerRole.TeamRed  => IsAttacker(role) ? "RED · ATTACK" : "RED · DEFEND",
        PlayerRole.TeamBlue => IsAttacker(role) ? "BLUE · ATTACK" : "BLUE · DEFEND",
        _                   => base.RoleName(role),
    };

    public override string GetPhaseLabel(MatchPhase phase) => phase switch
    {
        MatchPhase.Countdown => "Pre-Round",
        MatchPhase.Active    => Bomb == BombState.Planted ? $"Bomb Planted {Site}" : "Time to Plant",
        MatchPhase.RoundEnd  => $"{TeamName(RoundWinner)} Wins the Round",
        _                    => base.GetPhaseLabel(phase),
    };

    public override string GetObjective(PlayerRole role, MatchPhase phase)
    {
        if (phase != MatchPhase.Active && phase != MatchPhase.Countdown) return "";
        string sites = SiteList();
        bool planted = Bomb == BombState.Planted;
        if (IsAttacker(role))
        {
            if (planted) return "Guard the bomb until it goes off!";
            if (Carrier == PhotonNetwork.LocalPlayer.ActorNumber) return $"You have the bomb! Plant it at {sites} (hold {Keybinds.Label(Keybinds.Get(InputAction.Bomb))}).";
            if (phase == MatchPhase.Countdown) return $"Pre-round: pick a gun and plan. Grab the bomb, then plant it at {sites}.";
            return $"Plant the bomb at {sites}, or take out every defender.";
        }
        if (planted) return $"Bomb planted at {Site}! Defuse it (hold {Keybinds.Label(Keybinds.Get(InputAction.Bomb))}).";
        if (phase == MatchPhase.Countdown) return $"Pre-round: pick a gun and plan how to hold {sites}.";
        return $"Defend {sites}: stop the plant, or take out every attacker.";
    }

    private static string SiteList()
    {
        var zones = BombSite.Zones;
        if (zones.Count == 0) return "a site";
        var names = new List<string>();
        foreach (var z in zones) names.Add(z.Letter);
        return names.Count == 1 ? names[0] : string.Join(", ", names.GetRange(0, names.Count - 1)) + " or " + names[^1];
    }

    // ─── Scoreboard ───────────────────────────────────────────────────────────

    public override IReadOnlyList<ScoreboardColumn> ScoreboardColumns => new[]
    {
        new ScoreboardColumn("Points",         p => Mathf.RoundToInt(NetProps.Of(p, PlayerKeys.Score, 0f)).ToString()),
        new ScoreboardColumn("Team",           TeamLabel),
        new ScoreboardColumn("Kills",          p => MatchStats.Count(p, PlayerKeys.StatKills)),
        new ScoreboardColumn("Deaths",         p => MatchStats.Count(p, PlayerKeys.StatDeaths)),
        new ScoreboardColumn("Assists",        p => MatchStats.Count(p, PlayerKeys.StatAssists)),
        new ScoreboardColumn("Damage",         p => MatchStats.Count(p, PlayerKeys.StatDamage)),
        new ScoreboardColumn("Plants",         p => MatchStats.Count(p, PlayerKeys.StatPlants)),
        new ScoreboardColumn("Defuses",        p => MatchStats.Count(p, PlayerKeys.StatDefuses)),
        new ScoreboardColumn("Abilities Used", p => MatchStats.Count(p, PlayerKeys.StatAbilitiesUsed)),
    };

    public override string FinalScore(PlayerRole team) => (team == PlayerRole.TeamBlue ? BlueWins : RedWins).ToString();
    public override string FinalScoreUnit => "rounds";

    public override string ScoreboardTitle(IReadOnlyList<PhotonPlayer> ranked)
    {
        string score = $"{RedWins} - {BlueWins}";
        return Winner switch
        {
            Red  => $"Red Team wins!  {score}",
            Blue => $"Blue Team wins!  {score}",
            _    => $"Draw!  {score}",
        };
    }
}
