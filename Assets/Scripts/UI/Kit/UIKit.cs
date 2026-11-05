using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Builds menu UI in code with one consistent look (<see cref="UITheme"/>): canvases that fit any screen,
/// panels, text, chunky buttons, switches, sliders, option selectors, number steppers, text fields, scroll
/// lists and modals. Screens (MainMenuScreen, LobbyScreen…) use only these, so changing a builder restyles
/// every menu.
///
/// Fitting the screen: canvases scale from a 1920×1080 layout with "Expand", so the whole layout is always
/// visible, from 16:10 (1280×800, Steam Deck) through 16:9 (1080p, 1440p, 4K) to 21:9 and 32:9 ultrawides:
/// the extra width or height goes to edge-anchored areas and the background. The Menu Scale setting zooms it.
/// </summary>
public static class UIKit
{
    public enum Style { Primary, Secondary, Ghost, Danger, Accent }
    public enum Font  { Display, Body, Bold }

    public static UITheme Theme => UITheme.Current;

    // ─── Canvas ───────────────────────────────────────────────────────────────

    /// <summary>A screen-space canvas that always fits the 1920×1080 layout on screen, with an EventSystem.</summary>
    public static Canvas Canvas(string name, int sortingOrder = 0)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var canvas = go.AddComponent<UnityEngine.Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        canvas.pixelPerfect = false;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.Expand;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        go.AddComponent<UIScaler>();
        go.AddComponent<GraphicRaycaster>();
        EnsureEventSystem();
        return canvas;
    }

    public static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null) return;
        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        UnityEngine.Object.DontDestroyOnLoad(es);
    }

    /// <summary>The animated party background, filling <paramref name="parent"/>.</summary>
    public static UIBackground Background(Transform parent)
    {
        var rect = Rect("Background", parent);
        Stretch(rect);
        var bg = rect.gameObject.AddComponent<UIBackground>();
        bg.Build();
        return bg;
    }

    // ─── Rects ────────────────────────────────────────────────────────────────

    public static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    /// <summary>Fills the parent, inset by the given margins.</summary>
    public static RectTransform Stretch(RectTransform rect, float left = 0f, float right = 0f, float top = 0f, float bottom = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot     = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
        return rect;
    }

    /// <summary>Pins to one anchor point (0–1 on each axis) with a fixed size; the pivot matches the anchor.</summary>
    public static RectTransform Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    public static LayoutElement Size(Component c, float width = -1f, float height = -1f, float flexWidth = -1f, float flexHeight = -1f)
    {
        var le = c.GetComponent<LayoutElement>();
        if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
        if (width  >= 0f) { le.preferredWidth  = width;  le.minWidth  = width; }
        if (height >= 0f) { le.preferredHeight = height; le.minHeight = height; }
        if (flexWidth  >= 0f) le.flexibleWidth  = flexWidth;
        if (flexHeight >= 0f) le.flexibleHeight = flexHeight;
        return le;
    }

    public static VerticalLayoutGroup VStack(Transform target, float spacing, RectOffset padding = null, TextAnchor align = TextAnchor.UpperLeft, bool expandWidth = true)
    {
        var v = target.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = spacing;
        v.padding = padding ?? new RectOffset();
        v.childAlignment = align;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = expandWidth;
        v.childForceExpandHeight = false;
        return v;
    }

    public static HorizontalLayoutGroup HStack(Transform target, float spacing, RectOffset padding = null, TextAnchor align = TextAnchor.MiddleLeft, bool expandHeight = true)
    {
        var h = target.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = spacing;
        h.padding = padding ?? new RectOffset();
        h.childAlignment = align;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = expandHeight;
        return h;
    }

    public static RectOffset Pad(int all) => new(all, all, all, all);
    public static RectOffset Pad(int horizontal, int vertical) => new(horizontal, horizontal, vertical, vertical);

    /// <summary>An empty flexible space inside a layout group.</summary>
    public static RectTransform Spacer(Transform parent, float size = -1f)
    {
        var r = Rect("Spacer", parent);
        if (size >= 0f) Size(r, size, size);
        else Size(r, flexWidth: 1f, flexHeight: 1f);
        return r;
    }

    // ─── Surfaces ─────────────────────────────────────────────────────────────

    /// <summary>A rounded panel. <paramref name="radius"/> in pixels.</summary>
    public static Image Panel(Transform parent, Color color, float radius = 24f, string name = "Panel")
    {
        var img = Rect(name, parent).gameObject.AddComponent<Image>();
        Round(img, radius);
        img.color = color;
        return img;
    }

    public static void Round(Image img, float radius)
    {
        img.sprite = UISprites.Rounded;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = UISprites.CornerRadius / Mathf.Max(1f, radius);
    }

    /// <summary>
    /// A raised card: a container with a soft drop shadow and a rounded background behind whatever is added to
    /// it later (both ignored by layout groups on the container). Returns the container.
    /// </summary>
    public static RectTransform Card(Transform parent, Color color, float radius = 24f, float shadow = 0.4f, string name = "Card")
    {
        var root = Rect(name, parent);
        if (shadow > 0f)
        {
            var s = Rect("Shadow", root).gameObject.AddComponent<Image>();
            s.sprite = UISprites.Shadow;
            s.type = Image.Type.Sliced;
            s.color = new Color(0f, 0f, 0f, shadow);
            s.raycastTarget = false;
            Stretch(s.rectTransform, -22f, -22f, -14f, -30f);
            s.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        }
        var bg = Panel(root, color, radius, "Bg");
        Stretch(bg.rectTransform);
        bg.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        return root;
    }

    /// <summary>The background image of a <see cref="Card"/>.</summary>
    public static Image CardBg(RectTransform card) => card.Find("Bg").GetComponent<Image>();

    // ─── Text ─────────────────────────────────────────────────────────────────

    public static TextMeshProUGUI Text(Transform parent, string text, float size, Font font = Font.Body,
        Color? color = null, TextAlignmentOptions align = TextAlignmentOptions.Left, string name = "Text")
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        var asset = FontAsset(font);
        if (asset != null) t.font = asset;
        t.text = text;
        t.fontSize = size;
        t.color = color ?? Theme.text;
        t.alignment = align;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }

    /// <summary>
    /// Single line that shrinks (down to 60%) to fit its width (names). Not "…": TMP's Ellipsis/Truncate modes hide
    /// the whole line when a layout gives it exactly its own height, which happened all over.
    /// </summary>
    public static TextMeshProUGUI OneLine(TextMeshProUGUI t)
    {
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        t.enableAutoSizing = true;
        t.fontSizeMax = t.fontSize;
        t.fontSizeMin = t.fontSize * 0.6f;
        return t;
    }

    public static TMP_FontAsset FontAsset(Font font) => font switch
    {
        Font.Display => Theme.display != null ? Theme.display : Theme.bodyBold,
        Font.Bold    => Theme.bodyBold != null ? Theme.bodyBold : Theme.body,
        _            => Theme.body,
    };

    /// <summary>Big screen title with a soft shadow.</summary>
    public static TextMeshProUGUI Title(Transform parent, string text, float size = 72f, TextAlignmentOptions align = TextAlignmentOptions.Left)
    {
        var t = Text(parent, text, size, Font.Display, Theme.text, align, "Title");
        t.textWrappingMode = TextWrappingModes.NoWrap;
        var shadow = t.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
        shadow.effectDistance = new Vector2(0f, -5f);
        return t;
    }

    // ─── Buttons ──────────────────────────────────────────────────────────────

    public static Color StyleColor(Style style) => style switch
    {
        Style.Primary   => Theme.primary,
        Style.Secondary => Theme.secondary,
        Style.Danger    => Theme.danger,
        Style.Accent    => Theme.cyan,
        _               => Theme.card,
    };

    /// <summary>A chunky rounded button: a coloured face on a darker lip that it presses down onto.</summary>
    public static Button Button(Transform parent, string label, Style style, UnityAction onClick,
        float height = 72f, float fontSize = 32f, string name = null)
    {
        Color face = StyleColor(style);
        Color ink  = style == Style.Primary || style == Style.Accent ? Theme.textDark : Theme.text;

        var root = Rect(name ?? label + " Button", parent);
        var lip  = root.gameObject.AddComponent<Image>();
        Round(lip, 20f);
        Size(root, height: height);

        var faceImg = Panel(root, face, 20f, "Face");
        Stretch(faceImg.rectTransform, 0f, 0f, 0f, 6f);
        faceImg.raycastTarget = false;

        var text = Text(faceImg.transform, label, fontSize, Font.Display, ink, TextAlignmentOptions.Center, "Label");
        Stretch(text.rectTransform, 16f, 16f, 0f, 0f);
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.enableAutoSizing = true;
        text.fontSizeMax = fontSize;
        text.fontSizeMin = fontSize * 0.6f;

        var button = root.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = lip;
        if (onClick != null) button.onClick.AddListener(onClick);

        var fx = root.gameObject.AddComponent<UIButtonFx>();
        fx.Face = faceImg;
        fx.Lip = lip;
        fx.FaceColor = face;
        fx.ClickSound = style == Style.Primary ? UISound.Kind.Confirm : UISound.Kind.Click;
        return button;
    }

    /// <summary>The label of a kit button.</summary>
    public static TMP_Text Label(Button button) => button.GetComponentInChildren<TMP_Text>(true);

    /// <summary>A round icon-ish button with a short text glyph (×, ‹, +, −).</summary>
    public static Button RoundButton(Transform parent, string glyph, UnityAction onClick, float size = 56f, Style style = Style.Ghost)
    {
        var b = Button(parent, glyph, style, onClick, size, size * 0.55f, glyph + " Button");
        Size(b, size, size);
        foreach (var img in b.GetComponentsInChildren<Image>(true)) if (img.name != "Shadow") Round(img, size * 0.5f);
        var label = Label(b);
        label.rectTransform.offsetMin = new Vector2(0f, 0f);
        label.rectTransform.offsetMax = new Vector2(0f, 0f);
        label.font = FontAsset(Font.Bold);
        return b;
    }

    /// <summary>Text that acts as a link (underline-free, brightens on hover).</summary>
    public static Button LinkButton(Transform parent, string label, UnityAction onClick, float fontSize = 22f)
    {
        var root = Rect(label, parent);
        var hit = root.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        var text = Text(root, label, fontSize, Font.Bold, Theme.cyan, TextAlignmentOptions.Center, "Label");
        Stretch(text.rectTransform);
        Size(root, height: fontSize + 14f);
        var button = root.gameObject.AddComponent<Button>();
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.4f, 1.4f, 1.4f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        colors.colorMultiplier = 1.5f;
        button.colors = colors;
        button.targetGraphic = text;
        button.onClick.AddListener(() => UISound.Play(UISound.Kind.Click));
        if (onClick != null) button.onClick.AddListener(onClick);
        return button;
    }

    // ─── Inputs ───────────────────────────────────────────────────────────────

    public static TMP_InputField InputField(Transform parent, string placeholder, bool password = false, float height = 64f)
    {
        var root = Panel(parent, Theme.field, 16f, placeholder + " Field");
        Size(root, height: height);
        var outline = root.gameObject.AddComponent<Outline>();
        outline.effectColor = Theme.outline;
        outline.effectDistance = new Vector2(2f, -2f);

        var area = Rect("Text Area", root.transform);
        Stretch(area, 20f, 20f, 8f, 8f);
        area.gameObject.AddComponent<RectMask2D>();

        var hint = Text(area, placeholder, 24f, Font.Body, new Color(1f, 1f, 1f, 0.35f), TextAlignmentOptions.Left, "Placeholder");
        Stretch(hint.rectTransform);
        hint.textWrappingMode = TextWrappingModes.NoWrap;
        hint.alignment = TextAlignmentOptions.MidlineLeft;
        var text = Text(area, "", 24f, Font.Bold, Theme.text, TextAlignmentOptions.Left, "Text");
        Stretch(text.rectTransform);
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.alignment = TextAlignmentOptions.MidlineLeft;

        var field = root.gameObject.AddComponent<TMP_InputField>();
        field.textViewport = area;
        field.textComponent = text;
        field.placeholder = hint;
        field.fontAsset = text.font;
        field.pointSize = 24f;
        field.caretColor = Theme.primary;
        field.customCaretColor = true;
        field.caretWidth = 3;
        field.selectionColor = new Color(Theme.secondary.r, Theme.secondary.g, Theme.secondary.b, 0.5f);
        field.contentType = password ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard;
        field.targetGraphic = root;
        var colors = field.colors;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.3f);
        colors.selectedColor = new Color(1.25f, 1.25f, 1.45f);
        colors.colorMultiplier = 1.3f;
        field.colors = colors;
        return field;
    }

    /// <summary>A pill switch (on/off) with a sliding knob.</summary>
    public static UISwitch Switch(Transform parent, bool on, UnityAction<bool> onChanged)
    {
        var root = Rect("Switch", parent);
        Size(root, 84f, 44f);
        var sw = root.gameObject.AddComponent<UISwitch>();
        sw.Build(on);
        if (onChanged != null) sw.Changed.AddListener(onChanged);
        return sw;
    }

    /// <summary>A slider with a rounded track, a coloured fill and a round knob.</summary>
    public static Slider Slider(Transform parent, float min, float max, float value, bool whole, UnityAction<float> onChanged)
    {
        var root = Rect("Slider", parent);
        Size(root, height: 44f, flexWidth: 1f);

        var track = Panel(root, Theme.field, 10f, "Track");
        Stretch(track.rectTransform, 0f, 0f, 14f, 14f);

        var fillArea = Rect("Fill Area", root);
        Stretch(fillArea, 0f, 0f, 14f, 14f);
        var fill = Panel(fillArea, Theme.primary, 10f, "Fill");
        fill.rectTransform.sizeDelta = Vector2.zero;

        var handleArea = Rect("Handle Area", root);
        Stretch(handleArea, 16f, 16f, 0f, 0f);
        var handle = Rect("Handle", handleArea).gameObject.AddComponent<Image>();
        handle.sprite = UISprites.Circle;
        handle.color = Color.white;
        handle.rectTransform.sizeDelta = new Vector2(36f, 0f);
        var shadow = handle.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.4f);
        shadow.effectDistance = new Vector2(0f, -3f);

        var slider = root.gameObject.AddComponent<Slider>();
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = whole;
        slider.SetValueWithoutNotify(value);
        var colors = slider.colors;
        colors.highlightedColor = new Color(1f, 0.95f, 0.8f);
        colors.pressedColor = Theme.primary;
        slider.colors = colors;
        if (onChanged != null) slider.onValueChanged.AddListener(onChanged);
        return slider;
    }

    /// <summary>‹ Option › : cycles through choices (a lighter, prettier dropdown).</summary>
    public static UISelector Selector(Transform parent, string[] options, int index, UnityAction<int> onChanged, float width = 360f)
    {
        var root = Rect("Selector", parent);
        Size(root, width, 56f);
        var sel = root.gameObject.AddComponent<UISelector>();
        sel.Build(options, index);
        if (onChanged != null) sel.Changed.AddListener(onChanged);
        return sel;
    }

    /// <summary>− value + : a number with buttons and a typeable field.</summary>
    public static UIStepper Stepper(Transform parent, float value, float step, Func<float, string> format,
        Func<string, float?> parse, UnityAction<float> onChanged, float width = 260f)
    {
        var root = Rect("Stepper", parent);
        Size(root, width, 56f);
        var st = root.gameObject.AddComponent<UIStepper>();
        st.Build(value, step, format, parse);
        if (onChanged != null) st.Changed.AddListener(onChanged);
        return st;
    }

    // ─── Lists ────────────────────────────────────────────────────────────────

    /// <summary>A vertical scrolling list filling <paramref name="parent"/>; add rows to the returned content.</summary>
    public static RectTransform ScrollList(Transform parent, float spacing = 12f, RectOffset padding = null, string name = "List")
    {
        var root = Rect(name, parent);
        Stretch(root);
        var scroll = root.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 45f;
        scroll.inertia = true;
        scroll.decelerationRate = 0.12f;

        var viewport = Rect("Viewport", root);
        Stretch(viewport, 0f, 18f, 0f, 0f);
        viewport.gameObject.AddComponent<RectMask2D>();
        var hit = viewport.gameObject.AddComponent<Image>();
        hit.color = Color.clear; // Lets the wheel scroll over gaps

        var content = Rect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        VStack(content, spacing, padding ?? Pad(4));
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Slim rounded scrollbar
        var bar = Rect("Scrollbar", root);
        bar.anchorMin = new Vector2(1f, 0f);
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(1f, 0.5f);
        bar.sizeDelta = new Vector2(8f, 0f);
        var barBg = bar.gameObject.AddComponent<Image>();
        Round(barBg, 4f);
        barBg.color = new Color(1f, 1f, 1f, 0.06f);
        var slide = Rect("Sliding Area", bar);
        Stretch(slide);
        var handle = Panel(slide, new Color(1f, 1f, 1f, 0.35f), 4f, "Handle");
        handle.rectTransform.sizeDelta = Vector2.zero;
        var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.handleRect = handle.rectTransform;
        scrollbar.targetGraphic = handle;

        scroll.viewport = viewport;
        scroll.content = content;
        scroll.verticalScrollbar = scrollbar;
        // AutoHide (not ...AndExpandViewport): resizing the viewport when the bar appears re-wraps text, which
        // changes the content height, which toggles the bar again; that loop left lists scrolled and shifted
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        return content;
    }

    // ─── Composite rows ───────────────────────────────────────────────────────

    /// <summary>A settings row: label (and optional hint) on the left, <paramref name="control"/> on the right.</summary>
    public static RectTransform Row(Transform parent, string label, string hint = null, float height = 72f)
    {
        var row = Panel(parent, Theme.card, 16f, label + " Row").rectTransform;
        Size(row, height: height);
        HStack(row, 16f, Pad(24, 8), TextAnchor.MiddleLeft, expandHeight: false);

        var texts = Rect("Labels", row);
        VStack(texts, 0f, null, TextAnchor.MiddleLeft);
        Size(texts, flexWidth: 1f);
        var title = Text(texts, label, 26f, Font.Bold);
        title.textWrappingMode = TextWrappingModes.NoWrap;
        if (!string.IsNullOrEmpty(hint))
        {
            var h = Text(texts, hint, 18f, Font.Body, Theme.textMuted);
            h.textWrappingMode = TextWrappingModes.NoWrap;
        }
        return row;
    }

    /// <summary>A small coloured pill with text (tags such as "HOST", "YOU", "FULL").</summary>
    public static Image Chip(Transform parent, string text, Color color, Color? ink = null, float fontSize = 16f)
    {
        var chip = Panel(parent, color, 12f, text + " Chip");
        var h = HStack(chip.transform, 0f, Pad(12, 4), TextAnchor.MiddleCenter);
        h.childControlWidth = true;
        var t = Text(chip.transform, text, fontSize, Font.Bold, ink ?? Theme.textDark, TextAlignmentOptions.Center);
        t.textWrappingMode = TextWrappingModes.NoWrap;
        chip.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        return chip;
    }

    // ─── Modals ───────────────────────────────────────────────────────────────

    /// <summary>A dimmed overlay with a centred window that pops in; Escape and the × close it.</summary>
    public static UIModal Modal(Transform canvas, string title, Vector2 size, bool closable = true)
    {
        var root = Rect(title + " Modal", canvas);
        Stretch(root);
        var modal = root.gameObject.AddComponent<UIModal>();
        modal.Build(title, size, closable);
        return modal;
    }

    /// <summary>A check mark drawn with two rounded bars, centred in <paramref name="parent"/>.</summary>
    public static RectTransform CheckMark(Transform parent, Color color, float size)
    {
        var root = Rect("Check Mark", parent);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
        root.sizeDelta = new Vector2(size, size);
        float thick = size * 0.16f;
        var shortBar = Panel(root, color, thick * 0.5f, "Short");
        shortBar.raycastTarget = false;
        shortBar.rectTransform.sizeDelta = new Vector2(size * 0.42f, thick);
        shortBar.rectTransform.anchoredPosition = new Vector2(-size * 0.2f, -size * 0.04f);
        shortBar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -45f);
        var longBar = Panel(root, color, thick * 0.5f, "Long");
        longBar.raycastTarget = false;
        longBar.rectTransform.sizeDelta = new Vector2(size * 0.72f, thick);
        longBar.rectTransform.anchoredPosition = new Vector2(size * 0.1f, size * 0.06f);
        longBar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 48f);
        return root;
    }

    /// <summary>
    /// Lays out <paramref name="root"/> and everything under it now (twice, so content-sized lists settle).
    /// Call after building or changing lists: Unity's automatic pass doesn't reliably settle nested scroll lists.
    /// </summary>
    public static void Relayout(RectTransform root)
    {
        if (root == null) return;
        UnityEngine.Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(root);
        LayoutRebuilder.ForceRebuildLayoutImmediate(root);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Sets up arrow-key/controller navigation between buttons in a vertical list.</summary>
    public static void ChainVertical(params Selectable[] items)
    {
        for (int i = 0; i < items.Length; i++)
        {
            var nav = new Navigation { mode = Navigation.Mode.Explicit };
            nav.selectOnUp   = items[(i - 1 + items.Length) % items.Length];
            nav.selectOnDown = items[(i + 1) % items.Length];
            items[i].navigation = nav;
        }
    }

    public static Color WithAlpha(Color c, float a) { c.a = a; return c; }
}
