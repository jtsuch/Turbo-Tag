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
        blots = new UnityEngine.UI.Image[7];
        for (int i = 0; i < blots.Length; i++)
        {
            var img = UIKit.Rect("Blot", canvas.transform).gameObject.AddComponent<UnityEngine.UI.Image>();
            img.sprite = UISprites.Blob;
            img.color = new Color(0.04f, 0.03f, 0.08f, 0.96f);
            img.raycastTarget = false;
            var r = img.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(Random.Range(0.25f, 0.75f), Random.Range(0.25f, 0.75f));
            float size = Random.Range(380f, 720f);
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
    }

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
    public override string DisplayName => "Shield Bubble";
    public override string Description => $"Absorbs {Amount:0} damage";

    protected override void OnEffectStart() { if (IsLocalEffect && player.Health != null) player.Health.Shield = Amount; }
    protected override void OnEffectEnd()   { if (IsLocalEffect && player != null && player.Health != null) player.Health.Shield = 0f; }

    private void Update()
    {
        if (IsLocalEffect && player != null && player.Health != null && player.Health.Shield <= 0f) { CancelEffect(); Destroy(this); } // Used up
    }
}
