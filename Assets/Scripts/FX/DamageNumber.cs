using TMPro;
using UnityEngine;

/// <summary>
/// A damage number that pops out of a hit on the shooter's screen, drifts up and fades (yellow; red and bigger for
/// headshots). Local only, drawn on top of everything. Quick hits on the same spot add up into one number instead of stacking.
/// </summary>
public class DamageNumber : MonoBehaviour
{
    private const float Life = 0.8f;

    private static DamageNumber last;
    private TextMeshPro text;
    private Vector3     velocity;
    private float       born, total;
    private bool        headshot;

    public static void Show(Vector3 position, float damage, bool headshot)
    {
        // Sprayed into the same target: keep counting on the newest number
        if (last != null && Time.time - last.born < 0.35f && Vector3.Distance(last.transform.position, position) < 1.5f)
        {
            last.Add(damage, headshot);
            return;
        }

        var go = new GameObject("Damage Number");
        var number = go.AddComponent<DamageNumber>();
        number.text = go.AddComponent<TextMeshPro>(); // Swaps the Transform for a RectTransform: position it after
        go.transform.position = position + Random.insideUnitSphere * 0.15f;
        number.text.font = UIKit.FontAsset(UIKit.Font.Display);
        number.text.alignment = TextAlignmentOptions.Center;
        number.text.textWrappingMode = TextWrappingModes.NoWrap;
        number.text.fontSharedMaterial = OnTop();
        number.text.fontSize = 10f; // About a metre tall at scale 1 (the scale keeps it a steady size on screen)
        number.velocity = new Vector3(Random.Range(-0.6f, 0.6f), 2.2f, Random.Range(-0.6f, 0.6f));
        number.born = Time.time;
        number.Add(damage, headshot);
        last = number;
    }

    private static Material onTop;

    // Drawn over everything (walls, players, fire) and last, so the shooter always sees the number
    private static Material OnTop()
    {
        if (onTop != null) return onTop;
        var font = UIKit.FontAsset(UIKit.Font.Display);
        onTop = new Material(font.material) { name = "Damage Number (on top)" };
        var overlay = Shader.Find("TextMeshPro/Distance Field Overlay");
        if (overlay != null) onTop.shader = overlay;
        onTop.SetFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
        onTop.renderQueue = 4000;
        return onTop;
    }

    private void Add(float damage, bool head)
    {
        total += damage;
        headshot |= head;
        born = Time.time; // A fresh pop
        text.text = Mathf.RoundToInt(total).ToString();
        text.color = headshot ? new Color(1f, 0.3f, 0.3f) : new Color(1f, 0.85f, 0.3f);
    }

    private void LateUpdate()
    {
        float age = Time.time - born;
        if (age > Life) { Destroy(gameObject); return; }

        transform.position += velocity * Time.deltaTime;
        velocity *= 1f - 3f * Time.deltaTime;

        // Face the camera, and keep the same size on screen at any distance
        var cam = PlayerCam.Local != null ? PlayerCam.Local.Camera : Camera.main;
        if (cam != null)
        {
            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
            float distance = Vector3.Distance(cam.transform.position, transform.position);
            float pop = age < 0.12f ? 1f + (0.12f - age) * 5f : 1f;
            float size = (headshot ? 1.4f : 1f) * pop * distance * 0.06f;
            transform.localScale = Vector3.one * size;
        }
        text.alpha = Mathf.Clamp01((Life - age) / 0.25f);
    }
}
