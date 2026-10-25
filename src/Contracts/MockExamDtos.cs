using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record MockExamItemDto(
    int ExamId,
    int CertId,
    string CertCode,
    string Name,
    string? Description,
    int DurationMinutes,
    int? QuestionCount,
    string? SourceName,
    int CreditCost,
    int? ActiveAttemptId,
    DateTimeOffset? ActiveAttemptExpiresAt,
    int? CompletedAttemptScore,
    string? CompletedAttemptBand);

public sealed record MockExamListViewDto(
    CertificateOptionDto Certificate,
    int CreditBalance,
    IReadOnlyList<MockExamItemDto> Exams);

public sealed record MockExamSectionInfoDto(
    int SectionId,
    string SkillCode,
    string SkillName,
    ModalityDto Modality,
    string SectionCode,
    string SectionName,
    int? ExpectedQuestionCount,
    int QuestionCount,
    int? DurationMinutes,
    int DisplayOrder);

public sealed record MockExamInstructionsDto(
    int ExamId,
    int CertId,
    string CertCode,
    string ExamName,
    string? Description,
    int DurationMinutes,
    int? QuestionCount,
    int CreditCost,
    int CreditBalance,
    int? ActiveAttemptId,
    IReadOnlyList<MockExamSectionInfoDto> Sections);

public sealed record StartMockExamResponse(
    int AttemptId,
    int ExamId,
    AttemptStatusDto Status,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    int RemainingSeconds,
    bool IsAutoSubmitted);

public sealed record AttemptOptionDto(
    int OptionId,
    string Label,
    string Text);

public sealed record AttemptQuestionDto(
    int QuestionId,
    int DisplayOrder,
    int TotalQuestions,
    int SectionId,
    string SectionCode,
    string SectionName,
    string SkillCode,
    string SkillName,
    ModalityDto Modality,
    string Stem,
    string? ImagePath,
    string? ImageAlt,
    string? AudioPath,
    int? AudioStartMs,
    int? AudioEndMs,
    string? Transcript,
    string? Passage,
    IReadOnlyList<AttemptOptionDto> Options,
    int? SelectedOptionId,
    bool IsFlagged,
    string QuestionType = "mcq",
    int? MinWords = null,
    int? MaxWords = null,
    int? PrepSeconds = null,
    int? ResponseSeconds = null,
    string? ResponseText = null,
    string? ResponseAudioPath = null,
    int? ResponseDurationMs = null,
    DateTimeOffset? DraftSavedAt = null,
    int? SkillDurationMinutes = null,
    int SkillDisplayOrder = 0);

public sealed record NavigatorItemDto(
    int QuestionId,
    int DisplayOrder,
    int SectionId,
    string SectionCode,
    ModalityDto Modality,
    bool IsAnswered,
    bool IsFlagged,
    bool IsCurrent,
    bool IsLocked = false);

public sealed record TransitionUnitRequest(int TargetUnitNumber);
public sealed record TransitionGroupRequest(int TargetGroupId);

public sealed record AttemptTransitionResponse(
    int AttemptId,
    int? ActiveSkillId,
    int ActiveUnitNumber,
    int? ActiveGroupId,
    DateTimeOffset? PhaseDeadlineAt,
    int RemainingSeconds,
    AttemptStatusDto Status,
    bool IsSubmitted);

public sealed record AttemptTakeDto(
    int AttemptId,
    int ExamId,
    string ExamName,
    string CertCode,
    AttemptStatusDto Status,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    int RemainingSeconds,
    int TotalQuestions,
    int AnsweredCount,
    int FlaggedCount,
    AttemptQuestionDto CurrentQuestion,
    IReadOnlyList<NavigatorItemDto> Navigator,
    bool IsAutoSubmitted,
    IReadOnlyList<AttemptQuestionDto>? Questions = null,
    IReadOnlyList<AttemptPhaseDto>? Phases = null,
    int? ActiveSkillId = null,
    int ActiveUnitNumber = 1,
    int? ActiveGroupId = null,
    DateTimeOffset? PhaseDeadlineAt = null,
    bool IsSkillLocked = false);

public sealed record AttemptPhaseDto(
    int SkillId,
    string SkillCode,
    string SkillName,
    int SkillDisplayOrder,
    int StartOffsetSeconds,
    int DurationSeconds);

public sealed record PreflightStatusDto(
    int AttemptId,
    int ExamId,
    string ExamName,
    string CertCode,
    string CertName,
    int DurationMinutes,
    bool RequiresPreflight,
    bool RequiresAudio,
    bool RequiresMicrophone,
    bool IsVerified,
    DateTimeOffset? VerifiedAt,
    IReadOnlyList<string> AudioUrls);

