using System.ComponentModel.DataAnnotations;

namespace BusinessObjects;

/// <summary>
/// #155: launching a drill no longer takes a question count or a source - the whole point is that the
/// learner does not choose what to revise. A section and a feedback mode are all that is left; the ten
/// questions come from <see cref="SmartPracticePlanner"/> and the cooldown.
/// </summary>
public sealed record StartPracticeSessionInput
{
    public const string FeedbackModeError = "Feedback mode must be 'immediate' or 'end'.";

    public StartPracticeSessionInput(int sectionId, FeedbackMode feedbackMode = FeedbackMode.Immediate)
    {
        SectionId = sectionId;
        FeedbackMode = feedbackMode;
    }

    /// <summary>From the wire's raw codes; null means the default. An unknown code fails validation with
    /// the same message the old string check gave.</summary>
    public static StartPracticeSessionInput FromCodes(int sectionId, string? feedbackMode)
    {
        var mode = FeedbackMode.Immediate;
        if (feedbackMode is not null && !EnumCodes.TryParse(feedbackMode, out mode))
        {
            throw new ValidationException(FeedbackModeError);
        }

        return new StartPracticeSessionInput(sectionId, mode);
    }

    [Range(1, int.MaxValue, ErrorMessage = "A valid section must be chosen.")]
    public int SectionId { get; init; }

    [EnumDataType(typeof(FeedbackMode), ErrorMessage = FeedbackModeError)]
    public FeedbackMode FeedbackMode { get; init; }
}
