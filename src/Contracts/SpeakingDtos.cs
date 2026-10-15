using System;
using System.Collections.Generic;

namespace Contracts;

public record SpeakingInstructionsDto(
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
    IReadOnlyList<SpeakingSectionInfoDto> Sections
);

public record SpeakingSectionInfoDto(
    int SectionId,
    string Code,
    string Name,
    int ExpectedQuestionCount,
    int? PrepSeconds,
    int? ResponseSeconds,
    int DisplayOrder
);

public record StartSpeakingAttemptResponse(
    int AttemptId,
    int ExamId,
    AttemptStatusDto Status,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    int RemainingSeconds,
    bool IsAutoSubmitted
);

public record SpeakingTakeDto(
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
    int SavedCount,
    SpeakingQuestionDto Question,
    IReadOnlyList<SpeakingQuestionNavDto> Questions
);

public record SpeakingQuestionDto(
    int QuestionId,
    int DisplayOrder,
    string Stem,
    int PrepSeconds,
    int ResponseSeconds,
    string? Hint,
    string SectionCode,
    string SectionName,
    string? GroupImagePath,
    string? GroupImageAlt,
    string? GroupAudioPath,
    string? AudioPath,
    int? DurationMs,
    string? ResponseText,
    DateTimeOffset? DraftSavedAt,
    FreeResponseStatusDto Status
);

public record SpeakingQuestionNavDto(
    int QuestionId,
    int DisplayOrder,
    string SectionCode,
    string SectionName,
    bool IsAnswered,
    bool IsCurrent
);

public record UploadSpeakingAudioResponse(
    int FreeResponseId,
    int QuestionId,
    string AudioPath,
    int? DurationMs,
    DateTimeOffset SavedAt
);

public record SubmitSpeakingResponse(
    int AttemptId,
    AttemptStatusDto Status,
    DateTimeOffset SubmittedAt
);

public record SpeakingGradingPendingDto(
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

public record SpeakingReviewDto(
    int AttemptId,
    int ExamId,
    string ExamName,
    string CertCode,
    DateTimeOffset? SubmittedAt,
    int TotalQuestions,
    int AnsweredQuestions,
    decimal TotalScore,
    decimal MaxScore,
    int? ScaledScore,
    IReadOnlyList<SpeakingQuestionReviewItemDto> Questions,
    SpeakingQuestionReviewItemDto? LowestScoredQuestion
);

public record SpeakingQuestionReviewItemDto(
    int QuestionId,
    int DisplayOrder,
    string SectionName,
    string Stem,
    string? AudioPath,
    int? DurationMs,
    string? Transcript,
    decimal TotalScore,
    decimal MaxScore,
    IReadOnlyList<SpeakingCriterionScoreItemDto> Criteria
);

public record SpeakingCriterionScoreItemDto(
    int CriterionId,
    string Code,
    string Name,
    decimal Score,
    decimal MaxScore,
    string? Comment
);
