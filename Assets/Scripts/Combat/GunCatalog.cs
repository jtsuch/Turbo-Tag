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
    public float Spread;                     // Degrees from the crosshair (random cone)
    public float AirSpread;                  // Extra degrees while airborne
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
/// Every gun the picker offers. One primary per life, no secondaries. Planned classes (add an entry here
/// and they appear in the picker): AR (automatic, mid damage), LMG (big magazine, slow reload), Sniper
/// (one-shot headshots, slow), Shotgun (pellets, short range).
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
            Magazine = 12, ReloadTime = 1.4f, Range = 150f, Spread = 0.4f, AirSpread = 1.5f, Recoil = 1.2f,
        },
        new GunInfo
        {
            Id = "AR", Name = "Assault Rifle", Model = "Guns/AR",
            Description = "Full-auto. Hold to fire; controllable recoil, a long reload.",
            Damage = 14f, HeadshotMultiplier = 1.75f, FireInterval = 0.1f, Automatic = true,
            Magazine = 30, ReloadTime = 2f, Range = 200f, Spread = 0.8f, AirSpread = 2.5f, Recoil = 0.45f,
            ViewOffset = new Vector3(0.38f, -0.36f, 0.75f), ViewLength = 1.25f, WorldLength = 1.75f,
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
