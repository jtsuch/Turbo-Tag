using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Keeps a kit canvas sized to the Menu Scale setting (on top of fitting the screen).</summary>
[RequireComponent(typeof(CanvasScaler))]
public class UIScaler : MonoBehaviour
{
    private CanvasScaler scaler;

    private void Awake()
    {
        scaler = GetComponent<CanvasScaler>();
        Apply(SettingsManager.Instance != null ? SettingsManager.Instance.UiScale : 1f);
        SettingsManager.UiScaleChanged += Apply;
    }

    private void OnDestroy() => SettingsManager.UiScaleChanged -= Apply;

    // A bigger scale means a smaller reference layout, so everything is drawn larger
    private void Apply(float scale) => scaler.referenceResolution = new Vector2(1920f, 1080f) / Mathf.Clamp(scale, 0.7f, 1.4f);
}

/// <summary>Pill on/off switch with a sliding knob (built by <see cref="UIKit.Switch"/>).</summary>
public class UISwitch : Selectable, IPointerClickHandler, ISubmitHandler
{
    public class BoolEvent : UnityEvent<bool> { }
    public BoolEvent Changed { get; } = new();

    public bool IsOn { get; private set; }

    private Image track;
    private RectTransform knob;
    private float t;

    public void Build(bool on)
    {
        track = gameObject.AddComponent<Image>();
        UIKit.Round(track, 22f);
        targetGraphic = track;
        transition = Transition.None;

        var k = UIKit.Rect("Knob", transform).gameObject.AddComponent<Image>();
        k.sprite = UISprites.Circle;
        k.raycastTarget = false;
        var shadow = k.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
        shadow.effectDistance = new Vector2(0f, -2f);
        knob = k.rectTransform;
        knob.anchorMin = knob.anchorMax = new Vector2(0f, 0.5f);
        knob.pivot = new Vector2(0.5f, 0.5f);
        knob.sizeDelta = new Vector2(34f, 34f);

        IsOn = on;
        t = on ? 1f : 0f;
        Draw();
    }

    public void SetWithoutNotify(bool on) => IsOn = on;

    public void OnPointerClick(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) Flip(); }
    public void OnSubmit(BaseEventData e) => Flip();

    private void Flip()
    {
        if (!IsInteractable()) return;
        IsOn = !IsOn;
        UISound.Play(UISound.Kind.Click);
        Changed.Invoke(IsOn);
    }

    private void Update()
    {
        t = Mathf.MoveTowards(t, IsOn ? 1f : 0f, Time.unscaledDeltaTime * 8f);
        Draw();
    }

    private void Draw()
    {
        if (track == null) return;
        var theme = UITheme.Current;
        float e = t * t * (3f - 2f * t);
        Color c = Color.Lerp(theme.field, theme.lime, e);
        if (!IsInteractable()) c = Color.Lerp(c, Color.gray, 0.5f);
        track.color = c;
        knob.anchoredPosition = new Vector2(Mathf.Lerp(22f, 62f, e), 0f);
    }
}

/// <summary>‹ Option › selector: arrows (or a click on the value) cycle through options.</summary>
public class UISelector : MonoBehaviour
{
    public class IntEvent : UnityEvent<int> { }
    public IntEvent Changed { get; } = new();

    public int Index { get; private set; }
    public bool Interactable
    {
        get => interactable;
        set { interactable = value; if (left != null) { left.interactable = right.interactable = value; } }
    }

    private string[] options = new string[0];
    private TMP_Text value;
    private Button left, right;
    private bool interactable = true;

    public void Build(string[] choices, int index)
    {
        options = choices ?? new string[0];
        var bg = gameObject.AddComponent<Image>();
        UIKit.Round(bg, 16f);
        bg.color = UITheme.Current.field;
        UIKit.HStack(transform, 4f, UIKit.Pad(6), TextAnchor.MiddleCenter);

        left = UIKit.RoundButton(transform, "‹", () => Step(-1), 44f);
        value = UIKit.Text(transform, "", 24f, UIKit.Font.Bold, UITheme.Current.text, TextAlignmentOptions.Center, "Value");
        value.textWrappingMode = TextWrappingModes.NoWrap;
        value.enableAutoSizing = true;
        value.fontSizeMin = 16f;
        value.fontSizeMax = 24f;
        UIKit.Size(value, flexWidth: 1f);
        right = UIKit.RoundButton(transform, "›", () => Step(1), 44f);
        Set(index, notify: false);
    }

    public void SetOptions(string[] choices, int index)
    {
        options = choices ?? new string[0];
        Set(index, notify: false);
    }

    public void Set(int index, bool notify)
    {
        if (options.Length == 0) { Index = 0; value.text = "-"; return; }
        Index = (index % options.Length + options.Length) % options.Length;
        value.text = options[Index];
        if (notify) Changed.Invoke(Index);
    }

    private void Step(int dir)
    {
        if (!interactable) return;
        Set(Index + dir, notify: true);
    }
}

/// <summary>− [value] + number control; the value can also be typed. Format/parse are supplied by the owner.</summary>
public class UIStepper : MonoBehaviour
{
    public class FloatEvent : UnityEvent<float> { }
    public FloatEvent Changed { get; } = new();

    public float Value { get; private set; }

    private float step;
    private Func<float, string> format;
    private Func<string, float?> parse;
    private TMP_InputField field;
    private Button minus, plus;

    public bool Interactable
    {
        set { minus.interactable = plus.interactable = value; field.interactable = value; }
    }

