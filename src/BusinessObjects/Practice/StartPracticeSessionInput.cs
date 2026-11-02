using System.ComponentModel.DataAnnotations;

namespace BusinessObjects;

/// <summary>
/// #155: launching a drill no longer takes a question count or a source - the whole point is that the
/// learner does not choose what to revise. A section is all that is left; the ten questions come from
/// <see cref="SmartPracticePlanner"/> and the cooldown.
/// </summary>
public sealed record StartPracticeSessionInput
{
    public StartPracticeSessionInput(int sectionId)
    {
        SectionId = sectionId;
    }

    [Range(1, int.MaxValue, ErrorMessage = "A valid section must be chosen.")]
    public int SectionId { get; init; }
}
