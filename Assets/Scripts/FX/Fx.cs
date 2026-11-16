using UnityEngine;

/// <summary>
/// One-call gameplay effects: each moment's sound (<see cref="Sfx"/>), particles (<see cref="Vfx"/>) and screen
/// shake together, so hooks around the game stay one line. Local only: call it on every client that should
/// see/hear it (from an RPC that runs on all, or from code that already runs everywhere).
/// </summary>
public static class Fx
{
    public static readonly Color Dust   = new(0.85f, 0.85f, 0.9f, 0.8f);
    public static readonly Color Cyan   = new(0.13f, 0.83f, 0.93f);
    public static readonly Color Yellow = new(1f, 0.79f, 0.24f);
    public static readonly Color Pink   = new(1f, 0.24f, 0.5f);
    public static readonly Color Lime   = new(0.49f, 0.91f, 0.35f);
    public static readonly Color Violet = new(0.55f, 0.36f, 0.96f);
    public static readonly Color Ice    = new(0.65f, 0.9f, 1f);

    // ─── Abilities ────────────────────────────────────────────────────────────

    /// <summary>
    /// The moment <paramref name="player"/> uses ability <paramref name="id"/>, on every screen: the ability's own
    /// sound and particles (<see cref="global::Ability.PlayUseFx"/>, written in the ability's class), and a mark on
    /// other players' sound visualizers unless it's a quiet one.
    /// </summary>
    public static void Ability(string id, Player player)
    {
        if (player == null) return;
        var ability = player.Abilities != null ? player.Abilities.Find(id) : null;
        if (ability == null) return;
        Vector3 feet  = player.transform.position;
        Vector3 chest = feet + Vector3.up * Mathf.Max(0.8f, player.EyeHeight * 0.6f);
        bool    mine  = player.IsLocal;
        if (!mine && ability.LoudUse) SoundVisualizer.Notify(feet, 1f, player.ActorNumber);
        ability.PlayUseFx(player, feet, chest, mine);
    }

    // ─── Guns ─────────────────────────────────────────────────────────────────

    /// <summary>A gun firing: <paramref name="local"/> = the shooter's own screen (flat, louder).</summary>
    public static void Shot(string gunId, Vector3 muzzle, bool local)
    {
        SfxKind kind = gunId switch
        {
            "AR" or "SMG" or "LMG" or "LongRifle" => SfxKind.Rifle,
            "Shotgun" => SfxKind.Shotgun,
            "Sniper" or "DesertEagle" => SfxKind.Sniper,
            _         => SfxKind.Pistol,
        };
        float range = kind == SfxKind.Sniper ? 160f : kind == SfxKind.Shotgun ? 90f : 80f;
        if (local) Sfx.Here(kind, 0.55f);
        else       Sfx.At(kind, muzzle, 1f, range);
    }

    /// <summary>Where a bullet stopped: sparks off a surface, a red puff off a player.</summary>
    public static void Impact(Vector3 point, Vector3 from, bool player)
    {
        Vector3 back = (from - point).normalized;
        if (player) Vfx.Burst(point, new Color(1f, 0.3f, 0.35f), 6, 2.5f, 0.14f, 0.3f, direction: back, spread: 0.8f);
        else        Vfx.Sparks(point, back, new Color(1f, 0.85f, 0.45f), 6);
    }

    // ─── World ────────────────────────────────────────────────────────────────

    /// <summary>A blast: boom, fire and smoke, and a shake that fades with the local player's distance.</summary>
    public static void Explosion(Vector3 position, float radius)
    {
        Sfx.At(SfxKind.Explosion, position, 1f, Mathf.Max(70f, radius * 10f));
        Vfx.Explosion(position, radius);
        var local = Player.Local;
        if (local == null) return;
        float distance = Vector3.Distance(local.transform.position, position);
        Shake(Mathf.Clamp01(1f - distance / (radius * 4f)) * 0.9f);
    }

