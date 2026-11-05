using Photon.Pun;
using UnityEngine;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// Penguin colours. Each skin is a recoloured copy of the penguin colour map
/// (Resources/Player/Skins/Penguin-{Name}): the black body parts take the skin's colour; the belly, feet,
/// beak and the (transparent, so black in game) eyes stay. "Black" is the original map. A player's pick is the
/// <see cref="PlayerKeys.Skin"/> property (synced), remembered locally between games, and drawn by
/// PlayerAppearance. Chosen on the main menu player card or in the Lobby (MainMenuScreen, LobbyScreen).
/// </summary>
public static class PenguinSkins
{
    public readonly struct Skin
    {
        public readonly string Name;
        public readonly Color  Color;
        public Skin(string name, Color color) { Name = name; Color = color; }
    }

    public const string Default = "Black";
    private const string PrefsKey = "PenguinSkin";

    /// <summary>Every colour, in picker order. The colour is used for the skin and its swatch.</summary>
    public static readonly Skin[] All =
    {
        new("Black",  new Color(0.08f, 0.08f, 0.08f)),
        new("Red",    new Color(0.86f, 0.13f, 0.13f)),
        new("Orange", new Color(1.00f, 0.50f, 0.05f)),
        new("Yellow", new Color(1.00f, 0.84f, 0.10f)),
        new("Green",  new Color(0.16f, 0.70f, 0.22f)),
        new("Blue",   new Color(0.16f, 0.40f, 0.95f)),
        new("Purple", new Color(0.56f, 0.22f, 0.86f)),
        new("Pink",   new Color(1.00f, 0.33f, 0.97f)),
        new("Brown",  new Color(0.46f, 0.29f, 0.15f)),
    };

    /// <summary>The colour map for a skin, or null for the original ("Black", or anything unknown).</summary>
    public static Texture2D Texture(string name) =>
        string.IsNullOrEmpty(name) || name == Default ? null : Resources.Load<Texture2D>($"Player/Skins/Penguin-{name}");

    public static string Of(PhotonPlayer player) => NetProps.Of(player, PlayerKeys.Skin, Default);

    public static string Saved => PlayerPrefs.GetString(PrefsKey, Default);

    /// <summary>Local player: wears <paramref name="name"/> and remembers it for next time.</summary>
    public static void SetLocal(string name)
    {
        PlayerPrefs.SetString(PrefsKey, name);
        if (PhotonNetwork.InRoom) NetProps.SetLocal(PlayerKeys.Skin, name); // Otherwise EnsureLocal sends it on joining
    }

    /// <summary>
    /// Local player: puts the remembered skin on if they haven't picked one in this room. Does nothing until
    /// the room is joined (Photon rejects property writes while still "Joining"); call it again on joining.
    /// </summary>
    public static void EnsureLocal()
    {
        if (!PhotonNetwork.InRoom) return;
        var props = PhotonNetwork.LocalPlayer?.CustomProperties;
        if (props != null && !props.ContainsKey(PlayerKeys.Skin)) NetProps.SetLocal(PlayerKeys.Skin, Saved);
    }
}
