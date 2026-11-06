using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The player's crosshair options (Settings → Gameplay), saved in PlayerPrefs: style, colour, size and outline.
/// <see cref="Changed"/> fires when any of them changes.
/// </summary>
public static class CrosshairSettings
{
    public static readonly string[] Styles = { "Cross + Dot", "Cross", "Dot", "Circle", "Circle + Dot", "T", "None" };
    public static readonly string[] ColorNames = { "White", "Yellow", "Green", "Cyan", "Pink", "Red" };
    public static readonly Color[] Colors =
    {
        Color.white, new(1f, 0.85f, 0.2f), new(0.35f, 1f, 0.4f), new(0.2f, 0.9f, 1f), new(1f, 0.4f, 0.75f), new(1f, 0.3f, 0.3f),
    };

    private const string StyleKey = "Crosshair_Style", ColorKey = "Crosshair_Color", SizeKey = "Crosshair_Size", OutlineKey = "Crosshair_Outline";

    public static event Action Changed;

    public static int Style
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(StyleKey, 0), 0, Styles.Length - 1);
        set { PlayerPrefs.SetInt(StyleKey, value); Changed?.Invoke(); }
    }

    public static int ColorIndex
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(ColorKey, 0), 0, Colors.Length - 1);
        set { PlayerPrefs.SetInt(ColorKey, value); Changed?.Invoke(); }
    }

    /// <summary>Scale, 0.5 to 2.</summary>
    public static float Size
    {
        get => Mathf.Clamp(PlayerPrefs.GetFloat(SizeKey, 1f), 0.5f, 2f);
        set { PlayerPrefs.SetFloat(SizeKey, value); Changed?.Invoke(); }
    }

    public static bool Outline
    {
        get => PlayerPrefs.GetInt(OutlineKey, 1) == 1;
        set { PlayerPrefs.SetInt(OutlineKey, value ? 1 : 0); Changed?.Invoke(); }
    }
}

/// <summary>
/// The HUD crosshair, drawn from <see cref="CrosshairSettings"/> and rebuilt whenever they change.
/// Created by HUDManager in the middle of the screen.
/// </summary>
public class Crosshair : MonoBehaviour
{
    public static Crosshair Create(Transform parent)
    {
        var rect = UIKit.Place(UIKit.Rect("Crosshair", parent), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(60f, 60f));
        rect.pivot = new Vector2(0.5f, 0.5f);
        var cross = rect.gameObject.AddComponent<Crosshair>();
        cross.Rebuild();
        return cross;
    }

    private void OnEnable() => CrosshairSettings.Changed += Rebuild;
    private void OnDisable() => CrosshairSettings.Changed -= Rebuild;

    private void Rebuild()
    {
        for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
        Color color = CrosshairSettings.Colors[CrosshairSettings.ColorIndex];
        float s = CrosshairSettings.Size;
        bool outline = CrosshairSettings.Outline;
        string style = CrosshairSettings.Styles[CrosshairSettings.Style];

        bool dot = style.Contains("Dot"), cross = style.StartsWith("Cross"), circle = style.StartsWith("Circle"), t = style == "T";
        if (dot) Mark(UISprites.Circle, Vector2.zero, new Vector2(6f, 6f) * s, color, outline);
        if (cross || t)
            foreach (var dir in t ? new[] { Vector2.down, Vector2.left, Vector2.right } : new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right })
                Mark(null, dir * 13f * s, (dir.x != 0f ? new Vector2(10f, 3f) : new Vector2(3f, 10f)) * s, color, outline);
        if (circle) Mark(UISprites.Ring, Vector2.zero, new Vector2(26f, 26f) * s, color, outline);
    }

    private void Mark(Sprite sprite, Vector2 pos, Vector2 size, Color color, bool outline)
    {
        var img = UIKit.Rect("Mark", transform).gameObject.AddComponent<Image>();
        if (sprite != null) img.sprite = sprite; else UIKit.Round(img, 1.5f);
        img.color = color;
        img.raycastTarget = false;
        img.rectTransform.anchoredPosition = pos;
        img.rectTransform.sizeDelta = size;
        if (!outline) return;
        var shadow = img.gameObject.AddComponent<Outline>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);
    }
}
