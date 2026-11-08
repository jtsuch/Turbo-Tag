using System;
using System.Collections.Generic;
using System.Globalization;
using Photon.Pun;
using UnityEngine;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// Admin cheats, carried out on the target's own machine. The admin's console (<see cref="AdminConsole"/>) sends
/// RPC_Admin to each target player (or to everyone for shared moments like stare, honk, announce); the receiver
/// checks the sender really is an admin (<see cref="AdminConsole.IsAdmin"/>) and applies it to its own player, the
/// same way an ability's effect runs on the owner. Lives next to GameModeManager and shares its scene PhotonView.
/// Add a command: an entry in <see cref="Help"/>, a case in <see cref="Apply"/> (or <see cref="ApplyEverywhere"/>).
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class AdminCommands : MonoBehaviourPun
{
    private static AdminCommands _instance;
    public static AdminCommands Instance => _instance;

    /// <summary>Source key for every stat change made by admin commands (so "reset" removes exactly those).</summary>
    public static readonly object Source = new();

    /// <summary>Command, arguments, what it does. Shown by "help"; the console also completes command names from it.</summary>
    public static readonly (string name, string args, string what)[] Help =
    {
        ("ability", "0|1",       "Turn their abilities off (0) or back on (1)"),
        ("freeze",  "[sec]",     "Stuck in place (default 5 s)"),
        ("ice",     "[sec]",     "Slippery feet (default 5 s)"),
        ("launch",  "[speed]",   "Straight up (default 35)"),
        ("yeet",    "[speed]",   "Off in a random direction (default 30)"),
        ("size",    "<x>",       "Body size multiplier (1 = normal)"),
        ("speed",   "<x>",       "Move speed multiplier"),
        ("jump",    "<x>",       "Jump multiplier"),
        ("gravity", "<x>",       "Gravity multiplier (0.2 = moon)"),
        ("cooldown", "<x>",      "Cooldown multiplier: 0 = no cooldowns (abilities, Basic meters, hunter tools), 1 = normal"),
        ("invert",  "0|1",       "Backwards controls"),
        ("blind",   "[sec]",     "White screen (default 3 s)"),
        ("dizzy",   "[sec]",     "Wobbly, drunk camera (default 8 s)"),
        ("spin",    "[sec]",     "Their view spins round and round (default 3 s)"),
        ("stare",   "[sec]",     "Everyone else is forced to look at them (default 5 s)"),
        ("honk",    "",          "A honk from nowhere bumps them"),
        ("god",     "0|1",       "Can't be hurt by guns"),
        ("heal",    "",          "Full health"),
        ("slay",    "",          "Dead on the spot (a fall: costs a point in Free for All)"),
        ("bring",   "",          "Teleport them to you"),
        ("say",     "<text>",    "A message on their screen only"),
        ("reset",   "",          "Undo every admin effect on them"),
    };

    private int   stareActor = -1;
    private float stareUntil;

    private void Awake() => _instance = this;

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    // ─── Sending (admin) ──────────────────────────────────────────────────────

    /// <summary>Admin: runs <paramref name="command"/> on <paramref name="target"/>. Returns false if it isn't a command.</summary>
    public bool Send(PhotonPlayer target, string command, string args)
    {
        if (!IsCommand(command)) return false;
        switch (command)
        {
            // Everyone sees (or is affected by) these
            case "stare":
            case "honk":
                photonView.RPC(nameof(RPC_Admin), RpcTarget.All, command, args, target.ActorNumber);
                break;
            case "bring":
                var me = Player.Local;
                if (me == null) return true;
                Vector3 p = me.transform.position + me.transform.forward * 2f;
                photonView.RPC(nameof(RPC_Admin), target, command, $"{F(p.x)} {F(p.y)} {F(p.z)}", target.ActorNumber);
                break;
            default:
                photonView.RPC(nameof(RPC_Admin), target, command, args, target.ActorNumber);
                break;
        }
        return true;
    }

    /// <summary>Admin: a banner on everyone's screen.</summary>
    public void Announce(string text) => photonView.RPC(nameof(RPC_Admin), RpcTarget.All, "announce", text, -1);

    public static bool IsCommand(string command)
    {
        foreach (var entry in Help) if (entry.name == command) return true;
        return false;
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    // ─── Receiving ────────────────────────────────────────────────────────────

    [PunRPC]
    private void RPC_Admin(string command, string args, int targetActor, PhotonMessageInfo info)
    {
        if (!AdminConsole.IsAdmin(info.Sender)) return; // Only admins can do this to you
        string by = info.Sender != null ? info.Sender.NickName : "?";

        if (ApplyEverywhere(command, args, targetActor)) return;
        if (targetActor != PhotonNetwork.LocalPlayer.ActorNumber) return;
        var me = Player.Local;
        if (me == null) return;
        Apply(me, command, args, by);
    }

    // Commands every screen takes part in
    private bool ApplyEverywhere(string command, string args, int targetActor)
    {
        switch (command)
        {
            case "announce":
                if (HUDManager.Instance != null) HUDManager.Instance.ShowBanner(args, 5f);
                Sfx.Here(SfxKind.Horn, 0.5f);
                return true;

            case "stare":
                stareActor = targetActor;
                stareUntil = Time.time + Number(args, 5f);
                return true;

            case "honk":
                var target = Player.Find(targetActor);
                if (target == null) return true;
                Sfx.At(SfxKind.Honk, target.transform.position + Vector3.up, 1f, 50f);
                Vfx.Burst(target.transform.position + Vector3.up, Color.white, 20, 6f, 0.3f, 0.4f);
                if (target.IsLocal)
                {
                    Vector3 dir = UnityEngine.Random.insideUnitSphere;
                    dir.y = Mathf.Abs(dir.y) + 0.6f;
                    WorldFx.Push(target.Body, dir.normalized * 16f, ForceMode.VelocityChange);
                    Fx.Shake(0.4f);
                }
                return true;
        }
        return false;
    }

    // Commands the target's own machine carries out on its player
    private static void Apply(Player me, string command, string args, string by)
    {
        var cam = PlayerCam.Local;
        switch (command)
        {
            case "ability":
                me.AdminAbilitiesOff = Number(args, 1f) < 0.5f;
                if (me.AdminAbilitiesOff && me.Abilities != null) me.Abilities.CancelAll();
                break;
            case "freeze":  me.HoldFor(Number(args, 5f)); break;
            case "ice":     me.IceFor(Number(args, 5f)); break;
            case "launch":
                Launch(me, Vector3.up * Number(args, 35f));
                break;
            case "yeet":
                Vector3 flat = UnityEngine.Random.insideUnitCircle.normalized;
                Launch(me, (new Vector3(flat.x, 0.6f, flat.y)).normalized * Number(args, 30f));
                break;
            case "size":    SetStat(me, Stat.Size, Number(args, 1f), 0.05f, 20f); break;
            case "speed":   SetStat(me, Stat.Speed, Number(args, 1f), 0f, 20f); break;
            case "jump":    SetStat(me, Stat.Jump, Number(args, 1f), 0f, 20f); break;
            case "cooldown": SetStat(me, Stat.Cooldown, Number(args, 1f), 0f, 10f); break;
            case "gravity": SetStat(me, Stat.Gravity, Number(args, 1f), -2f, 10f); break;
            case "invert":  SetStat(me, Stat.ControlDirection, Number(args, 1f) >= 0.5f ? -1f : 1f, -1f, 1f); break;
            case "blind":   ScreenFlash.Apply(1f, Number(args, 3f)); break;
            case "dizzy":   if (cam != null) cam.Dizzy(Number(args, 8f)); break;
            case "spin":    if (cam != null) cam.SpinAround(Number(args, 3f)); break;
            case "god":     me.AdminGod = Number(args, 1f) >= 0.5f; break;
            case "heal":    me.Health.Refill(); Fx.Heal(me.transform.position + Vector3.up, true); break;
            case "slay":
                if (Spawner.Instance != null) Spawner.Instance.LocalPlayerFell();
                break;
            case "bring":
                if (TryVector(args, out Vector3 to))
                {
                    me.Teleport(to, me.transform.rotation);
                    Fx.Respawn(to);
                }
                break;
            case "say":
                if (HUDManager.Instance != null) HUDManager.Instance.ShowBanner($"{by}: {args}", 5f);
                break;
            case "reset":
                me.Modifiers.Clear(Source);
                me.AdminAbilitiesOff = false;
                me.AdminGod = false;
                if (cam != null) cam.StopAdminEffects();
                break;
        }
    }

    private static void Launch(Player me, Vector3 velocity)
    {
        me.Body.linearVelocity = velocity;
        me.NotePushed();
        Fx.Bounce(me.transform.position);
    }

    private static void SetStat(Player me, Stat stat, float value, float min, float max)
    {
        value = Mathf.Clamp(value, min, max);
        if (Mathf.Approximately(value, 1f)) me.Modifiers.Remove(stat, Source);
        else me.Modifiers.Set(stat, Source, value);
    }

    private static float Number(string args, float fallback)
    {
        if (string.IsNullOrWhiteSpace(args)) return fallback;
        string first = args.Trim().Split(' ')[0];
        return float.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;
    }

    private static bool TryVector(string args, out Vector3 v)
    {
        v = default;
        var parts = (args ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return false;
        var c = CultureInfo.InvariantCulture;
        if (!float.TryParse(parts[0], NumberStyles.Float, c, out v.x)) return false;
        if (!float.TryParse(parts[1], NumberStyles.Float, c, out v.y)) return false;
        return float.TryParse(parts[2], NumberStyles.Float, c, out v.z);
    }

    // ─── Stare (every client) ─────────────────────────────────────────────────

    private void Update()
    {
        if (stareActor < 0 || Time.time > stareUntil) { stareActor = -1; return; }
        var local = Player.Local;
        var target = Player.Find(stareActor);
        if (local == null || target == null || target == local || PlayerCam.Local == null) return;
        PlayerCam.Local.Lure(target.transform.position + Vector3.up * target.EyeHeight);
    }

    /// <summary>Players whose name starts with <paramref name="prefix"/> (an exact match wins).</summary>
    public static List<PhotonPlayer> MatchPlayers(string prefix)
    {
        var found = new List<PhotonPlayer>();
        foreach (var p in PhotonNetwork.PlayerList)
        {
            string name = p.NickName ?? "";
            if (string.Equals(name, prefix, StringComparison.OrdinalIgnoreCase)) return new List<PhotonPlayer> { p };
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) found.Add(p);
        }
        return found;
    }
}
