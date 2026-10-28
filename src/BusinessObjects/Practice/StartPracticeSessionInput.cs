using System.ComponentModel.DataAnnotations;

namespace BusinessObjects;

/// <summary>Options for launching a new practice drill.</summary>
public sealed record StartPracticeSessionInput
{
    public const string QuestionSourceError = "Question source must be 'all', 'wrong_before', or 'unseen'.";
    public const string FeedbackModeError = "Feedback mode must be 'immediate' or 'end'.";

    public StartPracticeSessionInput(
        int sectionId,
        int? questionCount = 20,
        QuestionSource questionSource = QuestionSource.All,
        FeedbackMode feedbackMode = FeedbackMode.Immediate)
    {
        SectionId = sectionId;
        QuestionCount = questionCount ?? 20;
        QuestionSource = questionSource;
        FeedbackMode = feedbackMode;
    }

    /// <summary>From the wire's raw codes; null means the default. An unknown code fails
    /// validation with the same message the old string check gave.</summary>
    public static StartPracticeSessionInput FromCodes(int sectionId, int? questionCount, string? questionSource, string? feedbackMode)
    {
        var source = QuestionSource.All;
        if (questionSource is not null && !EnumCodes.TryParse(questionSource, out source))
        {
            throw new ValidationException(QuestionSourceError);
        }

        var mode = FeedbackMode.Immediate;
        if (feedbackMode is not null && !EnumCodes.TryParse(feedbackMode, out mode))
        {
            throw new ValidationException(FeedbackModeError);
        }

        return new StartPracticeSessionInput(sectionId, questionCount, source, mode);
    }

    [Range(1, int.MaxValue, ErrorMessage = "A valid section must be chosen.")]
    public int SectionId { get; init; }

    [Range(1, 200, ErrorMessage = "Question count must be between 1 and 200.")]
    public int QuestionCount { get; init; }

    [EnumDataType(typeof(QuestionSource), ErrorMessage = QuestionSourceError)]
    public QuestionSource QuestionSource { get; init; }

    [EnumDataType(typeof(FeedbackMode), ErrorMessage = FeedbackModeError)]
    public FeedbackMode FeedbackMode { get; init; }
}
