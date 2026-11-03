using System;

/// <summary>
/// Player-facing text for an ability, shown in Pregame (and anywhere else abilities are listed).
/// The display name comes from the class name ("ZipGrapple" → "Zip Grapple") unless set here.
/// Usage: [AbilityInfo("Hold to glide.")] public class Flappy : BasicAbility { … }
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class AbilityInfoAttribute : Attribute
{
    public readonly string Description;
    public string DisplayName { get; set; }

    public AbilityInfoAttribute(string description) => Description = description;
}
