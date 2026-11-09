using UnityEngine;

// Effect cards for the tactical abilities (one per file is only required for scene components; these are added at
// runtime, so they can share a file).

/// <summary>Curse (Ink Blast): dark ink splattered over your screen, fading away.</summary>
public class InkedEffect : PlayerEffect
{
    public override string DisplayName => "Inked";
    public override string Description => "Ink in your eyes";
    public override bool   IsGood      => false;

    private Canvas canvas;
    private UnityEngine.UI.Image[] blots;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        canvas = UIKit.Canvas("Ink", 4);
        // A dark wash over the whole view, then big blots everywhere on top (the middle is all but black)
        wash = UIKit.Rect("Wash", canvas.transform).gameObject.AddComponent<UnityEngine.UI.Image>();
        UIKit.Stretch(wash.rectTransform);
        wash.raycastTarget = false;
        blots = new UnityEngine.UI.Image[16];
        for (int i = 0; i < blots.Length; i++)
        {
            var img = UIKit.Rect("Blot", canvas.transform).gameObject.AddComponent<UnityEngine.UI.Image>();
            img.sprite = UISprites.Blob;
            img.color = new Color(0.04f, 0.03f, 0.08f, 0.96f);
            img.raycastTarget = false;
            var r = img.rectTransform;
            // The first few sit over the middle; the rest spread right out to the edges
            r.anchorMin = r.anchorMax = i < 5 ? new Vector2(Random.Range(0.35f, 0.65f), Random.Range(0.35f, 0.65f))
                                               : new Vector2(Random.Range(0.02f, 0.98f), Random.Range(0.02f, 0.98f));
            float size = Random.Range(620f, 1100f);
            r.sizeDelta = new Vector2(size, size * Random.Range(0.7f, 1.2f));
            r.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            blots[i] = img;
        }
    }

    private void Update()
    {
        if (blots == null) return;
        float left = Duration > 0f ? Remaining / Duration : 0f;
        float alpha = Mathf.Clamp01(left * 1.6f) * 0.96f; // Holds, then drips away
        foreach (var b in blots) if (b != null) b.color = new Color(0.04f, 0.03f, 0.08f, alpha);
        if (wash != null) wash.color = new Color(0.04f, 0.03f, 0.08f, alpha * 0.82f);
    }

    private UnityEngine.UI.Image wash;

    protected override void OnEffectEnd()
    {
        if (canvas != null) Destroy(canvas.gameObject);
    }
}

/// <summary>Curse (Suppression): no abilities while it lasts.</summary>
public class SuppressedEffect : PlayerEffect
{
    public override string DisplayName => "Suppressed";
    public override string Description => "No abilities";
    public override bool   IsGood      => false;

    protected override void OnEffectStart() { if (IsLocalEffect) { player.AbilitiesLockedByEffect = true; player.Abilities.CancelAll(); } }
    protected override void OnEffectEnd()   { if (IsLocalEffect && player != null) player.AbilitiesLockedByEffect = false; }
}

/// <summary>Curse (Shock Dart, Slow Field, Snow Screen): slowed down.</summary>
public class ShockedEffect : PlayerEffect
{
    public override string DisplayName => "Slowed";
    public override string Description => "Moving at 60%";
    public override bool   IsGood      => false;

    protected override void OnEffectStart() { if (IsLocalEffect) player.Modifiers.Set(Stat.Speed, this, 0.6f); }
    protected override void OnEffectEnd()   { if (IsLocalEffect && player != null) player.Modifiers.Remove(Stat.Speed, this); }
}

/// <summary>Curse (Bulldozer): stunned: barely moving and shooting slowly.</summary>
public class StunnedEffect : PlayerEffect
{
    public const float Speed = 0.3f, FireRate = 0.35f;
    public override string DisplayName => "Stunned";
    public override string Description => "Slow to move and shoot";
    public override bool   IsGood      => false;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        player.Modifiers.Set(Stat.Speed, this, Speed);
        if (player.Gun != null) player.Gun.FireRateMultiplier *= FireRate;
        if (PlayerCam.Local != null) { PlayerCam.Local.AddShake(0.3f); PlayerCam.Local.Dizzy(Mathf.Max(Duration, 0.5f)); }

