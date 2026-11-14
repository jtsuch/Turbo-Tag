using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The settings window content, built into any rect (the main menu's modal now; the pause menu later):
/// tabs for Gameplay (sensitivity, field of view, invert Y), Video (display mode, resolution, VSync, frame
/// cap, quality, menu scale), Audio (master, music) and Controls (every keybind, click to rebind, reset).
/// Every change applies and saves immediately through <see cref="SettingsManager"/> and <see cref="Keybinds"/>.
/// </summary>
public class SettingsPanel : MonoBehaviour
{
    private static readonly (string label, InputAction action)[] KeyRows =
    {
        ("Shoot / Use",   InputAction.Action),
        ("Jump",          InputAction.Jump),
        ("Sprint",        InputAction.Sprint),
        ("Crouch",        InputAction.Crouch),
        ("Prone / Slide", InputAction.Prone),
        ("Grab",          InputAction.Grab),
        ("Reload",        InputAction.Reload),
        ("Free Spin",     InputAction.FreeSpin),
        ("Scores",        InputAction.Scores),
        ("Map",           InputAction.Map),
        ("Emotes",        InputAction.Emote),
        ("Taunt (hold: throw)", InputAction.Taunt),
        ("Ping Wheel",    InputAction.Ping),
        ("Hunter Class",  InputAction.HunterClass),
        ("Hunter Tool",   InputAction.HunterTool),
        ("Watch Last Hider", InputAction.Spectate),
        ("Bomb (tap: hold it, hold: plant/defuse)", InputAction.Bomb),
        ("Drop Bomb", InputAction.DropBomb),
        ("Shop (gun modes)", InputAction.Shop),
        ("Knife",         InputAction.WeaponKnife),
        ("Secondary",     InputAction.WeaponSecondary),
        ("Primary",       InputAction.WeaponPrimary),
        ("Ability 1",     InputAction.AbilityBasic),
        ("Ability 2",     InputAction.AbilityQuick),
        ("Ability 3",     InputAction.AbilityThrow),
        ("Ability 4",     InputAction.AbilityTrap),
    };

    private static readonly int[] FpsCaps = { 30, 60, 90, 120, 144, 165, 240, 360 };

    private readonly List<(Button tab, RectTransform page)> tabs = new();
    private readonly List<(InputAction action, TMP_Text label)> keyLabels = new();
    private readonly KeyListener listener = new();
    private InputAction listeningFor;

    /// <summary>True while a settings panel waits for a key (Escape then cancels the listen, not the menu).</summary>
    public static bool IsListening { get; private set; }
    private TMP_Text listeningLabel;
    private RectTransform content;

    private static SettingsManager S => SettingsManager.Ensure();

    /// <summary>Builds the panel filling <paramref name="parent"/>.</summary>
    public static SettingsPanel Create(RectTransform parent)
    {
        var root = UIKit.Rect("Settings", parent);
        UIKit.Stretch(root);
        var panel = root.gameObject.AddComponent<SettingsPanel>();
        panel.Build();
        return panel;
    }

    private void OnEnable()  => Keybinds.Changed += RefreshKeys;
    private void OnDisable()
    {
        Keybinds.Changed -= RefreshKeys;
        StopListening();
    }

    private void Build()
    {
        var bar = UIKit.Rect("Tabs", transform);
        bar.anchorMin = new Vector2(0f, 1f);
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.sizeDelta = new Vector2(0f, 64f);
        UIKit.HStack(bar, 12f, null, TextAnchor.MiddleLeft, expandHeight: true);

        content = UIKit.Rect("Pages", transform);
        UIKit.Stretch(content, 0f, 0f, 88f, 0f);

        AddTab(bar, "Gameplay", BuildGameplay);
        AddTab(bar, "Video", BuildVideo);
        AddTab(bar, "Audio", BuildAudio);
        AddTab(bar, "Controls", BuildControls);
        Show(0);
    }

    private void AddTab(Transform bar, string name, System.Action<RectTransform> build)
    {
        int index = tabs.Count;
        var button = UIKit.Button(bar, name, UIKit.Style.Ghost, () => Show(index), 60f, 26f);
        UIKit.Size(button, 210f, 60f);
        var page = UIKit.Rect(name, content);
        UIKit.Stretch(page);
        build(UIKit.ScrollList(page, 12f));
        tabs.Add((button, page));
    }

    private void Show(int index)
    {
        for (int i = 0; i < tabs.Count; i++)
        {
            bool on = i == index;
            tabs[i].page.gameObject.SetActive(on);
            var fx = tabs[i].tab.GetComponent<UIButtonFx>();
            fx.FaceColor = on ? UITheme.Current.secondary : UITheme.Current.card;
        }
        StopListening();
        UIKit.Relayout((RectTransform)tabs[index].page);
    }

    // ─── Pages ────────────────────────────────────────────────────────────────

    private void BuildGameplay(RectTransform list)
    {
        SliderRow(list, "Mouse Sensitivity", "100 = one-to-one with your mouse", 10f, 300f, S.Sensitivity, true,
            v => S.Sensitivity = v, v => $"{v:0}");
        SliderRow(list, "Field of View", "Vertical degrees. Higher sees more around you", 50f, 100f, S.FieldOfView, true,
            v => S.FieldOfView = v, v => $"{v:0}°");
        SwitchRow(list, "Invert Mouse Y", "Moving the mouse up looks down", S.InvertY, v => S.InvertY = v);
        SwitchRow(list, "Control Hints", "List the less obvious keys on the right of the screen", ControlsHint.Enabled, v => ControlsHint.Enabled = v);
        SelectorRow(list, "Crosshair", "The mark in the middle of the screen", CrosshairSettings.Styles, CrosshairSettings.Style,
            i => CrosshairSettings.Style = i);
        SelectorRow(list, "Crosshair Colour", null, CrosshairSettings.ColorNames, CrosshairSettings.ColorIndex,
            i => CrosshairSettings.ColorIndex = i);
        SliderRow(list, "Crosshair Size", null, 50f, 200f, CrosshairSettings.Size * 100f, true,
            v => CrosshairSettings.Size = v / 100f, v => $"{v:0}%");
        SwitchRow(list, "Crosshair Outline", "A dark edge so it shows on bright walls", CrosshairSettings.Outline,
            v => CrosshairSettings.Outline = v);
    }

    private void BuildVideo(RectTransform list)
    {
        var modes = SettingsManager.DisplayModes;
        int modeIndex = System.Array.IndexOf(modes, S.DisplayMode);
        var modeNames = System.Array.ConvertAll(modes, SettingsManager.DisplayModeName);
        SelectorRow(list, "Display Mode", "Borderless is best for alt-tabbing", modeNames, Mathf.Max(0, modeIndex),
            i => S.DisplayMode = modes[i]);

        var sizes = SettingsManager.Resolutions();
        int sizeIndex = Mathf.Max(0, sizes.IndexOf(S.Resolution));
        SelectorRow(list, "Resolution", null, sizes.ConvertAll(r => $"{r.x} × {r.y}").ToArray(), sizeIndex,
            i => S.Resolution = sizes[i]);

        SwitchRow(list, "VSync", "Locks to your monitor's refresh rate (no tearing)", S.VSync, v => S.VSync = v);

        int fpsIndex = System.Array.IndexOf(FpsCaps, S.TargetFps);
        if (fpsIndex < 0) fpsIndex = System.Array.IndexOf(FpsCaps, 144);
        SelectorRow(list, "Frame Rate Limit", "Used when VSync is off", System.Array.ConvertAll(FpsCaps, f => $"{f} FPS"), fpsIndex,
            i => S.TargetFps = FpsCaps[i]);

        var levels = QualitySettings.names;
        SelectorRow(list, "Graphics Quality", null, levels, S.Quality, i => S.Quality = i);

        SliderRow(list, "Menu Scale", "Makes menus bigger or smaller", 80f, 125f, S.UiScale * 100f, true,
            v => S.UiScale = v / 100f, v => $"{v:0}%");
    }

    private void BuildAudio(RectTransform list)
    {
        SliderRow(list, "Master Volume", null, 0f, 100f, S.SfxVolume * 100f, true, v => S.SfxVolume = v / 100f, v => $"{v:0}%");
        SliderRow(list, "Music Volume", null, 0f, 100f, S.MusicVolume * 100f, true, v => S.MusicVolume = v / 100f, v => $"{v:0}%");
    }

    private void BuildControls(RectTransform list)
    {
        foreach (var (label, action) in KeyRows)
        {
            var row = UIKit.Row(list, label, null, 68f);
            var button = UIKit.Button(row, "", UIKit.Style.Ghost, null, 52f, 24f);
            UIKit.Size(button, 220f, 52f);
            var text = UIKit.Label(button);
            text.font = UIKit.FontAsset(UIKit.Font.Bold);
            button.onClick.AddListener(() => Listen(action, text));
            keyLabels.Add((action, text));
        }

        var reset = UIKit.Button(list, "Reset Controls", UIKit.Style.Danger, () =>
        {
            Keybinds.ResetAll();
            RefreshKeys();
        }, 60f, 26f);
        UIKit.Size(reset, height: 60f);
        RefreshKeys();
    }

    // ─── Keybinding ───────────────────────────────────────────────────────────

    private void Listen(InputAction action, TMP_Text label)
    {
        StopListening();
        listeningFor = action;
        listeningLabel = label;
        label.text = "Press a key…";
        label.color = UITheme.Current.primary;
        listener.Start();
        IsListening = true;
    }

    private void StopListening()
    {
        IsListening = false;
        if (!listener.IsListening) return;
        listener.Stop();
        RefreshKeys();
    }

    private void Update()
    {
        if (!listener.IsListening) return;
        if (Input.GetKeyDown(KeyCode.Escape)) { StopListening(); return; }
        if (listener.Poll(out KeyCode key))
        {
            IsListening = false;
            Keybinds.Set(listeningFor, key);
            UISound.Play(UISound.Kind.Confirm);
            RefreshKeys();
        }
    }

    private void RefreshKeys()
    {
        foreach (var (action, label) in keyLabels)
        {
            if (label == null) continue;
            KeyCode key = Keybinds.Get(action);
            label.text = key == KeyCode.None ? "Not bound" : Keybinds.Label(key);
            label.color = key == KeyCode.None ? UITheme.Current.textMuted : UITheme.Current.text;
        }
    }

    // ─── Row helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// A settings row with a slider and a live readout. Returns the slider (refresh it with <see cref="ShowValue"/>).
    /// <paramref name="typeable"/>: the readout is a text box instead, and a typed value may go past the slider's range
    /// (anything down to <paramref name="hardMin"/>, no upper limit): for cheats.
    /// </summary>
    public static Slider SliderRow(Transform list, string label, string hint, float min, float max, float value, bool whole,
        System.Action<float> apply, System.Func<float, string> format, bool typeable = false, float hardMin = 0f)
    {
        var row = UIKit.Row(list, label, hint, hint != null ? 84f : 72f);
        var slider = UIKit.Slider(row, min, max, value, whole, null);
        UIKit.Size(slider, 380f, 44f, 0f);

        if (typeable)
        {
            var field = UIKit.InputField(row, "", height: 50f);
            UIKit.Size(field, 140f, 50f);
            field.textComponent.alignment = TextAlignmentOptions.Center;
            field.SetTextWithoutNotify(format(value));
            var box = slider.gameObject.AddComponent<UISliderInput>();
            box.Bind(field, format);
            slider.onValueChanged.AddListener(v =>
            {
                field.SetTextWithoutNotify(format(v));
                apply(v);
            });
            field.onEndEdit.AddListener(text =>
            {
                string digits = System.Text.RegularExpressions.Regex.Replace(text, @"[^0-9.\-]", "");
                if (!float.TryParse(digits, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v))
                {
                    field.SetTextWithoutNotify(format(slider.value));
                    return;
                }
                v = Mathf.Max(hardMin, v);
                slider.SetValueWithoutNotify(v); // The handle stops at its end; the value doesn't
                field.SetTextWithoutNotify(format(v));
                apply(v);
            });
            return slider;
        }

        var readout = UIKit.Text(row, format(value), 24f, UIKit.Font.Bold, UITheme.Current.text, TextAlignmentOptions.Right);
        UIKit.Size(readout, 90f);
        slider.onValueChanged.AddListener(v =>
        {
            readout.text = format(v);
            apply(v);
        });
        slider.gameObject.AddComponent<UISliderReadout>().Bind(readout, format);
        return slider;
    }

    /// <summary>Sets a slider row's value from code, including its typed-value box if it has one.</summary>
    public static void ShowValue(Slider slider, float value)
    {
        if (slider.TryGetComponent(out UISliderInput box)) box.Show(value);
        else slider.SetValueWithoutNotify(value);
    }

    private static void SwitchRow(Transform list, string label, string hint, bool value, System.Action<bool> apply)
    {
        var row = UIKit.Row(list, label, hint, hint != null ? 84f : 72f);
        UIKit.Switch(row, value, v => apply(v));
    }

    private static void SelectorRow(Transform list, string label, string hint, string[] options, int index, System.Action<int> apply)
    {
        var row = UIKit.Row(list, label, hint, hint != null ? 84f : 72f);
        UIKit.Selector(row, options, index, i => apply(i), 420f);
    }
}

/// <summary>The typed-value box next to a cheats slider (see <see cref="SettingsPanel.SliderRow"/>).</summary>
public class UISliderInput : MonoBehaviour
{
    private TMP_InputField field;
    private System.Func<float, string> format;

    public void Bind(TMP_InputField input, System.Func<float, string> formatter)
    {
        field = input;
        format = formatter;
    }

    /// <summary>Shows a value set from code (it may be past the slider's range).</summary>
    public void Show(float value)
    {
        GetComponent<Slider>().SetValueWithoutNotify(value);
        if (!field.isFocused) field.SetTextWithoutNotify(format(value));
    }
}

/// <summary>Keeps a slider's readout text in step when the value is set from code (SetValueWithoutNotify).</summary>
public class UISliderReadout : MonoBehaviour
{
    private Slider slider;
    private TMP_Text readout;
    private System.Func<float, string> format;
    private float last = float.NaN;

    public void Bind(TMP_Text text, System.Func<float, string> formatter)
    {
        slider = GetComponent<Slider>();
        readout = text;
        format = formatter;
    }

    private void LateUpdate()
    {
        if (slider == null || Mathf.Approximately(slider.value, last)) return;
        last = slider.value;
        readout.text = format(last);
    }
}
