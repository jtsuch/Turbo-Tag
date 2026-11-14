using System.Collections.Generic;
using UnityEngine;

/// <summary>Where a gun goes in the loadout: 1 knife, 2 secondary (pistols), 3 primary.</summary>
public enum GunSlot { Knife, Secondary, Primary }

/// <summary>One gun class: how it shoots. Read by PlayerGun, the shop and the killcam.</summary>
public sealed class GunInfo
{
    public string Id;
    public string Name;
    public string Description;
    public GunSlot Slot = GunSlot.Primary;
    public int     Price;                    // Credits in modes with an economy (Search & Destroy); free elsewhere
    public bool    Melee;                    // The knife: a swing at whoever is in front, no ammo
    public float   BackstabDamage;           // Melee: damage from behind

    public float Damage;                     // Per bullet (per pellet for shotguns), before the Damage rule
    public float HeadshotMultiplier = 2f;
    public float FireInterval;               // Seconds between shots
    public bool  Automatic;                  // Hold to keep firing
    public int   Magazine;
    public float ReloadTime;                 // Seconds
    public float Range = 150f;               // Metres
    public float Spread;                     // Degrees from the crosshair on the first shot (random cone; 0 = perfect)
    public float AirSpread;                  // Extra degrees while airborne (only with the Movement Inaccuracy rule)
    public float Bloom;                      // Extra degrees added per shot while spraying
    public float MaxBloom;                   // Most extra degrees spraying can add
    public float BloomRecovery = 8f;         // Degrees per second the bloom shrinks once you stop
    public float BloomDelay;                 // Seconds without firing before the bloom starts shrinking (0: always)
    public float ScopeZoom = 1f;             // Mouse wheel up: look through a scope this many times closer (1: no scope)
    public float Recoil;                     // Degrees the view kicks up per shot
    public int   Pellets = 1;                // Bullets per shot (shotguns)

    // Model
    public string  Model;                    // Resources/Guns prefab (GunPrefabBuilder); null = the player's built-in pistol
    public Vector3 ViewOffset = new(0.42f, -0.36f, 0.62f); // First-person position under the camera (m)
    public float   ViewLength  = 1.2f;       // First-person length (m)
    public float   WorldLength = 1.4f;       // Third-person length in the hand (m)

    /// <summary>Short stat line for the shop.</summary>
    public string Stats => Melee
        ? $"{Damage:0} front · {BackstabDamage:0} back · {FireInterval:0.#} s a swing"
        : $"{Damage:0}{(Pellets > 1 ? $"×{Pellets}" : "")} dmg · {60f / FireInterval:0} rpm · {Magazine} rounds · {(Automatic ? "auto" : "semi-auto")}";
}

/// <summary>
/// Every gun: the knife (always carried), secondaries (the Pebble is free and always carried) and primaries, bought in
/// the shop (Search &amp; Destroy) or picked free (other gun modes). Ids are stable (saved picks, the kill feed); the
/// names are the game's own (Justin: "creative names that aren't real guns"). Prices are first guesses (Valorant-like).
/// Balance (Nov 13, Justin: "similar to Valorant when comparing them to each other, but a much higher time to kill"):
/// against 100 health, body-shot kill times are about 4× Valorant's: rifles ~0.9–1 s (10 hits), SMG ~1.05 s, the
/// semi-auto rifle 0.9 s but a 2-tap to the head, pistols 1.25–1.5 s; no gun one-shots a head except the knife's
/// backstab. Damage per second climbs with price within each slot; pistols stay well under every primary.
/// </summary>
public static class GunCatalog
{
    public const string DefaultId = "Pistol";   // The Pebble: every player's free secondary
    public const string KnifeId   = "Knife";