public sealed record SaveAttemptDraftRequest(
    int QuestionId,
    string? ResponseText);

public sealed record SaveAttemptDraftResponse(
    int FreeResponseId,
    int QuestionId,
    DateTimeOffset SavedAt,
    int WordCount);

public sealed record SaveAnswerRequest(
    int? SelectedOptionId,
    bool IsFlagged,
    string? ResponseText = null);

public sealed record SaveAnswerResultDto(
    int QuestionId,
    int? SelectedOptionId,
    bool IsFlagged,
    int RemainingSeconds,
    bool IsAutoSubmitted);

public sealed record SubmitAttemptResponse(
    int AttemptId,
    AttemptStatusDto Status,
    int TotalScore,
    string? BandCode,
    string? BandName,
    DateTimeOffset SubmittedAt,
    bool IsAutoSubmitted);

public sealed record SubmitAttemptDraftRequest(
    int QuestionId,
    string? ResponseText);

public sealed record SubmitAttemptRequest(
    IReadOnlyList<SubmitAttemptDraftRequest> Drafts);

public sealed record AttemptSkillScoreDto(
    int SkillId,
    string SkillCode,
    string SkillName,
    ModalityDto Modality,
    int RawScore,
    int ScaledScore,
    int TotalQuestions);

public sealed record AttemptResultDto(
    int AttemptId,
    int ExamId,
    string ExamName,
    int CertId,
    string CertCode,
    string CertName,
    AttemptStatusDto Status,
    DateTimeOffset StartedAt,
    DateTimeOffset SubmittedAt,
    int DurationSeconds,
    bool IsAutoSubmitted,
    int TotalScore,
    string? BandCode,
    string? BandName,
    IReadOnlyList<AttemptSkillScoreDto> SkillScores,
    int? PreviousAttemptScore,
    int? ScoreDelta,
    int FreeResponseCount = 0,
    int GradedFreeResponseCount = 0,
    int FailedFreeResponseCount = 0);

public sealed record ScoreBandItemDto(
    string Code,
    string Name,
    int MinTotal,
    int MaxTotal,
    int DisplayOrder);

public sealed record ExamHistoryItemDto(
    int AttemptId,
    int ExamId,
    string ExamName,
    int CertId,
    string CertCode,
    DateTimeOffset StartedAt,
    DateTimeOffset SubmittedAt,
    int TotalScore,
    string? BandCode,
    string? BandName,
    IReadOnlyList<AttemptSkillScoreDto> SkillScores,
    AttemptStatusDto Status);

public sealed record ExamHistoryDto(
    CertificateOptionDto Certificate,
    int TotalAttempts,
    int? BestScore,
    int? AverageScore,
    string? BestScoreBandCode,
    string? BestScoreBandName,
    IReadOnlyList<ScoreBandItemDto> CefrBands,
    IReadOnlyList<ExamHistoryItemDto> Attempts);

public sealed record AttemptReviewOptionDto(
    int OptionId,
    string Label,
    string Text,
    bool IsCorrect,
    string? Rationale);

public sealed record AttemptReviewQuestionDto(
    int QuestionId,
    int DisplayOrder,
    int TotalQuestions,
    int SectionId,
    string SectionCode,
    string SectionName,
    string SkillCode,
    string SkillName,
    ModalityDto Modality,
    string Stem,
    string? ImagePath,
    string? ImageAlt,
    string? AudioPath,
    int? AudioStartMs,
    int? AudioEndMs,
    string? Transcript,
    string? Passage,
    string? Explanation,
    IReadOnlyList<AttemptReviewOptionDto> Options,
    int? SelectedOptionId,
    bool? IsCorrect,
    bool IsFlagged);

public sealed record ReviewNavigatorItemDto(
    int QuestionId,
    int DisplayOrder,
    int SectionId,
    string SectionCode,
    ModalityDto Modality,
    bool IsAnswered,
    bool IsFlagged,
    bool? IsCorrect);

public sealed record AttemptReviewDto(
    int AttemptId,
    int ExamId,
    string ExamName,
    int CertId,
    string CertCode,
    string CertName,
    AttemptStatusDto Status,
    DateTimeOffset StartedAt,
    DateTimeOffset SubmittedAt,
    int DurationSeconds,
    int TotalScore,
    string? BandCode,
    string? BandName,
    IReadOnlyList<AttemptSkillScoreDto> SkillScores,
    int TotalQuestions,
    int AnsweredCount,
    int CorrectCount,
    int IncorrectCount,
    int BlankCount,
    int FlaggedCount,
    IReadOnlyList<AttemptReviewQuestionDto> Questions,
    IReadOnlyList<ReviewNavigatorItemDto> Navigator);
