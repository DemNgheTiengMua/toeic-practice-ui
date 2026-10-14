using System.ComponentModel.DataAnnotations;

namespace BusinessObjects;

/// <summary>One section available for practice under the learner's active certificate.</summary>
public sealed record PracticeSectionItem(
    int SectionId,
    string SkillCode,
    string SkillName,
    string Modality,
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
    PracticeSectionItem? WeakestSection);

/// <summary>Options for launching a new practice drill.</summary>
public sealed record StartPracticeSessionInput
{
    public StartPracticeSessionInput(int sectionId, int? questionCount = 20, string questionSource = "all", string feedbackMode = "immediate")
    {
        SectionId = sectionId;
        QuestionCount = questionCount ?? 20;
        QuestionSource = questionSource ?? "all";
        FeedbackMode = feedbackMode ?? "immediate";
    }

    [Range(1, int.MaxValue, ErrorMessage = "A valid section must be chosen.")]
    public int SectionId { get; init; }

    [Range(1, 200, ErrorMessage = "Question count must be between 1 and 200.")]
    public int QuestionCount { get; init; }

    [RegularExpression("^(all|wrong_before|unseen)$", ErrorMessage = "Question source must be 'all', 'wrong_before', or 'unseen'.")]
    public string QuestionSource { get; init; }

    [RegularExpression("^(immediate|end)$", ErrorMessage = "Feedback mode must be 'immediate' or 'end'.")]
    public string FeedbackMode { get; init; }
}

public sealed record PracticeSessionInfo(
    int SessionId,
    int SectionId,
    string SectionCode,
    string SectionName,
    string Modality,
    int QuestionCount,
    string Status,
    int CorrectCount,
    string QuestionSource,
    string FeedbackMode,
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
    string? ImageAlt);

public sealed record SubmitPracticeAnswerInput(int? SelectedOptionId);

public sealed record GradedPracticeAnswerResult(
    bool? IsCorrect,
    int? SelectedOptionId,
    int? CorrectOptionId,
    string? Explanation,
    string? Transcript,
    bool IsSessionFinished,
    int? NextQuestionDisplayOrder);

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
    string? Explanation);

public sealed record PracticeSummary(
    int SessionId,
    string SectionCode,
    string SectionName,
    string Modality,
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
