using System.Collections.Generic;
using UnityEngine;

/// <summary>One gun class: how it shoots. Read by PlayerGun, the gun picker and the killcam.</summary>
public sealed class GunInfo
{
    public string Id;
    public string Name;
    public string Description;

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
    public float Recoil;                     // Degrees the view kicks up per shot
    public int   Pellets = 1;                // Bullets per shot (shotguns)

    // Model
    public string  Model;                    // Resources/Guns prefab (GunPrefabBuilder); null = the player's built-in pistol
    public Vector3 ViewOffset = new(0.42f, -0.36f, 0.62f); // First-person position under the camera (m)
    public float   ViewLength  = 1.2f;       // First-person length (m)
    public float   WorldLength = 1.4f;       // Third-person length in the hand (m)

    /// <summary>Short stat line for the gun picker.</summary>
    public string Stats =>
        $"{Damage:0}{(Pellets > 1 ? $"×{Pellets}" : "")} dmg · {60f / FireInterval:0} rpm · {Magazine} rounds · {(Automatic ? "auto" : "semi-auto")}";
}

/// <summary>
/// Every gun the picker offers. One primary per life, no secondaries. Planned: LMG (big magazine, slow reload).
/// </summary>
public static class GunCatalog
{
    public const string DefaultId = "Pistol";

    public static readonly IReadOnlyList<GunInfo> All = new[]
    {
        new GunInfo
        {
            Id = "Pistol", Name = "Pistol",
            Description = "Reliable semi-auto. Five body shots or three headshots.",
            Damage = 20f, HeadshotMultiplier = 2f, FireInterval = 0.2f, Automatic = false,
            Magazine = 12, ReloadTime = 1.4f, Range = 150f, Spread = 0f, AirSpread = 1.5f, Recoil = 1.2f,
            Bloom = 0.4f, MaxBloom = 2.5f,
        },
        new GunInfo
        {
            Id = "AR", Name = "Assault Rifle", Model = "Guns/AR",
            Description = "Full-auto. Hold to fire; controllable recoil, a long reload.",
            Damage = 14f, HeadshotMultiplier = 1.75f, FireInterval = 0.1f, Automatic = true,
            Magazine = 30, ReloadTime = 2f, Range = 200f, Spread = 0f, AirSpread = 2.5f, Recoil = 0.45f,
            Bloom = 0.25f, MaxBloom = 3.5f, BloomRecovery = 10f,
            ViewOffset = new Vector3(0.38f, -0.36f, 0.75f), ViewLength = 1.25f, WorldLength = 1.75f,
        },
        new GunInfo
        {
            // Nine pellets: a close-range one-shot to the body if they all land, useless past ~45 m
            Id = "Shotgun", Name = "Shotgun", Model = "Guns/Shotgun",
            Description = "Nine pellets per blast. Devastating up close, nothing at range.",
            Damage = 12f, HeadshotMultiplier = 1.5f, FireInterval = 0.85f, Automatic = false, Pellets = 9,
            Magazine = 6, ReloadTime = 2.4f, Range = 45f, Spread = 5f, AirSpread = 2f, Recoil = 3.5f,
            Bloom = 0.5f, MaxBloom = 1.5f,
            ViewOffset = new Vector3(0.36f, -0.37f, 0.52f), ViewLength = 1.25f, WorldLength = 1.6f,
        },
        new GunInfo
        {
            // 80 to the body (two hits), exactly 100 to the head (one); slow and shaky in the air
            Id = "Sniper", Name = "Sniper", Model = "Guns/Sniper",
            Description = "One shot to the head, two to the body. Slow, and dead accurate.",
            Damage = 80f, HeadshotMultiplier = 1.25f, FireInterval = 1.3f, Automatic = false,
            Magazine = 5, ReloadTime = 2.6f, Range = 400f, Spread = 0f, AirSpread = 4f, Recoil = 5f,
            ViewOffset = new Vector3(0.36f, -0.36f, 0.85f), ViewLength = 1.55f, WorldLength = 2.1f,
        },
    };

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
