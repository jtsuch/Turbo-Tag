using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The animated party backdrop behind every menu: a vertical gradient, big soft colour blobs drifting slowly,
/// and little confetti pieces floating up and spinning. Built in code by <see cref="UIKit.Background"/>.
/// </summary>
public class UIBackground : MonoBehaviour
{
    private struct Piece
    {
        public RectTransform Rect;
        public Vector2 Home;
        public Vector2 Drift;
        public float   Speed, Phase, Spin;
    }

    private Piece[] blobs;
    private Piece[] confetti;
    private RectTransform area;

    public void Build(int blobCount = 6, int confettiCount = 28)
    {
        area = (RectTransform)transform;
        var theme = UITheme.Current;

        var gradient = UIKit.Rect("Gradient", transform).gameObject.AddComponent<RawImage>();
        UIKit.Stretch(gradient.rectTransform);
        gradient.texture = UISprites.Gradient(theme.backgroundTop, theme.backgroundBottom);
        gradient.raycastTarget = false;

        var party = theme.Party;
        blobs = new Piece[blobCount];
        for (int i = 0; i < blobCount; i++)
        {
            var img = UIKit.Rect("Blob", transform).gameObject.AddComponent<Image>();
            img.sprite = UISprites.Blob;
            img.raycastTarget = false;
            Color c = party[i % party.Length];
            c.a = 0.22f;
            img.color = c;
            float size = Random.Range(700f, 1200f);
            img.rectTransform.sizeDelta = new Vector2(size, size);
            img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            blobs[i] = new Piece
            {
                Rect  = img.rectTransform,
                Home  = new Vector2(Random.Range(-900f, 900f), Random.Range(-500f, 500f)),
                Drift = new Vector2(Random.Range(120f, 260f), Random.Range(80f, 200f)),
                Speed = Random.Range(0.04f, 0.09f),
                Phase = Random.Range(0f, 10f),
            };
        }

        confetti = new Piece[confettiCount];
        for (int i = 0; i < confettiCount; i++)
        {
            var img = UIKit.Rect("Confetti", transform).gameObject.AddComponent<Image>();
            img.sprite = i % 3 == 0 ? UISprites.Circle : UISprites.Rounded;
            img.type = i % 3 == 0 ? Image.Type.Simple : Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 8f;
            img.raycastTarget = false;
            Color c = party[i % party.Length];
            c.a = Random.Range(0.35f, 0.7f);
            img.color = c;
            float s = Random.Range(10f, 22f);
            img.rectTransform.sizeDelta = i % 3 == 0 ? new Vector2(s, s) : new Vector2(s * 0.6f, s * 1.6f);
            img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            confetti[i] = new Piece
            {
                Rect  = img.rectTransform,
                Home  = new Vector2(Random.Range(-1300f, 1300f), Random.Range(-800f, 800f)),
                Drift = new Vector2(Random.Range(20f, 60f), 0f),
                Speed = Random.Range(18f, 45f),
                Phase = Random.Range(0f, 10f),
                Spin  = Random.Range(-90f, 90f),
            };
        }
    }

    private void Update()
    {
        if (blobs == null) return;
        float t = Time.unscaledTime;
        foreach (var b in blobs)
            b.Rect.anchoredPosition = b.Home + new Vector2(
                Mathf.Sin(t * b.Speed * 6.28f + b.Phase) * b.Drift.x,
                Mathf.Cos(t * b.Speed * 5.1f + b.Phase) * b.Drift.y);

        float height = Mathf.Max(1080f, area.rect.height) + 200f;
        for (int i = 0; i < confetti.Length; i++)
        {
            var c = confetti[i];
            float y = Mathf.Repeat(c.Home.y + t * c.Speed + height * 0.5f, height) - height * 0.5f;
            float x = c.Home.x + Mathf.Sin(t * 0.7f + c.Phase) * c.Drift.x;
            c.Rect.anchoredPosition = new Vector2(x, y);
            c.Rect.localRotation = Quaternion.Euler(0f, 0f, c.Phase * 40f + t * c.Spin);
        }
    }
}
