using System;
using System.Collections.Generic;

namespace BusinessObjects;

public record WritingPracticeInstructionsView(
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
    IReadOnlyList<WritingSectionInfo> Sections
);

public record WritingSectionInfo(
    int SectionId,
    string Code,
    string Name,
    int ExpectedQuestionCount,
    int? ResponseSeconds,
    int DisplayOrder
);

public record WritingAttemptInfo(
    int AttemptId,
    int ExamId,
    AttemptStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    int RemainingSeconds,
    bool IsAutoSubmitted
);

public record WritingTakeView(
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
    WritingQuestionView Question,
    IReadOnlyList<WritingQuestionNav> Questions
);

public record WritingQuestionView(
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
    FreeResponseStatus Status
);

public record WritingQuestionNav(
    int QuestionId,
    int DisplayOrder,
    string SectionCode,
    string SectionName,
    bool IsAnswered,
    bool IsCurrent
);

public record SaveDraftResult(
    int FreeResponseId,
    int QuestionId,
    DateTimeOffset SavedAt,
    int WordCount
);

public record SubmitWritingResult(
    int AttemptId,
    AttemptStatus Status,
    DateTimeOffset SubmittedAt
);

public record WritingGradingPendingView(
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

public record FreeResponseGradingStatus(
    int FreeResponseId,
    int QuestionId,
    int DisplayOrder,
    AiGradingStatus Status,
    int AttemptCount,
    string? FailureReason
);

public record WritingReviewView(
    int AttemptId,
    int ExamId,
    string ExamName,
    string CertCode,
    DateTimeOffset? SubmittedAt,
    int TotalQuestions,
    int AnsweredQuestions,
    decimal TotalScore,
    decimal MaxScore,
    IReadOnlyList<WritingQuestionReviewItem> Questions
);

public record WritingQuestionReviewItem(
    int QuestionId,
    int DisplayOrder,
    string SectionName,
    string Stem,
    string? ResponseText,
    int WordCount,
    decimal TotalScore,
    decimal MaxScore,
    IReadOnlyList<WritingCriterionScoreItem> Criteria
);

public record WritingCriterionScoreItem(
    int CriterionId,
    string Code,
    string Name,
    decimal Score,
    decimal MaxScore,
    string? Comment
);
