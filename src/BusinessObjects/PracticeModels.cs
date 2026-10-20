using System.ComponentModel.DataAnnotations;

namespace BusinessObjects;

/// <summary>One section available for practice under the learner's active certificate.</summary>
public sealed record PracticeSectionItem(
    int SectionId,
    string SkillCode,
    string SkillName,
    Modality Modality,
    string Code,
    string Name,
    int? OptionCount,
    int ExpectedQuestionCount,
    int AvailableQuestionCount,
    int? AccuracyPercentage,
    int AnsweredCount,
    int DisplayOrder);

/// <summary>Sections grouped by modality for the practice selection screen.</summary>
public sealed record PracticeSectionsView(
    CertificateOption Certificate,
    IReadOnlyList<PracticeSectionItem> ListeningSections,
    IReadOnlyList<PracticeSectionItem> ReadingSections,
    IReadOnlyList<PracticeSectionItem> OtherSections,
    PracticeSectionItem? WeakestSection,
    PracticeSessionInfo? UnfinishedSession = null);

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

public sealed record PracticeSessionInfo(
    int SessionId,
    int SectionId,
    string SectionCode,
    string SectionName,
    Modality Modality,
    int QuestionCount,
    PracticeStatus Status,
    int CorrectCount,
    QuestionSource QuestionSource,
    FeedbackMode FeedbackMode,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt);

public sealed record PracticeQuestionOption(
    int OptionId,
    string Label,
    string Text);

public sealed record PracticeQuestionView(
    int SessionId,
    string SectionName,
    int QuestionId,
    int DisplayOrder,
    int TotalQuestions,
    string Stem,
    string? CategoryName,
    string? Hint,
    bool HintUsed,
    IReadOnlyList<PracticeQuestionOption> Options,
    int? SelectedOptionId,
    bool? IsCorrect,
    int? CorrectOptionId,
    string? Explanation,
    string? Transcript,
    string? Passage,
    string? AudioPath,
    string? ImagePath,
    string? ImageAlt,
    bool IsSaved = false,
    int? AudioStartMs = null,
    int? AudioEndMs = null);

public sealed record SubmitPracticeAnswerInput(int? SelectedOptionId);

public sealed record GradedPracticeAnswerResult(
    bool? IsCorrect,
    int? SelectedOptionId,
    int? CorrectOptionId,
    string? Explanation,
    string? Transcript,
    bool IsSessionFinished,
    int? NextQuestionDisplayOrder,
    bool IsSaved = false);

public sealed record QuestionOutcome(
    int DisplayOrder,
    int QuestionId,
    string? CategoryName,
    string Stem,
    string? SelectedOptionLabel,
    string? SelectedOptionText,
    string? CorrectOptionLabel,
    string? CorrectOptionText,
    bool? IsCorrect,
    bool HintUsed,
    string? Explanation,
    string? AudioPath = null,
    int? AudioStartMs = null,
    int? AudioEndMs = null,
    string? Transcript = null);

public sealed record PracticeSummary(
    int SessionId,
    string SectionCode,
    string SectionName,
    Modality Modality,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int TotalQuestions,
    int AnsweredCount,
    int CorrectCount,
    int IncorrectCount,
    int SkippedCount,
    int AccuracyPercentage,
    int HintUsedCount,
    int CorrectWithHintCount,
    int IncorrectWithHintCount,
    IReadOnlyList<QuestionOutcome> Questions);
