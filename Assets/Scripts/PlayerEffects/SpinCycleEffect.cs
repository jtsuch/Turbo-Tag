using UnityEngine;

/// <summary>Curse: every few seconds the view whips round a full turn.</summary>
public class SpinCycleEffect : PlayerEffect
{
    private float nextSpin;

    public override string DisplayName => "Spin Cycle";
    public override string Description => "Your view keeps spinning";
    public override bool   IsGood      => false;

    protected override void OnEffectStart() => nextSpin = Time.time + 1f;
    protected override void OnEffectEnd() { }

    private void Update()
    {
        if (!IsLocalEffect || Time.time < nextSpin || PlayerCam.Local == null) return;
        nextSpin = Time.time + Random.Range(3f, 4.5f);
        PlayerCam.Local.SpinAround(360f / 540f); // One full turn
        Sfx.Here(SfxKind.Whoosh, 0.5f);
    }
}
