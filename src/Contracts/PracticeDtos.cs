namespace Contracts;

public sealed record PracticeSectionDto(
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

public sealed record PracticeCatalogDto(
    CertificateOptionDto Certificate,
    IReadOnlyList<PracticeSectionDto> ListeningSections,
    IReadOnlyList<PracticeSectionDto> ReadingSections,
    IReadOnlyList<PracticeSectionDto> OtherSections,
    PracticeSectionDto? WeakestSection);

public sealed record StartPracticeRequest(
    int SectionId,
    int? QuestionCount = 20,
    string? QuestionSource = "all",
    string? FeedbackMode = "immediate");

public sealed record PracticeSessionDto(
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

public sealed record PracticeOptionDto(
    int OptionId,
    string Label,
    string Text);

public sealed record PracticeQuestionDto(
    int SessionId,
    string SectionName,
    int QuestionId,
    int DisplayOrder,
    int TotalQuestions,
    string Stem,
    string? CategoryName,
    string? Hint,
    bool HintUsed,
    IReadOnlyList<PracticeOptionDto> Options,
    int? SelectedOptionId,
    bool? IsCorrect,
    int? CorrectOptionId,
    string? Explanation,
    string? Transcript,
    string? Passage,
    string? AudioPath,
    string? ImagePath,
    string? ImageAlt);

public sealed record SubmitPracticeAnswerRequest(int? SelectedOptionId);

public sealed record GradedAnswerDto(
    bool? IsCorrect,
    int? SelectedOptionId,
    int? CorrectOptionId,
    string? Explanation,
    string? Transcript,
    bool IsSessionFinished,
    int? NextQuestionDisplayOrder);

public sealed record QuestionOutcomeDto(
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

public sealed record PracticeSummaryDto(
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
    IReadOnlyList<QuestionOutcomeDto> Questions);

public sealed record RevealHintResponse(string? Hint, bool HintUsed);
