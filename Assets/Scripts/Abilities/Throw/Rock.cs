/// <summary>Throws a rock. At most 5 of a player's rocks lie about at once (the oldest goes).</summary>
[AbilityInfo("A plain rock. Bonk.")]
public class Rock : ThrowAbility
{
    public override int MaxOut => 5;
}
