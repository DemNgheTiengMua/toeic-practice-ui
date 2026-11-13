using BusinessObjects;

namespace Services;

/// <summary>
/// #289: retake credit prices for placement sittings. First placement per certificate per
/// learner is free; subsequent sittings charge the price of the chosen <see cref="PlacementScope"/>.
/// </summary>
public sealed class PlacementOptions
{
    public const string SectionName = "Placement";

    /// <summary>Default retake price for a full four-skill placement sitting.</summary>
    public int AllSkillsRetakeCredits { get; init; } = 5;

    /// <summary>Default retake price for a two-skill Listening &amp; Reading placement sitting.</summary>
    public int ListeningAndReadingRetakeCredits { get; init; } = 3;

    public int PriceFor(PlacementScope scope) => scope switch
    {
        PlacementScope.AllSkills => AllSkillsRetakeCredits,
        PlacementScope.ListeningAndReading => ListeningAndReadingRetakeCredits,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown placement scope.")
    };
}
