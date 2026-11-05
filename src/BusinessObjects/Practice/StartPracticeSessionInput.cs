using System.ComponentModel.DataAnnotations;

namespace BusinessObjects;

/// <summary>
/// #155: launching a drill no longer takes a question count or a source - the whole point is that the
/// learner does not choose what to revise. A section is all that is left; the ten questions come from
/// <see cref="SmartPracticePlanner"/> and the cooldown.
/// <para>
/// #187: a drill is now either a section or a pool cell. A cell drill has no exam and no section
/// behind it - it draws from the items authored at (topic, level) - so it is started by that pair
/// instead. Exactly one of the two shapes is supplied; the database refuses anything else, and
/// <see cref="Validate"/> says so before a query runs.
/// </para>
/// </summary>
public sealed record StartPracticeSessionInput : IValidatableObject
{
    public StartPracticeSessionInput(int sectionId)
    {
        SectionId = sectionId;
    }

    /// <summary>#187: start a drill against a pool cell rather than a section.</summary>
    public StartPracticeSessionInput(int topicId, CefrLevel level)
    {
        TopicId = topicId;
        CefrLevel = level;
    }

    public int? SectionId { get; init; }

    /// <summary>#187: the topic of the cell drilled. NULL for a section drill.</summary>
    public int? TopicId { get; init; }

    /// <summary>#187: the level of the cell drilled. NULL for a section drill.</summary>
    public CefrLevel? CefrLevel { get; init; }

    /// <summary>
    /// #190: which tier of stock this drill may draw. Selection filters on lifecycle <em>and</em> tier
    /// (CNT-42), so a request that named only the cell would draw demo stock into a production drill
    /// the moment the two tiers share a cell - and they do share every cell, by construction.
    /// <para>
    /// Defaults to <c>production</c> because that is the wrong-value-safe direction: a caller that
    /// forgets it draws reviewed stock, and the demo tier is the one that decommissions in a single
    /// bulk state change (CNT-46). #245's account-level flag is what will set it for a demo learner;
    /// until then nothing but a test asks for demo.
    /// </para>
    /// </summary>
    public string ContentTier { get; init; } = ContentTiers.Production;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var hasSection = SectionId is not null;
        var hasCell = TopicId is not null && CefrLevel is not null;

        if (hasSection == hasCell)
        {
            yield return new ValidationResult(
                "A drill is started against either a section or a cell (topic and level), not both and not neither.",
                [nameof(SectionId), nameof(TopicId), nameof(CefrLevel)]);
            yield break;
        }

        if (hasSection && SectionId < 1)
        {
            yield return new ValidationResult("A valid section must be chosen.", [nameof(SectionId)]);
        }

        if (TopicId < 1)
        {
            yield return new ValidationResult("A valid topic must be chosen.", [nameof(TopicId)]);
        }

        if (!ContentTiers.IsKnown(ContentTier))
        {
            yield return new ValidationResult(
                $"'{ContentTier}' is not a content tier. An item is either {ContentTiers.Production} or {ContentTiers.Demo}.",
                [nameof(ContentTier)]);
        }
    }
}
