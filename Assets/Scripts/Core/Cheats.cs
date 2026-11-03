/// <summary>
/// Shared identity for everything the Cheats tab changes, so it can all be undone at once
/// when the host turns cheats off (see AbilityHandler).
/// </summary>
public static class Cheats
{
    /// <summary>Source key for stat modifiers set from the Cheats tab.</summary>
    public static readonly object ModifierSource = new();

    public static bool Enabled => MatchRules.CheatsEnabled;
}
