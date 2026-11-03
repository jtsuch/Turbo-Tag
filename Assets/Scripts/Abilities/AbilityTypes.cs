/// <summary>The four loadout categories. Each player equips exactly one ability per slot.</summary>
public enum AbilitySlot { Basic, Quick, Throw, Trap }

/// <summary>Key transitions routed from InputHandler to an ability.</summary>
public enum AbilityInputEvent { Down, Up, Held }
