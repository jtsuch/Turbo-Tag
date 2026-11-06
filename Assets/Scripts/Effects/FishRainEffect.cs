/// <summary>Chaos: fish rain down around you and every other player has to stare at you.</summary>
public class FishRainEffect : PlayerEffect
{
    public override string DisplayName => "Fish Rain";
    public override string Description => "Everyone's staring at you";
    public override bool   IsChaos     => true;

    protected override void OnEffectStart()
    {
        if (IsLocalEffect && DonutManager.Instance != null) DonutManager.Instance.SendFishRain(Duration);
    }

    protected override void OnEffectEnd() { }
}
