/// <summary>Throws a rock. At most 5 of a player's rocks lie about at once (the oldest goes).</summary>
[AbilityInfo("A plain rock. Bonk. One at a time.")]
public class Rock : ThrowAbility
{
    public override int MaxOut => 1;   // Justin, Nov 14: one rock at a time (1 use, 2 s cooldown)
}