        // On screen: a pulsing yellow edge and little stars circling the top of the view
        screen = UIKit.Canvas("Stunned", 3);
        edge = UIKit.Rect("Edge", screen.transform).gameObject.AddComponent<UnityEngine.UI.Image>();
        UIKit.Stretch(edge.rectTransform);
        edge.sprite = CombatFeedback.VignetteSprite();
        edge.raycastTarget = false;
        stars = new UnityEngine.UI.Image[6];
        for (int i = 0; i < stars.Length; i++)
        {
            var s = UIKit.Rect("Star", screen.transform).gameObject.AddComponent<UnityEngine.UI.Image>();
            s.sprite = UISprites.Circle;
            s.raycastTarget = false;
            s.rectTransform.anchorMin = s.rectTransform.anchorMax = new Vector2(0.5f, 0.78f);
            stars[i] = s;
        }
    }

    private Canvas screen;
    private UnityEngine.UI.Image edge;
    private UnityEngine.UI.Image[] stars;

    private void Update()
    {
        if (screen == null) return;
        float t = Time.time;
        float fade = Duration > 0f ? Mathf.Clamp01(Remaining / 0.4f) : 1f;
        edge.color = new Color(1f, 0.85f, 0.25f, (0.3f + 0.12f * Mathf.Sin(t * 12f)) * fade);
        for (int i = 0; i < stars.Length; i++)
        {
            float a = t * 3.2f + i * Mathf.PI * 2f / stars.Length;
            float depth = (Mathf.Sin(a) + 1f) * 0.5f; // Nearer at the front of the circle: bigger and brighter
            var r = stars[i].rectTransform;
            r.anchoredPosition = new Vector2(Mathf.Cos(a) * 170f, Mathf.Sin(a) * 38f);
            float size = Mathf.Lerp(14f, 30f, depth);
            r.sizeDelta = new Vector2(size, size);
            r.localRotation = Quaternion.Euler(0f, 0f, t * 300f + i * 40f);
            stars[i].color = new Color(1f, 0.92f, 0.35f, Mathf.Lerp(0.45f, 1f, depth) * fade);
        }
    }

    protected override void OnEffectEnd()
    {
        if (screen != null) Destroy(screen.gameObject);
        if (!IsLocalEffect || player == null) return;
        player.Modifiers.Remove(Stat.Speed, this);
        if (player.Gun != null) player.Gun.FireRateMultiplier /= FireRate;
    }
}

/// <summary>Curse while standing in a Slow Field (no timer: held while inside).</summary>
public class SlowFieldEffect : PlayerEffect
{
    public override string DisplayName => "Slow Field";
    public override string Description => "Moving at 20% while inside";
    public override bool   IsGood      => false;

    protected override void OnEffectStart() { if (IsLocalEffect) player.Modifiers.Set(Stat.Speed, this, 0.2f); }
    protected override void OnEffectEnd()   { if (IsLocalEffect && player != null) player.Modifiers.Remove(Stat.Speed, this); }
}

/// <summary>Curse while wading through Snow Screen snow (no timer: held while on it).</summary>
public class SnowedEffect : PlayerEffect
{
    public override string DisplayName => "Snowed In";
    public override string Description => "Moving at 55% in the snow";
    public override bool   IsGood      => false;

    protected override void OnEffectStart() { if (IsLocalEffect) player.Modifiers.Set(Stat.Speed, this, 0.55f); }
    protected override void OnEffectEnd()   { if (IsLocalEffect && player != null) player.Modifiers.Remove(Stat.Speed, this); }
}

/// <summary>Boon (Heal Orb): health coming back over a few seconds.</summary>
public class HealingEffect : PlayerEffect
{
    public const float Total = 50f;
    public override string DisplayName => "Healing";
    public override string Description => $"+{Total:0} health";

    protected override void OnEffectStart() { }
    protected override void OnEffectEnd() { }

    private void Update()
    {
        if (!IsLocalEffect || player == null || player.Health == null || Duration <= 0f) return;
        player.Health.Heal(Total / Duration * Time.deltaTime);
    }
}

/// <summary>Boon (Shield Bubble): the next damage is soaked up by a shield.</summary>
public class BubbleShieldEffect : PlayerEffect
{
    public const float Amount = 50f;
    public override string DisplayName => "Armored";
    public override string Description => $"Absorbs {Amount:0} damage";

    private Canvas edge;
    private UnityEngine.UI.Image edgeImage;

    // While it's up: the player glows blue on everyone's screens (PlayerFlag.Barrier, PlayerHighlight), and a soft blue
    // edge shows on their own screen
    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        if (player.Health != null) player.Health.Shield = Amount;
        player.SetFlag(PlayerFlag.Barrier, true);
        edge = UIKit.Canvas("Shield Edge", 1);
        edgeImage = UIKit.Rect("Edge", edge.transform).gameObject.AddComponent<UnityEngine.UI.Image>();
        UIKit.Stretch(edgeImage.rectTransform);
        edgeImage.sprite = CombatFeedback.VignetteSprite();
        edgeImage.color = new Color(0.35f, 0.7f, 1f, 0.22f);
        edgeImage.raycastTarget = false;
    }

    protected override void OnEffectEnd()
    {
        if (edge != null) Destroy(edge.gameObject);
        if (!IsLocalEffect || player == null) return;
        if (player.Health != null) player.Health.Shield = 0f;
        player.SetFlag(PlayerFlag.Barrier, false);
    }

    private void Update()
    {
        if (!IsLocalEffect || player == null || player.Health == null) return;
        if (edgeImage != null) edgeImage.color = new Color(0.35f, 0.7f, 1f, 0.16f + 0.06f * Mathf.Sin(Time.time * 4f));
        if (player.Health.Shield <= 0f) { CancelEffect(); Destroy(this); } // Used up
    }
}
