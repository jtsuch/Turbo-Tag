using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Team pings on screen: a coloured dot with what it says, who sent it and how far it is, over the spot in the
/// world (held at the screen's edge when it's off screen or behind you). Pings last <see cref="Lifetime"/> seconds;
/// the minimap shows them too (<see cref="Active"/>). Its own canvas, built in code; added next to the GameModeManager.
/// </summary>
public class PingMarkers : MonoBehaviour
{
    private const float Lifetime = 8f;
    private const int   MaxPings = 8;

    private struct Ping { public Vector3 Point; public PingKind Kind; public string From; public float Until; }

    private static readonly List<Ping> pings = new();
    private static PingMarkers instance;

    private RectTransform root;
    private readonly List<(RectTransform rect, Image dot, TMP_Text text)> pool = new();

    public static Color ColorOf(PingKind kind) => kind switch
    {
        PingKind.HideHere    => new Color(0.45f, 0.95f, 0.45f),
        PingKind.HunterThere => new Color(1f, 0.3f, 0.3f),
        PingKind.MeetThere   => new Color(0.35f, 0.7f, 1f),
        _                    => new Color(1f, 0.85f, 0.25f),
    };

    private static string Label(PingKind kind) => kind switch
    {
        PingKind.HideHere    => "Hide here",
        PingKind.HunterThere => "Hunter there",
        PingKind.MeetThere   => "Let's meet there",
        _                    => "Run that way",
    };

    /// <summary>The pings up now (world point, colour), for the minimap.</summary>
    public static IEnumerable<(Vector3 point, Color color)> Active
    {
        get { foreach (var p in pings) if (Time.time < p.Until) yield return (p.Point, ColorOf(p.Kind)); }
    }

    public static void Add(Vector3 point, PingKind kind, string from)
    {
        pings.RemoveAll(p => p.From == from && p.Kind == kind); // A new one of the same kind replaces yours
        pings.Add(new Ping { Point = point, Kind = kind, From = from, Until = Time.time + Lifetime });
        while (pings.Count > MaxPings) pings.RemoveAt(0);
        Sfx.Here(SfxKind.Tick, 0.7f, kind == PingKind.HunterThere ? 0.8f : 1.2f);
    }

    private void Awake()
    {
        instance = this;
        pings.Clear();
    }

    private void OnDestroy()
    {
        if (instance == this) { instance = null; pings.Clear(); }
    }

    private void Start()
    {
        var canvas = UIKit.Canvas("Ping Canvas", 2);
        canvas.transform.SetParent(transform, false);
        root = (RectTransform)canvas.transform;
    }

    private void LateUpdate()
    {
        pings.RemoveAll(p => Time.time >= p.Until);
        var cam = PlayerCam.Local != null ? PlayerCam.Local.Camera : null;
        int used = 0;
        if (cam != null && root != null)
        {
            Vector2 half = root.rect.size * 0.5f;
            foreach (var p in pings)
            {
                Vector3 screen = cam.WorldToScreenPoint(p.Point + Vector3.up * 0.5f);
                bool behind = screen.z < 0f;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out Vector2 pos);
                // Behind you: mirrored, and always down at the bottom edge
                if (behind) pos = new Vector2(-pos.x, -Mathf.Abs(pos.y) - 1f);
                // Off screen or behind: held at the edge in its direction
                Vector2 limit = half - new Vector2(120f, pos.y < 0f ? 200f : 70f); // Stay above the ability bar
                if (behind || Mathf.Abs(pos.x) > limit.x || Mathf.Abs(pos.y) > limit.y)
                {
                    float k = Mathf.Min(limit.x / Mathf.Max(0.01f, Mathf.Abs(pos.x)), limit.y / Mathf.Max(0.01f, Mathf.Abs(pos.y)));
                    pos *= behind ? k : Mathf.Min(1f, k);
                }
                var (rect, dot, text) = Marker(used++);
                rect.anchoredPosition = pos;
                float fade = Mathf.Clamp01((p.Until - Time.time) / 1.5f);
                Color c = ColorOf(p.Kind);
                dot.color = new Color(c.r, c.g, c.b, fade);
                float metres = Vector3.Distance(cam.transform.position, p.Point);
                text.text = $"<b>{Label(p.Kind)}</b>  {metres:0} m\n<size=70%>{p.From}</size>";
                text.alpha = fade;
            }
        }
        for (int i = used; i < pool.Count; i++) pool[i].rect.gameObject.SetActive(false);
    }

    private (RectTransform rect, Image dot, TMP_Text text) Marker(int index)
    {
        while (pool.Count <= index)
        {
            var rect = UIKit.Rect("Ping", root);
            rect.sizeDelta = new Vector2(30f, 30f);
            var dot = rect.gameObject.AddComponent<Image>();
            dot.sprite = UISprites.Circle;
            dot.raycastTarget = false;
            var outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.6f);
            outline.effectDistance = new Vector2(2f, -2f);
            var text = UIKit.Text(rect, "", 22f, UIKit.Font.Bold, Color.white, TextAlignmentOptions.Center);
            text.rectTransform.anchoredPosition = new Vector2(0f, 46f);
            text.rectTransform.sizeDelta = new Vector2(320f, 56f);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            pool.Add((rect, dot, text));
        }
        var m = pool[index];
        m.rect.gameObject.SetActive(true);
        return m;
    }
}
