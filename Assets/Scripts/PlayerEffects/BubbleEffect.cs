/// <summary>Boon: a free Shield bubble: other players' abilities can't touch you (synced; everyone sees it).</summary>
public class BubbleEffect : PlayerEffect
{
    public override string DisplayName => "Bubble";
    public override string Description => "Other players' abilities can't touch you";

    protected override void OnEffectStart()
    {
        if (IsLocalEffect) player.ShieldBubble = true;
    }

    protected override void OnEffectEnd()
    {
        if (IsLocalEffect && player != null) player.ShieldBubble = false;
    }
}