    /// <summary>A penguin's feathers knocked loose by a hit (black and white puffs that drift down).</summary>
    public static void Feathers(Vector3 position, Vector3 outward, int count)
    {
        Vfx.Burst(position, new Color(0.95f, 0.95f, 0.97f), count, 4f, 0.16f, 0.9f, gravity: true, direction: outward, spread: 0.7f);
        Vfx.Burst(position, new Color(0.12f, 0.12f, 0.16f), count / 2, 3.5f, 0.14f, 0.9f, gravity: true, direction: outward, spread: 0.7f);
        Vfx.Burst(position, new Color(1f, 0.3f, 0.35f, 0.8f), count / 2, 2.5f, 0.2f, 0.3f, direction: outward, spread: 0.5f);
    }

    /// <summary>A bounce pad (or anything springy) launching someone.</summary>
    public static void Bounce(Vector3 position)
    {
        Sfx.At(SfxKind.Boing, position, 1f);
        Vfx.Ring(position, Dust, 24, 7f, 0.5f, 0.55f);
        Vfx.Burst(position, Lime, 10, 5f, 0.2f, 0.5f, direction: Vector3.up, spread: 0.5f);
    }

    /// <summary>A player hitting the ground hard (other players' landings and big drops).</summary>
    public static void Land(Vector3 feet, float speed)
    {
        float k = Mathf.InverseLerp(8f, 30f, speed);
        Sfx.At(SfxKind.Land, feet, Mathf.Lerp(0.4f, 1f, k), 30f);
        Vfx.Ring(feet, Dust, Mathf.RoundToInt(Mathf.Lerp(10f, 26f, k)), Mathf.Lerp(3f, 7f, k), Mathf.Lerp(0.3f, 0.55f, k), 0.45f);
    }

    /// <summary>A jump puff under someone else's feet (the jumper's own client has its own jump sound).</summary>
    public static void Jump(Vector3 feet)
    {
        Sfx.At(SfxKind.Jump, feet, 0.5f, 25f);
        Vfx.Ring(feet, Dust, 10, 2.5f, 0.3f, 0.35f);
    }

    /// <summary>Someone tagged (or killed): a slap and a burst of their penguin's colour.</summary>
    public static void Tagged(Vector3 position, bool byGun)
    {
        if (!byGun) Sfx.At(SfxKind.Tag, position, 1f, 50f);
        Vfx.Burst(position + Vector3.up, byGun ? new Color(1f, 0.3f, 0.35f) : Yellow, 26, 5f, 0.3f, 0.6f);
        Vfx.Burst(position + Vector3.up, Color.white, 14, 3f, 0.4f, 0.8f);
    }

    /// <summary>A player appearing at a spawn.</summary>
    public static void Respawn(Vector3 feet)
    {
        Sfx.At(SfxKind.Respawn, feet + Vector3.up, 0.7f, 30f);
        Vfx.Burst(feet + Vector3.up, Cyan, 22, 3f, 0.25f, 0.7f, direction: Vector3.up, spread: 0.7f);
        Vfx.Ring(feet, Color.white, 20, 4f, 0.35f, 0.5f);
    }

    /// <summary>A health pack taken.</summary>
    public static void Heal(Vector3 position, bool local)
    {
        if (local) Sfx.Here(SfxKind.Heal, 0.8f);
        else       Sfx.At(SfxKind.Heal, position, 0.7f, 25f);
        Vfx.Burst(position, Lime, 20, 3f, 0.25f, 0.7f, direction: Vector3.up, spread: 0.6f);
    }

    /// <summary>A splash of frost (snowball hits, ice).</summary>
    public static void Frost(Vector3 position)
    {
        Sfx.At(SfxKind.Ice, position, 0.8f, 30f);
        Vfx.Burst(position, Ice, 24, 4f, 0.25f, 0.7f);
    }

    /// <summary>Shakes the local camera (0–1).</summary>
    public static void Shake(float amount)
    {
        if (amount > 0f && PlayerCam.Local != null) PlayerCam.Local.AddShake(amount);
    }
}
