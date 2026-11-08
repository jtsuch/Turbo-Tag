using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A faint sound visualizer at the edges of the screen: a soft glow on the side a loud sound came from, so players
/// can place others without headphones. Only loud things count: another player sprinting (full running, not
/// walking), a taunt, and noisy abilities (<see cref="Fx.Ability"/>). Only players who aren't on your side (in
/// modes without teams, everyone else) within <see cref="Range"/>. One glow per player, refreshed by each sound.
/// Its own canvas, built in code; added next to the GameModeManager.
/// </summary>
public class SoundVisualizer : MonoBehaviour
{
    private const float Range    = 35f;  // Sounds further than this don't show
    private const float Show     = 0.9f; // How long a glow lasts after a sound
    private const float MaxAlpha = 0.6f; // Faint (the glow sprite is soft, so this reads as a light haze)

    private struct Sound { public Vector3 Point; public float At; public float Loudness; }

    private static readonly Dictionary<int, Sound> sounds = new();
    private RectTransform root;
    private readonly List<Image> pool = new();

    /// <summary>A sound at <paramref name="point"/> by <paramref name="actor"/>, 0..1 loud (from every screen's own copy).</summary>
    public static void Notify(Vector3 point, float loudness, int actor)
    {
        var local = Player.Local;
        if (local == null || local.ActorNumber == actor || local.IsCaught) return;
        if (Vector3.Distance(point, local.transform.position) > Range) return;
        var source = Player.Find(actor);
        var mode = GameModeManager.Instance != null ? GameModeManager.Instance.Mode : null;
        if (source != null && mode != null && mode.UsesRoles && mode.SameTeam(local.Role, source.Role)) return; // Teammates: no need
        sounds[actor] = new Sound { Point = point, At = Time.time, Loudness = Mathf.Clamp01(loudness) };
    }

    private void Awake() => sounds.Clear();

    private void Start()
    {
        var canvas = UIKit.Canvas("Sound Canvas", 1);
        canvas.transform.SetParent(transform, false);
        root = (RectTransform)canvas.transform;
    }

    private void LateUpdate()
    {
        var local = Player.Local;
        var cam = PlayerCam.Local != null ? PlayerCam.Local.Camera : null;
        int used = 0;
        if (local != null && cam != null && root != null)
        {
            Vector3 forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            Vector2 half = root.rect.size * 0.5f;
            var stale = new List<int>();
            foreach (var pair in sounds)
            {
                var s = pair.Value;
                float age = Time.time - s.At;
                if (age > Show) { stale.Add(pair.Key); continue; }
                Vector3 to = Vector3.ProjectOnPlane(s.Point - local.transform.position, Vector3.up);
                if (to.sqrMagnitude < 0.25f || forward.sqrMagnitude < 0.001f) continue;
                float angle = Vector3.SignedAngle(forward, to, Vector3.up) * Mathf.Deg2Rad;
                // On the screen's edge, in the sound's direction (ahead is the top, behind the bottom)
                Vector2 pos = new(Mathf.Sin(angle) * (half.x - 40f), Mathf.Cos(angle) * (half.y - 40f));
                float near = 1f - Mathf.Clamp01(to.magnitude / Range) * 0.6f;
                var glow = Glow(used++);
                glow.rectTransform.anchoredPosition = pos;
                glow.color = new Color(0.85f, 0.92f, 1f, MaxAlpha * s.Loudness * near * (1f - age / Show));
            }
            foreach (int key in stale) sounds.Remove(key);
        }
        for (int i = used; i < pool.Count; i++) pool[i].gameObject.SetActive(false);
    }

    private Image Glow(int index)
    {
        while (pool.Count <= index)
        {
            var img = UIKit.Rect("Sound", root).gameObject.AddComponent<Image>();
            img.sprite = UISprites.Blob;
            img.raycastTarget = false;
            img.rectTransform.sizeDelta = new Vector2(320f, 320f);
            pool.Add(img);
        }
        pool[index].gameObject.SetActive(true);
        return pool[index];
    }
}
