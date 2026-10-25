namespace Contracts;

public sealed record PracticeSectionDto(
    int SectionId,
    string SkillCode,
    string SkillName,
    ModalityDto Modality,
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
    PracticeSectionDto? WeakestSection,
    PracticeSessionDto? UnfinishedSession = null);

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
    ModalityDto Modality,
    int QuestionCount,
    PracticeStatusDto Status,
    int CorrectCount,
    QuestionSourceDto QuestionSource,
    FeedbackModeDto FeedbackMode,
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
    string? ImageAlt,
    bool IsSaved = false,
    int? AudioStartMs = null,
    int? AudioEndMs = null,
    string QuestionType = "mcq",
    int? MaxSelections = null,
    bool AllowNumber = true,
    string? AcceptedAnswersJson = null,
    string? RequiredKeywords = null,
    int? MinWords = null,
    int? MaxWords = null,
    string? ResponseText = null,
    IReadOnlyList<int>? SelectedOptionIds = null,
    string? SelectedOptionIdsJson = null,
    IReadOnlyList<int>? CorrectOptionIds = null);

public sealed record SubmitPracticeAnswerRequest(
    int? SelectedOptionId,
    string? ResponseText = null,
    string? SelectedOptionIdsJson = null,
    IReadOnlyList<int>? SelectedOptionIds = null);

public sealed record GradedAnswerDto(
    bool? IsCorrect,
    int? SelectedOptionId,
    int? CorrectOptionId,
    string? Explanation,
    string? Transcript,
    bool IsSessionFinished,
    int? NextQuestionDisplayOrder,
    bool IsSaved = false,
    string? AcceptedAnswersJson = null,
    IReadOnlyList<int>? CorrectOptionIds = null,
    string? ResponseText = null,
    string? SelectedOptionIdsJson = null);

public sealed record PracticeNavigatorItemDto(
    int QuestionId,
    int DisplayOrder,
    bool IsAnswered,
    bool? IsCorrect,
    bool IsSaved,
    bool IsCurrent);

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
    string? Explanation,
    string? AudioPath = null,
    int? AudioStartMs = null,
    int? AudioEndMs = null,
    string? Transcript = null,
    string QuestionType = "mcq",
    string? ResponseText = null,
    string? SelectedOptionIdsJson = null,
    string? AcceptedAnswersJson = null);

public sealed record PracticeSessionTakeDto(
    PracticeSessionDto Session,
    PracticeQuestionDto Question,
    IReadOnlyList<PracticeQuestionDto> Questions,
    IReadOnlyList<PracticeNavigatorItemDto> Navigator);

public sealed record PracticeSummaryDto(
    int SessionId,
    string SectionCode,
    string SectionName,
    ModalityDto Modality,
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
