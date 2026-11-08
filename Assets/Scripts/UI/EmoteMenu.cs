using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD: the emote wheel. Hold the Emote key (default T): the emotes (<see cref="PlayerEmotes.All"/>) sit round the
/// middle of the screen; flick the mouse toward one and let go to play it (let go without a flick to cancel). Number
/// keys still play one while the wheel is open. The view holds still while it's open (PlayerCam), movement keeps
/// working, and the number keys belong to the wheel (Free Spin waits). Your own emote shows as a line under the
/// crosshair, since your head is hidden from you. Part of the HUD canvas.
/// </summary>
public class EmoteMenu : MonoBehaviour
{
    private const float Flick = 28f;
    private const float Radius = 210f;

    /// <summary>True while the wheel is open (the view holds still; number keys belong to it).</summary>
    public static bool IsOpen { get; private set; }

    private RectTransform wheel;
    private Image[] backs;
    private TMP_Text echo;
    private float echoAt = -10f;
    private Vector2 travel;
    private int selected = -1;

    public static EmoteMenu Create(RectTransform hudRoot)
    {
        var rect = UIKit.Rect("EmoteMenu", hudRoot);
        UIKit.Stretch(rect);
        var menu = rect.gameObject.AddComponent<EmoteMenu>();
        menu.Build(rect);
        return menu;
    }

    private static Vector2 Direction(int i, int count)
    {
        float a = Mathf.PI * 0.5f - i * Mathf.PI * 2f / count; // First at the top, then clockwise
        return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
    }

    private void Build(RectTransform rect)
    {
        var theme = UITheme.Current;
        wheel = UIKit.Rect("Wheel", rect);
        UIKit.Place(wheel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 10f));
        int count = PlayerEmotes.All.Length;
        backs = new Image[count];
        var hub = UIKit.Text(wheel, "EMOTES", 22f, UIKit.Font.Display, theme.primary, TextAlignmentOptions.Center);
        hub.rectTransform.sizeDelta = new Vector2(200f, 40f);
        for (int i = 0; i < count; i++)
        {
            var back = UIKit.Panel(wheel, UIKit.WithAlpha(theme.panel, 0.88f), 22f, PlayerEmotes.All[i].label);
            back.rectTransform.sizeDelta = new Vector2(200f, 64f);
            back.rectTransform.anchoredPosition = Direction(i, count) * new Vector2(Radius * 1.25f, Radius * 0.85f);
            var label = UIKit.Text(back.transform, $"<size=70%><color=#FFC93C>{i + 1}</color></size>  {PlayerEmotes.All[i].label}", 26f, UIKit.Font.Display, theme.text, TextAlignmentOptions.Center);
            UIKit.Stretch(label.rectTransform);
            backs[i] = back;
        }
        wheel.gameObject.SetActive(false);

        echo = UIKit.OneLine(UIKit.Text(rect, "", 26f, UIKit.Font.Bold, theme.text, TextAlignmentOptions.Center, "Echo"));
        UIKit.Place(echo.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), new Vector2(800f, 36f));
        echo.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        echo.rectTransform.anchoredPosition = new Vector2(0f, -60f);
        echo.gameObject.SetActive(false);
        foreach (var g in GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
    }

    private void OnDestroy() => IsOpen = false;

    private void Update()
    {
        var local = Player.Local;
        bool blocked = local == null || local.IsCaught || PauseMenuManager.IsPaused || GunPicker.IsOpen || PingWheel.IsOpen;
        KeyCode key = Keybinds.Get(InputAction.Emote);
        if (blocked) SetOpen(false);
        else if (!IsOpen && Input.GetKeyDown(key)) SetOpen(true);

        if (IsOpen && local != null && local.TryGetComponent(out PlayerEmotes emotes))
        {
            // Flick toward one
            travel += new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 4f;
            travel = Vector2.ClampMagnitude(travel, 120f);
            selected = -1;
            if (travel.magnitude > Flick)
            {
                float best = float.MinValue;
                for (int i = 0; i < backs.Length; i++)
                {
                    float dot = Vector2.Dot(travel.normalized, Direction(i, backs.Length));
                    if (dot > best) { best = dot; selected = i; }
                }
            }
            for (int i = 0; i < backs.Length; i++) backs[i].transform.localScale = Vector3.one * (i == selected ? 1.15f : 1f);

            int chosen = -1;
            for (int i = 0; i < PlayerEmotes.All.Length && i < 9; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) chosen = i;
            if (chosen < 0 && Input.GetKeyUp(key)) chosen = selected;
            if (chosen >= 0 || Input.GetKeyUp(key))
            {
                if (chosen >= 0 && emotes.Play(chosen))
                {
                    echo.text = $"<color=#B9B2E0>You:</color> {PlayerEmotes.All[chosen].bubble}";
                    echoAt = Time.unscaledTime;
                }
                SetOpen(false);
            }
        }

        float age = Time.unscaledTime - echoAt;
        bool show = age < 1.8f;
        if (echo.gameObject.activeSelf != show) echo.gameObject.SetActive(show);
        if (show) echo.alpha = Mathf.Clamp01((1.8f - age) / 0.4f);
    }

    private void SetOpen(bool open)
    {
        if (IsOpen == open) return;
        IsOpen = open;
        wheel.gameObject.SetActive(open);
        travel = Vector2.zero;
        selected = -1;
        if (open) UISound.Play(UISound.Kind.Click);
    }
}
