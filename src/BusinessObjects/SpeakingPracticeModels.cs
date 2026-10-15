using System;
using System.Collections.Generic;

namespace BusinessObjects;

public record SpeakingPracticeInstructionsView(
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
    IReadOnlyList<SpeakingSectionInfo> Sections
);

public record SpeakingSectionInfo(
    int SectionId,
    string Code,
    string Name,
    int ExpectedQuestionCount,
    int? PrepSeconds,
    int? ResponseSeconds,
    int DisplayOrder
);

public record SpeakingAttemptInfo(
    int AttemptId,
    int ExamId,
    AttemptStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    int RemainingSeconds,
    bool IsAutoSubmitted
);

public record SpeakingTakeView(
    int AttemptId,
    int ExamId,
    string ExamName,
    int CertId,
    string CertCode,
    AttemptStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    int RemainingSeconds,
    int CurrentQuestionId,
    int CurrentQuestionIndex,
    int TotalQuestions,
    int SavedCount,
    SpeakingQuestionView Question,
    IReadOnlyList<SpeakingQuestionNav> Questions
);

public record SpeakingQuestionView(
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
    FreeResponseStatus Status
);

public record SpeakingQuestionNav(
    int QuestionId,
    int DisplayOrder,
    string SectionCode,
    string SectionName,
    bool IsAnswered,
    bool IsCurrent
);

public record SaveSpeakingAudioResult(
    int FreeResponseId,
    int QuestionId,
    string AudioPath,
    int? DurationMs,
    DateTimeOffset SavedAt
);

public record SubmitSpeakingResult(
    int AttemptId,
    AttemptStatus Status,
    DateTimeOffset SubmittedAt
);

public record SpeakingGradingPendingView(
    int AttemptId,
    int ExamId,
    string ExamName,
    string CertCode,
    AttemptStatus AttemptStatus,
    int TotalResponses,
    int GradedResponses,
    int FailedResponses,
    int PendingResponses,
    bool CanRetry,
    string? FailureReason,
    IReadOnlyList<FreeResponseGradingStatus> Items
);

public record SpeakingReviewView(
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
    IReadOnlyList<SpeakingQuestionReviewItem> Questions,
    SpeakingQuestionReviewItem? LowestScoredQuestion
);

public record SpeakingQuestionReviewItem(
    int QuestionId,
    int DisplayOrder,
    string SectionName,
    string Stem,
    string? AudioPath,
    int? DurationMs,
    string? Transcript,
    decimal TotalScore,
    decimal MaxScore,
    IReadOnlyList<SpeakingCriterionScoreItem> Criteria
);

public record SpeakingCriterionScoreItem(
    int CriterionId,
    string Code,
    string Name,
    decimal Score,
    decimal MaxScore,
    string? Comment
);
