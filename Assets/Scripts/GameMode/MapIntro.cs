using System.Collections;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The map intro at the start of a match, before the countdown (on every screen, local): a slow pan round the
/// level with its name across the top, then the view settles back into your penguin. The master starts it once
/// everyone has spawned by writing <see cref="RoomKeys.IntroStart"/> and starts the match <see cref="Seconds"/>
/// later (<see cref="GameModeManager"/>); modes without a countdown skip it (<see cref="GameMode.PlaysMapIntro"/>).
/// Movement is locked while it plays. Added next to GameModeManager.
/// </summary>
public class MapIntro : MonoBehaviour
{
    public const float Seconds = 4f;

    /// <summary>Time.time when the last intro ended (the countdown's own fly-over skips right after one).</summary>
    public static float LastEnded { get; private set; } = -100f;

    private GameModeManager manager;
    private bool played, locked, viewing;
    private Transform cam;
    private Canvas canvas;

    private void Start() => manager = GetComponent<GameModeManager>();

    private void Update()
    {
        if (played || manager == null || manager.Mode == null || manager.Phase != MatchPhase.Waiting) return;
        double start = NetProps.Room(RoomKeys.IntroStart, 0d);
        if (start <= 0d) return;
        played = true;
        float left = Seconds - (float)(PhotonNetwork.Time - start);
        if (left > 0.75f) StartCoroutine(Play(left));
    }

    private IEnumerator Play(float seconds)
    {
        var local = Player.Local;
        var view = PlayerCam.Local;
        var b = LevelInfo.Bounds;
        if (local == null || view == null || !b.HasValue || Killcam.IsPlaying || view.HasViewOverride) yield break;

        if (cam == null) cam = new GameObject("Map Intro Camera").transform;
        Vector3 centre = b.Value.center;
        float radius = Mathf.Clamp(Mathf.Max(b.Value.extents.x, b.Value.extents.z) * 1.05f, 30f, 160f);
        float height = Mathf.Max(b.Value.max.y, centre.y) + radius * 0.45f;
        float startAngle = Random.Range(0f, 360f);

        view.SetViewOverride(cam);
        viewing = true;
        if (local.TryGetComponent(out PlayerInitializer init)) init.SetOwnBodyVisible(true);
        if (!local.MovementLocked) { local.MovementLocked = true; locked = true; }
        ShowTitle();

        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            if (Player.Local == null || PlayerCam.Local == null) break;
            float k = t / seconds;
            // A slow, steady pan round the whole map; the last quarter eases down into your penguin's view
            float angle = (startAngle + k * 75f) * Mathf.Deg2Rad;
            Vector3 orbit = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius + Vector3.up * (height - centre.y);
            Vector3 eye = local.transform.position + Vector3.up * local.EyeHeight;
            float home = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.75f, 1f, k));
            Vector3 pos = Vector3.Lerp(orbit, eye, home);
            Vector3 look = Vector3.Lerp(centre, eye + local.transform.forward * 10f, home);
            cam.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
            SetTitleAlpha(Mathf.Min(1f, t / 0.4f, (seconds - t) / 0.6f));
            yield return null;
        }
        End();
    }

    private void End()
    {
        if (viewing)
        {
            viewing = false;
            var view = PlayerCam.Local;
            if (view != null && view.HasViewOverride && !Killcam.IsPlaying) view.SetViewOverride(null);
            if (Player.Local != null && Player.Local.TryGetComponent(out PlayerInitializer init)) init.SetOwnBodyVisible(false);
            LastEnded = Time.time;
        }
        if (locked && Player.Local != null) Player.Local.MovementLocked = false;
        locked = false;
        if (canvas != null) Destroy(canvas.gameObject);
    }

    private void OnDestroy()
    {
        End();
        if (cam != null) Destroy(cam.gameObject);
    }

    // ─── The title: the map's name across the top, the mode under it ──────────

    private TextMeshProUGUI title, subtitle;

    private void ShowTitle()
    {
        canvas = UIKit.Canvas("Map Intro", 15);
        var rt = (RectTransform)canvas.transform;
        string map = System.Text.RegularExpressions.Regex.Replace(SceneManager.GetActiveScene().name, "(?<=[a-z])(?=[A-Z])", " ");
        title = UIKit.Text(rt, map.ToUpperInvariant(), 96f, UIKit.Font.Display, Color.white, TextAlignmentOptions.Center, "Map Name");
        Place(title.rectTransform, -70f, 120f);
        title.outlineWidth = 0.18f;
        title.outlineColor = new Color(0f, 0f, 0f, 0.6f);
        subtitle = UIKit.Text(rt, manager.Mode.DisplayName, 40f, UIKit.Font.Bold, new Color(1f, 1f, 1f, 0.9f), TextAlignmentOptions.Center, "Mode");
        Place(subtitle.rectTransform, -165f, 56f);
        SetTitleAlpha(0f);
    }

    private static void Place(RectTransform r, float y, float h)
    {
        r.anchorMin = new Vector2(0f, 1f);
        r.anchorMax = new Vector2(1f, 1f);
        r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = new Vector2(0f, y);
        r.sizeDelta = new Vector2(0f, h);
    }

    private void SetTitleAlpha(float a)
    {
        if (title != null) title.alpha = a;
        if (subtitle != null) subtitle.alpha = a * 0.9f;
    }
}
