using System.Text.Json.Serialization;

namespace Contracts;

public record WritingInstructionsDto(
    int ExamId,
    int CertId,
    string CertCode,
    string ExamName,
    string? Description,
    int DurationMinutes,
    int QuestionCount,
    int CreditCost,
    int CreditBalance,
    int? ActiveAttemptId,
    IReadOnlyList<WritingSectionInfoDto> Sections
);

public record WritingSectionInfoDto(
    int SectionId,
    string Code,
    string Name,
    int ExpectedQuestionCount,
    int? ResponseSeconds,
    int DisplayOrder
);

public record StartWritingAttemptResponse(
    int AttemptId,
    int ExamId,
    AttemptStatusDto Status,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    int RemainingSeconds,
    bool IsAutoSubmitted
);

public record WritingTakeDto(
    int AttemptId,
    int ExamId,
    string ExamName,
    int CertId,
    string CertCode,
    AttemptStatusDto Status,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    int RemainingSeconds,
    int CurrentQuestionId,
    int CurrentQuestionIndex,
    int TotalQuestions,
    WritingQuestionDto Question,
    IReadOnlyList<WritingQuestionNavDto> Questions
);

public record WritingQuestionDto(
    int QuestionId,
    int DisplayOrder,
    string Stem,
    int? MinWords,
    int? MaxWords,
    int? ResponseSeconds,
    string? Hint,
    string SectionCode,
    string SectionName,
    string? GroupImagePath,
    string? GroupImageAlt,
    string? ResponseText,
    DateTimeOffset? DraftSavedAt,
    FreeResponseStatusDto Status
);

public record WritingQuestionNavDto(
    int QuestionId,
    int DisplayOrder,
    string SectionCode,
    string SectionName,
    bool IsAnswered,
    bool IsCurrent
);

public record SaveWritingDraftRequest(
    int QuestionId,
    string? ResponseText
);

public record SaveWritingDraftResponse(
    int FreeResponseId,
    int QuestionId,
    DateTimeOffset SavedAt,
    int WordCount
);

public record SubmitWritingResponse(
    int AttemptId,
    AttemptStatusDto Status,
    DateTimeOffset SubmittedAt
);

public record WritingGradingPendingDto(
    int AttemptId,
    int ExamId,
    string ExamName,
    string CertCode,
    AttemptStatusDto AttemptStatus,
    int TotalResponses,
    int GradedResponses,
    int FailedResponses,
    int PendingResponses,
    bool CanRetry,
    string? FailureReason,
    IReadOnlyList<FreeResponseGradingStatusDto> Items
);

public record FreeResponseGradingStatusDto(
    int FreeResponseId,
    int QuestionId,
    int DisplayOrder,
    AiGradingStatusDto Status,
    int AttemptCount,
    string? FailureReason
);

public record WritingReviewDto(
    int AttemptId,
    int ExamId,
    string ExamName,
    string CertCode,
    DateTimeOffset? SubmittedAt,
    int TotalQuestions,
    int AnsweredQuestions,
    decimal TotalScore,
    decimal MaxScore,
    IReadOnlyList<WritingQuestionReviewItemDto> Questions
);

public record WritingQuestionReviewItemDto(
    int QuestionId,
    int DisplayOrder,
    string SectionName,
    string Stem,
    string? ResponseText,
    int WordCount,
    decimal TotalScore,
    decimal MaxScore,
    IReadOnlyList<WritingCriterionScoreItemDto> Criteria
);

public record WritingCriterionScoreItemDto(
    int CriterionId,
    string Code,
    string Name,
    decimal Score,
    decimal MaxScore,
    string? Comment
);
