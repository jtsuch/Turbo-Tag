using System.Collections;
using UnityEngine;

/// <summary>
/// Throwaway local gun effects built in code (no assets needed): bullet tracers (a fading line) and
/// muzzle flashes (a short point light). One hidden runner object drives their fades.
/// </summary>
public class GunFx : MonoBehaviour
{
    private const float TracerSeconds = 0.08f;
    private const float FlashSeconds  = 0.05f;

    private static GunFx    runner;
    private static Material tracerMaterial;

    private static GunFx Runner
    {
        get
        {
            if (runner == null) runner = new GameObject("GunFx").AddComponent<GunFx>();
            return runner;
        }
    }

    /// <summary>A bright line from <paramref name="from"/> to <paramref name="to"/> that fades out quickly.</summary>
    public static void Tracer(Transform from, Vector3 to) => Tracer(from.position, to);

    public static void Tracer(Vector3 from, Vector3 to)
    {
        if (tracerMaterial == null) tracerMaterial = new Material(Shader.Find("Sprites/Default"));

        var go   = new GameObject("Tracer");
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial    = tracerMaterial;
        line.positionCount     = 2;
        line.SetPosition(0, from);
        line.SetPosition(1, to);
        line.widthCurve        = new AnimationCurve(new Keyframe(0f, 0.03f), new Keyframe(1f, 0.06f));
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows    = false;
        Runner.StartCoroutine(Fade(line));
    }

    /// <summary>A short warm flash of light at <paramref name="position"/>.</summary>
    public static void Flash(Vector3 position)
    {
        var go    = new GameObject("MuzzleFlash");
        go.transform.position = position;
        var light = go.AddComponent<Light>();
        light.type      = LightType.Point;
        light.color     = new Color(1f, 0.75f, 0.35f);
        light.range     = 6f;
        light.intensity = 4f;
        light.shadows   = LightShadows.None;
        Destroy(go, FlashSeconds);
    }

    private static IEnumerator Fade(LineRenderer line)
    {
        Color start = new(1f, 0.9f, 0.55f, 0.9f);
        float t = 0f;
        while (t < TracerSeconds && line != null)
        {
            Color c = start;
            c.a *= 1f - t / TracerSeconds;
            line.startColor = c;
            line.endColor   = c;
            t += Time.deltaTime;
            yield return null;
        }
        if (line != null) Destroy(line.gameObject);
    }
}
