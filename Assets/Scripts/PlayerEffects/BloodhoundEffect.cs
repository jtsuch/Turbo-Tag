using UnityEngine;

/// <summary>Boon (chasing): glowing footprints show where everyone has been over the last few seconds.</summary>
public class BloodhoundEffect : PlayerEffect
{
    public override string DisplayName => "Bloodhound";
    public override string Description => "See where everyone walked";
    public override bool   IsGood      => true;
    public override bool   IsChaos     => false;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;

    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;

    }

    private float next;

    private void Update()
    {
        if (!IsLocalEffect || Time.time < next || TrailHistory.Instance == null) return;
        next = Time.time + 0.6f;
        foreach (var p in Player.All)
        {
            if (p == null || p == player) continue;
            Color c = PlayerRoles.Color(p.Role);
            var trail = TrailHistory.Instance.Of(p.ActorNumber);
            for (int i = 0; i < trail.Count; i += 2)
            {
                float age = Time.time - trail[i].time;
                Vfx.Burst(trail[i].at + Vector3.up * 0.15f, new Color(c.r, c.g, c.b, Mathf.Lerp(0.9f, 0.25f, age / TrailHistory.Keep)), 1, 0.05f, 0.35f, 0.7f);
            }
        }
    }
}
