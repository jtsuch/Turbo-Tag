using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The ping wheel: hold <see cref="InputAction.Ping"/> (default middle mouse), flick the mouse up, right, down or
/// left, and let go: "Hide here", "Hunter there", "Let's meet there" or "Run that way" is marked where you're
/// looking, for your team (<see cref="PlayerCallouts.SendPing"/>). Let go without a flick to cancel. The view holds
/// still while the wheel is open (PlayerCam). Off while the Teleport Gun donut uses the middle mouse.
/// Its own canvas, built in code; added next to the GameModeManager.
/// </summary>
public class PingWheel : MonoBehaviour
{
    private const float Flick = 28f;   // Mouse travel (pixels-ish) that picks a direction
    private const float Range = 250f;  // How far the ping can land

    private static readonly (PingKind kind, string label, Vector2 dir)[] Options =
    {
        (PingKind.HideHere,    "Hide here",        Vector2.up),
        (PingKind.HunterThere, "Hunter there",     Vector2.right),
        (PingKind.MeetThere,   "Let's meet there", Vector2.down),
        (PingKind.RunThatWay,  "Run that way",     Vector2.left),
    };

    /// <summary>True while the wheel is held open (the view doesn't turn).</summary>
    public static bool IsOpen { get; private set; }

    private RectTransform wheel;
    private readonly TMP_Text[] labels = new TMP_Text[4];
    private readonly Image[] backs = new Image[4];
    private Vector2 travel;
    private int selected = -1;

    private void Start()
    {
        var theme = UITheme.Current;
        var canvas = UIKit.Canvas("Ping Wheel Canvas", 4);
        canvas.transform.SetParent(transform, false);
        wheel = UIKit.Rect("Wheel", canvas.transform);
        UIKit.Place(wheel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 10f));
        for (int i = 0; i < Options.Length; i++)
        {
            var back = UIKit.Panel(wheel, UIKit.WithAlpha(theme.panel, 0.85f), 20f, Options[i].label);
            back.rectTransform.sizeDelta = new Vector2(240f, 58f);
            back.rectTransform.anchoredPosition = Options[i].dir * new Vector2(210f, 120f);
            back.raycastTarget = false;
            var text = UIKit.Text(back.transform, Options[i].label, 24f, UIKit.Font.Display, PingMarkers.ColorOf(Options[i].kind), TextAlignmentOptions.Center);
            UIKit.Stretch(text.rectTransform);
            text.raycastTarget = false;
            backs[i] = back;
            labels[i] = text;
        }
        wheel.gameObject.SetActive(false);
    }

    private void OnDestroy() => IsOpen = false;

    private void Update()
    {
        var local = Player.Local;
        KeyCode key = Keybinds.Get(InputAction.Ping);
        bool usable = local != null && !local.IsCaught && !PauseMenuManager.IsPaused && PlayerCam.Local != null
                      && (key != KeyCode.Mouse2 || local.GetComponent<TeleportGunEffect>() == null);
        if (!usable) { Close(); return; }

        if (!IsOpen && Input.GetKeyDown(key)) Open();
        if (!IsOpen) return;

        travel += new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 4f;
        travel = Vector2.ClampMagnitude(travel, 120f);
        selected = -1;
        if (travel.magnitude > Flick)
        {
            float best = float.MinValue;
            for (int i = 0; i < Options.Length; i++)
            {
                float dot = Vector2.Dot(travel.normalized, Options[i].dir);
                if (dot > best) { best = dot; selected = i; }
            }
        }
        for (int i = 0; i < Options.Length; i++)
            backs[i].transform.localScale = Vector3.one * (i == selected ? 1.15f : 1f);

        if (Input.GetKeyUp(key))
        {
            if (selected >= 0) Send(Options[selected].kind);
            Close();
        }
    }

    private void Open()
    {
        IsOpen = true;
        travel = Vector2.zero;
        selected = -1;
        wheel.gameObject.SetActive(true);
    }

    private void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        wheel.gameObject.SetActive(false);
    }

    // Where the crosshair points (past your own body), or far ahead into the open
    private static void Send(PingKind kind)
    {
        var cam = PlayerCam.Local.Camera.transform;
        Vector3 point = cam.position + cam.forward * 40f;
        var hits = Physics.RaycastAll(cam.position, cam.forward, Range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        foreach (var hit in hits)
        {
            if (Player.Local != null && hit.collider.transform.IsChildOf(Player.Local.transform)) continue;
            if (hit.distance < nearest) { nearest = hit.distance; point = hit.point; }
        }
        if (PlayerCallouts.Local != null) PlayerCallouts.Local.SendPing(point, kind);
    }
}