    public static readonly IReadOnlyList<GunInfo> All = new[]
    {
        new GunInfo
        {
            Id = KnifeId, Name = "Icepick", Model = "Guns/Knife", Slot = GunSlot.Knife, Melee = true,
            Description = "Always with you. A swing hits whoever is right in front: 50, or 100 from behind.",
            Damage = 50f, BackstabDamage = 100f, FireInterval = 1f, Magazine = 1, Range = 2.6f,
            ViewOffset = new Vector3(0.34f, -0.32f, 0.55f), ViewLength = 0.5f, WorldLength = 0.55f,
        },
        new GunInfo
        {
            Id = "Pistol", Name = "Pebble", Slot = GunSlot.Secondary,
            Description = "The free sidearm. Accurate semi-auto: eight body shots or four headshots.",
            Damage = 14f, HeadshotMultiplier = 2.25f, FireInterval = 0.18f, Automatic = false,
            Magazine = 12, ReloadTime = 1.3f, Range = 150f, Spread = 0f, AirSpread = 1.5f, Recoil = 1.1f,
            Bloom = 0.4f, MaxBloom = 2.5f,
        },
        new GunInfo
        {
            Id = "AR", Name = "Blizzard", Model = "Guns/AR", Price = 2900,
            // Long sprays drift: the bloom only shrinks once you let go (BloomDelay), up to 3° after ~2 s
            Description = "Full-auto rifle. Ten body shots, five to the head; long sprays drift.",
            Damage = 11f, HeadshotMultiplier = 2f, FireInterval = 0.11f, Automatic = true,
            Magazine = 30, ReloadTime = 2f, Range = 200f, Spread = 0f, AirSpread = 2.5f, Recoil = 0.45f,
            Bloom = 0.16f, MaxBloom = 3f, BloomRecovery = 10f, BloomDelay = 0.15f,
            ViewOffset = new Vector3(0.38f, -0.36f, 0.75f), ViewLength = 1.25f, WorldLength = 1.75f,
        },
        new GunInfo
        {
            // Nine pellets: two blasts up close (81 if they all land), useless past ~45 m
            Id = "Shotgun", Name = "Slush", Model = "Guns/Shotgun", Price = 1800,
            Description = "Nine pellets per blast. Two blasts up close, nothing at range.",
            Damage = 9f, HeadshotMultiplier = 1.5f, FireInterval = 0.85f, Automatic = false, Pellets = 9,
            Magazine = 6, ReloadTime = 2.4f, Range = 45f, Spread = 5f, AirSpread = 2f, Recoil = 3.5f,
            Bloom = 0.5f, MaxBloom = 1.5f,
            ViewOffset = new Vector3(0.36f, -0.37f, 0.52f), ViewLength = 1.25f, WorldLength = 1.6f,
        },
        new GunInfo
        {
            // 80 to the body (two hits), exactly 100 to the head (one); slow and shaky in the air
            Id = "Sniper", Name = "Icicle", Model = "Guns/Sniper", Price = 4700,
            Description = "About 90 to the head, 55 to the body: never a one-shot kill. Slow, dead accurate, scroll up to scope.",
            Damage = 55f, HeadshotMultiplier = 1.65f, FireInterval = 1.6f, Automatic = false,
            Magazine = 5, ReloadTime = 2.6f, Range = 400f, Spread = 0f, AirSpread = 4f, Recoil = 5f, ScopeZoom = 4f,
            ViewOffset = new Vector3(0.36f, -0.36f, 0.85f), ViewLength = 1.55f, WorldLength = 2.1f,
        },
        new GunInfo
        {
            // Full-auto pistol: fast and cheap, but it sprays wide
            Id = "AutoPistol", Name = "Flurry", Model = "Guns/AutoPistol", Slot = GunSlot.Secondary, Price = 450,
            Description = "A full-auto pistol. Shreds up close; the spray opens up fast.",
            Damage = 7f, HeadshotMultiplier = 1.75f, FireInterval = 0.09f, Automatic = true,
            Magazine = 18, ReloadTime = 1.5f, Range = 120f, Spread = 1.2f, AirSpread = 2.5f, Recoil = 0.6f,
            Bloom = 0.6f, MaxBloom = 5f, BloomRecovery = 12f, BloomDelay = 0.1f,
            ViewOffset = new Vector3(0.34f, -0.32f, 0.58f), ViewLength = 0.6f, WorldLength = 0.75f,
        },
        new GunInfo
        {
            // The hand cannon: 30 to the body, 75 to the head (a head and any body shot)
            Id = "DesertEagle", Name = "Tusk", Model = "Guns/DesertEagle", Slot = GunSlot.Secondary, Price = 800,
            Description = "Six heavy rounds. Slow and kicks hard: a headshot and one more hit kills.",
            Damage = 30f, HeadshotMultiplier = 2.5f, FireInterval = 0.5f, Automatic = false,
            Magazine = 6, ReloadTime = 2.2f, Range = 180f, Spread = 0f, AirSpread = 2f, Recoil = 3.2f,
            Bloom = 1.2f, MaxBloom = 3f,
            ViewOffset = new Vector3(0.34f, -0.32f, 0.6f), ViewLength = 0.65f, WorldLength = 0.8f,
        },
        new GunInfo
        {
            Id = "SMG", Name = "Squall", Model = "Guns/SMG", Price = 1600,
            Description = "Fast and light. Less damage and more spread than a rifle; great on the move.",
            Damage = 7f, HeadshotMultiplier = 1.75f, FireInterval = 0.075f, Automatic = true,
            Magazine = 30, ReloadTime = 1.6f, Range = 140f, Spread = 0.5f, AirSpread = 2f, Recoil = 0.35f,
            Bloom = 0.22f, MaxBloom = 4f, BloomRecovery = 10f, BloomDelay = 0.12f,
            ViewOffset = new Vector3(0.36f, -0.34f, 0.65f), ViewLength = 0.95f, WorldLength = 1.25f,
        },
        new GunInfo
        {
            // The semi-auto rifle: hits harder per shot than the Blizzard, slower, for long sightlines
            Id = "LongRifle", Name = "Glacier", Model = "Guns/LongRifle", Price = 2250,
            Description = "Semi-auto rifle. Four body shots or two headshots; tight at range.",
            Damage = 26f, HeadshotMultiplier = 2f, FireInterval = 0.3f, Automatic = false,
            Magazine = 20, ReloadTime = 2.2f, Range = 250f, Spread = 0f, AirSpread = 2.5f, Recoil = 1.2f,
            Bloom = 0.3f, MaxBloom = 1.5f,
            ViewOffset = new Vector3(0.37f, -0.36f, 0.8f), ViewLength = 1.4f, WorldLength = 1.9f,
        },
        new GunInfo
        {
            Id = "LMG", Name = "Avalanche", Model = "Guns/LMG", Price = 3200,
            Description = "Sixty rounds of suppression. Heavy, and a long reload.",
            Damage = 9f, HeadshotMultiplier = 1.75f, FireInterval = 0.085f, Automatic = true,
            Magazine = 60, ReloadTime = 3.5f, Range = 200f, Spread = 0.3f, AirSpread = 3f, Recoil = 0.5f,
            Bloom = 0.18f, MaxBloom = 3.5f, BloomRecovery = 9f, BloomDelay = 0.15f,
            ViewOffset = new Vector3(0.38f, -0.38f, 0.75f), ViewLength = 1.35f, WorldLength = 1.9f,
        },
    };

    /// <summary>The guns for one loadout slot, cheapest first.</summary>
    public static List<GunInfo> InSlot(GunSlot slot)
    {
        var list = new List<GunInfo>();
        foreach (var gun in All) if (gun.Slot == slot) list.Add(gun);
        list.Sort((a, b) => a.Price.CompareTo(b.Price));
        return list;
    }

    public static GunInfo Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var gun in All)
            if (gun.Id == id) return gun;
        return null;
    }

    public static int IndexOf(string id)
    {
        for (int i = 0; i < All.Count; i++)
            if (All[i].Id == id) return i;
        return -1;
    }
}
