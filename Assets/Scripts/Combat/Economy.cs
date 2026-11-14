/// <summary>
/// The shop's numbers for modes with an economy (<see cref="GameMode.UsesEconomy"/>: Search &amp; Destroy), copied from
/// Valorant for a start (Justin will balance them): credits per round, per kill, per plant; shields; ability upgrades.
/// Gun prices live on each <see cref="GunInfo"/>.
/// </summary>
public static class Economy
{
    public const int Start = 800;                // Every half starts here (a pistol round)
    public const int Max   = 9000;
    public const int Kill  = 200;
    public const int Win   = 3000;
    public const int Plant = 300;                // Every attacker, when the bomb goes down
    public static readonly int[] Loss = { 1900, 2400, 2900 }; // 1st, 2nd, 3rd+ loss in a row

    /// <summary>Bought shields (soaked before health, never regenerating).</summary>
    public static readonly (string name, float amount, int price)[] Shields =
    {
        ("Light Shield", 25f, 400),
        ("Full Shield",  50f, 1000),
    };

    /// <summary>
    /// Ability upgrades: every ability starts each round with one use (Basic abilities: their bar), and each can be
    /// upgraded twice for the half: +1 use for Quick/Throw/Trap abilities, +40% bar for Basic ones.
    /// </summary>
    public const int MaxUpgrades = 2;
    public static readonly int[] UpgradePrice = { 250, 400 };
    public const float DurationPerUpgrade = 0.4f;

    public static string Format(int credits) => $"${credits:#,0}";
}
