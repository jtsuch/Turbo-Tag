using UnityEngine;

/// <summary>
/// One-call gameplay effects: each moment's sound (<see cref="Sfx"/>), particles (<see cref="Vfx"/>) and screen
/// shake together, so hooks around the game stay one line. Local only: call it on every client that should
/// see/hear it (from an RPC that runs on all, or from code that already runs everywhere).
/// </summary>
public static class Fx
{
    private static readonly Color Dust   = new(0.85f, 0.85f, 0.9f, 0.8f);
    private static readonly Color Cyan   = new(0.13f, 0.83f, 0.93f);
    private static readonly Color Yellow = new(1f, 0.79f, 0.24f);
    private static readonly Color Pink   = new(1f, 0.24f, 0.5f);
    private static readonly Color Lime   = new(0.49f, 0.91f, 0.35f);
    private static readonly Color Violet = new(0.55f, 0.36f, 0.96f);
    private static readonly Color Ice    = new(0.65f, 0.9f, 1f);

    // ─── Abilities ────────────────────────────────────────────────────────────

    /// <summary>True when <see cref="Ability"/> has a use effect (AbilityHandler skips the RPC otherwise).</summary>
    public static bool HasAbilityFx(string id) => id switch
    {
        nameof(BoomBomb) => false, // Its use is counted at the blast, which has its own effect
        nameof(Honk)     => false, // Plays its own (RPC_Honk: horn, sound and push)
        nameof(GroundPound) => true,
        _                => true,
    };

    /// <summary>The moment <paramref name="player"/> uses ability <paramref name="id"/>: on every screen.</summary>
    public static void Ability(string id, Player player)
    {
        if (player == null) return;
        Vector3 feet  = player.transform.position;
        Vector3 chest = feet + Vector3.up * Mathf.Max(0.8f, player.EyeHeight * 0.6f);
        bool    mine  = player.IsLocal;

        switch (id)
        {
            // Basic
            case nameof(Flappy):
                Sfx.At(SfxKind.Flap, chest, 0.8f);
                Vfx.Burst(chest, Color.white, 10, 3f, 0.25f, 0.5f);
                break;
            case nameof(SwingGrapple):
            case nameof(SpringyGrapple):
            case nameof(ZipGrapple):
                Sfx.At(SfxKind.Thwip, chest, 0.9f);
                break;
            case nameof(LeafBlower):
                Sfx.At(SfxKind.Blow, chest, 0.9f);
                Vfx.Burst(chest, Lime, 12, 5f, 0.2f, 0.6f, direction: player.transform.forward, spread: 0.4f);
                break;
            case nameof(Chameleon):
                Sfx.At(SfxKind.Shimmer, chest, 0.7f);
                Vfx.Burst(chest, Lime, 18, 2.5f, 0.25f, 0.7f);
                break;
            case nameof(WallMagnet):
                Sfx.At(SfxKind.Snap, feet, 0.8f);
                Sfx.At(SfxKind.ZapDown, chest, 0.5f);
                Vfx.Ring(feet, Violet, 16, 3f, 0.25f, 0.4f);
                break;
            case nameof(Shield):
                Sfx.At(SfxKind.Shimmer, chest, 0.9f);
                Vfx.Burst(chest, Cyan, 24, 4f, 0.3f, 0.6f);
                break;
            case nameof(ZeroGravity):
                Sfx.At(SfxKind.Shimmer, chest, 0.7f, 45f, 0.7f);
                Vfx.Burst(chest, Violet, 20, 2f, 0.3f, 1f);
                break;
            case nameof(InfiniteClimb):
                Sfx.At(SfxKind.ZapUp, chest, 0.6f);
                Vfx.Burst(chest, Yellow, 10, 2f, 0.2f, 0.5f, direction: Vector3.up, spread: 0.6f);
                break;

            // Quick
            case nameof(Dash):
                Sfx.At(SfxKind.Whoosh, chest, 1f);
                Vfx.Burst(chest, Cyan, 16, 4f, 0.25f, 0.45f, direction: -player.Velocity, spread: 0.5f);
                if (mine) Shake(0.15f);
                break;
            case nameof(Launch):
                Sfx.At(SfxKind.Boing, feet, 1f);
                Sfx.At(SfxKind.Whoosh, chest, 0.7f);
                Vfx.Ring(feet, Dust, 26, 8f, 0.6f, 0.6f);
                Vfx.Burst(feet, Yellow, 14, 5f, 0.22f, 0.5f, direction: Vector3.down, spread: 0.7f);
                if (mine) Shake(0.25f);
                break;
            case nameof(Teleport):
                Sfx.At(SfxKind.Warp, chest, 1f);
                Vfx.Burst(chest, Violet, 30, 5f, 0.3f, 0.6f);
                break;
            case nameof(Shrink):
                Sfx.At(SfxKind.ZapDown, chest, 0.9f);
                Vfx.Burst(chest, Pink, 18, 3.5f, 0.2f, 0.5f);
                break;
            case nameof(GrowRay):
                Sfx.At(SfxKind.ZapUp, chest, 0.9f);
                break;
            case nameof(Cloak):
                Sfx.At(SfxKind.Shimmer, chest, 0.9f);
                Vfx.Burst(chest, new Color(1f, 1f, 1f, 0.6f), 24, 2.5f, 0.3f, 0.8f);
                break;
            case nameof(GroundPound):
                Sfx.At(SfxKind.Whoosh, chest, 1f, 45f, 0.7f);
                Vfx.Burst(chest, Color.white, 12, 4f, 0.25f, 0.4f, direction: Vector3.up, spread: 0.4f);
                break;
            case nameof(Adrenaline):
                Sfx.At(SfxKind.Heartbeat, chest, 1f);
                Vfx.Burst(chest, Pink, 16, 3.5f, 0.25f, 0.5f);
                if (mine) Shake(0.1f);
                break;

            // Throw and trap: by slot
            default:
                var ability = player.Abilities != null ? player.Abilities.Find(id) : null;
                if (ability is ThrowAbility) Sfx.At(SfxKind.Toss, chest, 0.8f);
                else if (ability is TrapAbility) Sfx.At(SfxKind.Place, feet, 0.8f);
                break;
        }
    }

    // ─── Guns ─────────────────────────────────────────────────────────────────

    /// <summary>A gun firing: <paramref name="local"/> = the shooter's own screen (flat, louder).</summary>
    public static void Shot(string gunId, Vector3 muzzle, bool local)
    {
        SfxKind kind = gunId switch
        {
            "AR"      => SfxKind.Rifle,
            "Shotgun" => SfxKind.Shotgun,
            "Sniper"  => SfxKind.Sniper,
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
