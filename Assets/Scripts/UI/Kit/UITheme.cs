using TMPro;
using UnityEngine;

/// <summary>
/// The look of every menu built with <see cref="UIKit"/>: fonts, logo and palette. One asset at
/// Resources/UI/UITheme (made by the "BuildUITheme" editor job); tweak colours there and every screen follows.
/// </summary>
[CreateAssetMenu(menuName = "TurboTag/UI Theme")]
public class UITheme : ScriptableObject
{
    private static UITheme current;

    /// <summary>The project's theme (a default one with no fonts if the asset is missing).</summary>
    public static UITheme Current
    {
        get
        {
            if (current != null) return current;
            current = Resources.Load<UITheme>("UI/UITheme");
            if (current == null) current = CreateInstance<UITheme>();
            return current;
        }
    }

    [Header("Fonts")]
    [Tooltip("Headings and buttons: chunky and playful.")]
    public TMP_FontAsset display;
    [Tooltip("Body text and labels.")]
    public TMP_FontAsset body;
    [Tooltip("Emphasis, values and small headings.")]
    public TMP_FontAsset bodyBold;

    [Header("Art")]
    public Texture2D logo;

    [Header("Background")]
    public Color backgroundTop    = Hex("1A1145");
    public Color backgroundBottom = Hex("3A1263");

    [Header("Surfaces")]
    public Color panel       = Hex("221A4DF0");
    public Color card        = Hex("2E2562");
    public Color cardHover   = Hex("3A3078");
    public Color field       = Hex("150F33");
    public Color outline     = Hex("FFFFFF1A");
    public Color dim         = Hex("0A0620C8");

    [Header("Text")]
    public Color text        = Hex("FFFFFF");
    public Color textMuted   = Hex("B9B2E0");
    public Color textDark    = Hex("1A1145");

    [Header("Accents")]
    public Color primary     = Hex("FFC93C"); // Main call to action (Play)
    public Color secondary   = Hex("8B5CF6");
    public Color pink        = Hex("FF3D7F");
    public Color cyan        = Hex("22D3EE");
    public Color lime        = Hex("7CE85A");
    public Color orange      = Hex("FF8A3D");
    public Color danger      = Hex("FF4D6D");

    public static Color Hex(string hex) =>
        ColorUtility.TryParseHtmlString("#" + hex, out Color c) ? c : Color.magenta;

    /// <summary>The party colours, for confetti, avatars and per-category accents.</summary>
    public Color[] Party => new[] { pink, cyan, primary, lime, orange, secondary };
}
