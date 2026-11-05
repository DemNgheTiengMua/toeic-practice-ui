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
    }
}
