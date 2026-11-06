using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD: the feeling of speed. When the local player is really moving (<see cref="PlayerCam.SpeedFeel"/>: dashes,
/// launches, bounce pads, long falls), white streaks rush outward from the middle of the screen and a wind
/// sound rises (louder and higher with speed). The camera's FOV stretch is in PlayerCam. Part of the HUD canvas.
/// </summary>
public class SpeedFx : MonoBehaviour
{
    private const int Lines = 26;

    private sealed class Line { public RectTransform Rect; public Image Image; public float Angle, Radius, Speed, Length; }
    private readonly Line[] lines = new Line[Lines];
    private AudioSource wind;

    public static SpeedFx Create(RectTransform hudRoot)
    {
        var rect = UIKit.Rect("SpeedLines", hudRoot);
        UIKit.Stretch(rect);
        rect.SetAsFirstSibling(); // Under every card
        var fx = rect.gameObject.AddComponent<SpeedFx>();
        fx.Build(rect);
        return fx;
    }

    private void Build(RectTransform rect)
    {
        for (int i = 0; i < Lines; i++)
        {
            var img = new GameObject("Line", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            img.transform.SetParent(rect, false);
            img.raycastTarget = false;
            img.sprite = UISprites.Rounded;
            img.type = Image.Type.Sliced;
            img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            img.rectTransform.pivot = new Vector2(0f, 0.5f);
            lines[i] = new Line { Rect = img.rectTransform, Image = img };
            Respawn(lines[i], Random.Range(380f, 1100f));
        }

        wind = gameObject.AddComponent<AudioSource>();
        wind.clip = Sfx.Get(SfxKind.Wind);
        wind.loop = true;
        wind.spatialBlend = 0f;
        wind.volume = 0f;
        wind.playOnAwake = false;
        wind.Play();
    }

    private static void Respawn(Line line, float radius)
    {
        line.Angle  = Random.Range(0f, 360f);
        line.Radius = radius;
        line.Speed  = Random.Range(1400f, 2600f);
        line.Length = Random.Range(90f, 240f);
        line.Rect.localRotation = Quaternion.Euler(0f, 0f, line.Angle);
    }

    private void Update()
    {
        float feel = PlayerCam.Local != null && !PauseMenuManager.IsPaused ? PlayerCam.Local.SpeedFeel : 0f;

        for (int i = 0; i < Lines; i++)
        {
            var line = lines[i];
            bool on = feel > 0.02f;
            if (line.Rect.gameObject.activeSelf != on) line.Rect.gameObject.SetActive(on);
            if (!on) continue;

            line.Radius += line.Speed * (0.4f + feel) * Time.unscaledDeltaTime;
            if (line.Radius > 1200f) Respawn(line, Random.Range(380f, 560f));
            float a = line.Angle * Mathf.Deg2Rad;
            line.Rect.anchoredPosition = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * line.Radius;
            line.Rect.sizeDelta = new Vector2(line.Length * (0.6f + feel), 3f + 2f * feel);
            float fadeIn = Mathf.InverseLerp(380f, 520f, line.Radius);
            line.Image.color = new Color(1f, 1f, 1f, feel * 0.35f * fadeIn);
        }

        // Rushing air: up with speed, a bit higher pitched as it rises
        if (wind != null)
        {
            float target = Mathf.Lerp(0f, 0.55f, feel);
            wind.volume = Mathf.MoveTowards(wind.volume, target, Time.unscaledDeltaTime * 1.5f);
            wind.pitch = Mathf.Lerp(0.8f, 1.35f, feel);
        }
    }
}