    public void Build(float initial, float stepSize, Func<float, string> formatter, Func<string, float?> parser)
    {
        step = stepSize;
        format = formatter ?? (v => v.ToString("0.##"));
        parse = parser ?? (s => float.TryParse(s, out float f) ? f : null);

        var bg = gameObject.AddComponent<Image>();
        UIKit.Round(bg, 16f);
        bg.color = UITheme.Current.field;
        UIKit.HStack(transform, 4f, UIKit.Pad(6), TextAnchor.MiddleCenter);

        minus = UIKit.RoundButton(transform, "−", () => Commit(Value - step), 44f);
        field = UIKit.InputField(transform, "", height: 44f);
        UIKit.Size(field, flexWidth: 1f);
        field.textComponent.alignment = TextAlignmentOptions.Center;
        field.GetComponent<Image>().color = Color.clear;
        var outline = field.GetComponent<Outline>();
        if (outline != null) Destroy(outline);
        field.onEndEdit.AddListener(text =>
        {
            float? v = parse(text);
            if (v.HasValue) Commit(v.Value);
            else SetWithoutNotify(Value);
        });
        plus = UIKit.RoundButton(transform, "+", () => Commit(Value + step), 44f);
        SetWithoutNotify(initial);
    }

    public void SetWithoutNotify(float v)
    {
        Value = v;
        field.SetTextWithoutNotify(format(v));
    }

    private void Commit(float v)
    {
        SetWithoutNotify(v);
        Changed.Invoke(v); // The owner clamps and may set it back
    }
}

/// <summary>
/// A modal window over a dimmed screen: pops in, closes with ×, Escape or a click outside. Put content in
/// <see cref="Body"/>; <see cref="Footer"/> is a row for buttons.
/// </summary>
public class UIModal : MonoBehaviour
{
    public RectTransform Window { get; private set; }
    public RectTransform Body   { get; private set; }
    public RectTransform Footer { get; private set; }
    public TMP_Text      TitleText { get; private set; }
    public event Action  Closed;

    private CanvasGroup group;
    private bool closable, open;
    private float t;

    public bool IsOpen => open;

    public void Build(string title, Vector2 size, bool canClose)
    {
        closable = canClose;
        var theme = UITheme.Current;
        group = gameObject.AddComponent<CanvasGroup>();

        var dim = gameObject.AddComponent<Image>();
        dim.color = theme.dim;
        var dimButton = gameObject.AddComponent<Button>();
        dimButton.transition = Selectable.Transition.None;
        dimButton.onClick.AddListener(() => { if (closable) Close(); });

        Window = UIKit.Place(UIKit.Card(transform, theme.panel, 32f, 0.6f, "Window"), new Vector2(0.5f, 0.5f), Vector2.zero, size);
        Window.gameObject.AddComponent<Button>().transition = Selectable.Transition.None; // Clicks inside don't close it
        var outline = UIKit.CardBg(Window).gameObject.AddComponent<Outline>();
        outline.effectColor = theme.outline;
        outline.effectDistance = new Vector2(2f, -2f);

        TitleText = UIKit.Title(Window, title, 48f);
        UIKit.Place(TitleText.rectTransform, new Vector2(0f, 1f), new Vector2(44f, -28f), new Vector2(size.x - 160f, 64f));

        if (canClose)
        {
            var x = UIKit.RoundButton(Window, "×", Close, 56f);
            UIKit.Place((RectTransform)x.transform, new Vector2(1f, 1f), new Vector2(-28f, -28f), new Vector2(56f, 56f));
            x.GetComponent<UIButtonFx>().ClickSound = UISound.Kind.Back;
        }

        Body = UIKit.Rect("Body", Window);
        UIKit.Stretch(Body, 44f, 44f, 112f, 120f);

        Footer = UIKit.Rect("Footer", Window);
        Footer.anchorMin = new Vector2(0f, 0f);
        Footer.anchorMax = new Vector2(1f, 0f);
        Footer.pivot = new Vector2(0.5f, 0f);
        Footer.sizeDelta = new Vector2(-88f, 80f);
        Footer.anchoredPosition = new Vector2(0f, 28f);
        UIKit.HStack(Footer, 16f, null, TextAnchor.MiddleRight, expandHeight: false);

        gameObject.SetActive(false);
    }

    /// <summary>Removes the footer and lets the body use its space (lists without buttons).</summary>
    public void NoFooter()
    {
        Footer.gameObject.SetActive(false);
        Body.offsetMin = new Vector2(Body.offsetMin.x, 44f);
    }

    public void Open()
    {
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        open = true;
        t = 0f;
        Apply();
        UIKit.Relayout(Window);
        relayoutNextFrame = true;
    }

    private bool relayoutNextFrame;

    public void Close()
    {
        if (!open) return;
        open = false;
        UISound.Play(UISound.Kind.Back);
        Closed?.Invoke();
    }

    private void Update()
    {
        if (open && closable && Input.GetKeyDown(KeyCode.Escape) && IsTopModal()) { Close(); return; }

        if (relayoutNextFrame) { relayoutNextFrame = false; UIKit.Relayout(Window); }
        t = Mathf.MoveTowards(t, open ? 1f : 0f, Time.unscaledDeltaTime / 0.18f);
        Apply();
        if (!open && t <= 0f) gameObject.SetActive(false);
    }

    private void Apply()
    {
        float e = open ? 1f - Mathf.Pow(1f - t, 3f) : t * t;
        group.alpha = e;
        group.interactable = group.blocksRaycasts = open;
        float s = Mathf.Lerp(0.92f, 1f, e);
        Window.localScale = new Vector3(s, s, 1f);
    }

    // Escape only closes the front-most open modal
    private bool IsTopModal()
    {
        var parent = transform.parent;
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            var other = parent.GetChild(i).GetComponent<UIModal>();
            if (other != null && other.open) return other == this;
        }
        return true;
    }
}
