using UnityEngine;

/// <summary>Boon (gun modes): every shot that lands heals you (more for headshots).</summary>
public class VampireEffect : PlayerEffect
{
    [SerializeField] private float healPerHit = 8f;

    public override string DisplayName => "Vampire";
    public override string Description => "Hits heal you";

    protected override void OnEffectStart()
    {
        if (IsLocalEffect) PlayerGun.LocalHit += HandleHit;
    }

    protected override void OnEffectEnd() => PlayerGun.LocalHit -= HandleHit;

    private void HandleHit(bool headshot)
    {
        if (player == null || player.Health == null) return;
        player.Health.Heal(headshot ? healPerHit * 2f : healPerHit);
        Vfx.Burst(player.transform.position + Vector3.up, new Color(1f, 0.2f, 0.3f), 6, 2f, 0.15f, 0.4f);
    }
}
