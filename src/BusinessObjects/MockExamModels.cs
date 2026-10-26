using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record MockExamItem(
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

public sealed record MockExamListView(
    CertificateOption Certificate,
    int CreditBalance,
    IReadOnlyList<MockExamItem> Exams);

public sealed record MockExamSectionInfo(
    int SectionId,
    string SkillCode,
    string SkillName,
    Modality Modality,
    string SectionCode,
    string SectionName,
    int? ExpectedQuestionCount,
    int QuestionCount,
    int? DurationMinutes,
    int DisplayOrder);

public sealed record MockExamInstructionsView(
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
    IReadOnlyList<MockExamSectionInfo> Sections);

public sealed record ExamAttemptInfo(
    int AttemptId,
    int ExamId,
    AttemptStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    int RemainingSeconds,
    bool IsAutoSubmitted);

public sealed record AttemptOption(
    int OptionId,
    string Label,
    string Text);

public sealed record AttemptQuestionInfo(
    int QuestionId,
    int DisplayOrder,
    int TotalQuestions,
    int SectionId,
    string SectionCode,
    string SectionName,
    string SkillCode,
    string SkillName,
    Modality Modality,
    string Stem,
    string? ImagePath,
    string? ImageAlt,
    string? AudioPath,
    int? AudioStartMs,
    int? AudioEndMs,
    string? Transcript,
    string? Passage,
    IReadOnlyList<AttemptOption> Options,
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
    int SkillDisplayOrder = 0,
    IReadOnlyList<int>? SelectedOptionIds = null,
    int? MaxSelections = null,
    bool AllowNumber = true,
    string? AcceptedAnswersJson = null,
    string? RequiredKeywords = null,
    string? LayoutType = null,
    bool RequirePassage = false,
    string? NavigationMode = null,
    string? ToolsConfigJson = null,
    bool HideOptionText = false,
    int OptionCount = 4,
    int ClusterSize = 1,
    bool RequireImage = false,
    int UnitNumber = 1,
    int? GroupId = null,
    string? TimerPolicyJson = null);

public sealed record AttemptFinalDraft(
    int QuestionId,
    string? ResponseText);

public sealed record AttemptPhaseInfo(
    int SkillId,
    string SkillCode,
    string SkillName,
    int SkillDisplayOrder,
    int StartOffsetSeconds,
    int DurationSeconds);

public sealed record SaveAttemptDraftResult(
    int FreeResponseId,
    int QuestionId,
    DateTimeOffset SavedAt,
    int WordCount);

public sealed record BatchAnswerItem(
    int QuestionId,
    int? SelectedOptionId,
    bool IsFlagged,
    string? ResponseText = null,
    string? SelectedOptionIdsJson = null);

public sealed record AttemptTransitionResult(
    int AttemptId,
    int? ActiveSkillId,
    int ActiveUnitNumber,
    int? ActiveGroupId,
    DateTimeOffset? PhaseDeadlineAt,
    int RemainingSeconds,
    AttemptStatus Status);

public sealed record NavigatorItem(
    int QuestionId,
    int DisplayOrder,
    int SectionId,
    string SectionCode,
    Modality Modality,
    bool IsAnswered,
    bool IsFlagged,
    bool IsCurrent,
    bool IsLocked = false);

public sealed record AttemptTakeView(
    int AttemptId,
    int ExamId,
    string ExamName,
    string CertCode,
    AttemptStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    int RemainingSeconds,
    int TotalQuestions,
    int AnsweredCount,
    int FlaggedCount,
    AttemptQuestionInfo CurrentQuestion,
    IReadOnlyList<NavigatorItem> Navigator,
    bool IsAutoSubmitted,
    IReadOnlyList<AttemptQuestionInfo>? Questions = null,
    IReadOnlyList<AttemptPhaseInfo>? Phases = null,
    int? ActiveSkillId = null,
    int ActiveUnitNumber = 1,
    int? ActiveGroupId = null,
    DateTimeOffset? PhaseDeadlineAt = null,
    bool IsSkillLocked = false,
    string? AuthorizationCode = null,
    int LastAudioOffsetMs = 0,
    string? CandidateNotesJson = null);

public sealed record SittingSyncRequest(
    IReadOnlyList<BatchAnswerItem>? Answers = null,
    int? LastAudioOffsetMs = null,
    string? CandidateNotesJson = null,
    int? LastQuestionId = null);

public sealed record SittingSyncResult(
    int AttemptId,
    int RemainingSeconds,
    bool IsAutoSubmitted,
    int? ActiveSkillId,
    int ActiveUnitNumber,
    int? ActiveGroupId,
    DateTimeOffset? PhaseDeadlineAt,
    int LastAudioOffsetMs,
    int AnswersSavedCount);

public sealed record AttemptAnswerSheetItem(
    int QuestionId,
    int? SelectedOptionId,
    string? SelectedOptionIdsJson,
    string? ResponseText,
    bool IsFlagged);

public sealed record AttemptResumeView(
    int AttemptId,
    int ExamId,
    string AuthorizationCode,
    int? ActiveSkillId,
    int ActiveUnitNumber,
    int? ActiveGroupId,
    int? LastQuestionId,
    int LastAudioOffsetMs,
    string? CandidateNotesJson,
    int RemainingSeconds,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? PhaseDeadlineAt,
    IReadOnlyList<AttemptAnswerSheetItem> AnswerSheet);

public sealed record PreflightStatusView(
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

public sealed record SaveAttemptAnswerResult(
    int QuestionId,
    int? SelectedOptionId,
    bool IsFlagged,
    int RemainingSeconds,
    bool IsAutoSubmitted);

public sealed record AttemptSkillScoreItem(
    int SkillId,
    string SkillCode,
    string SkillName,
    Modality Modality,
    int RawScore,
    int ScaledScore,
    int TotalQuestions);

public sealed record AttemptResultView(
    int AttemptId,
    int ExamId,
    string ExamName,
    int CertId,
    string CertCode,
    string CertName,
    AttemptStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset SubmittedAt,
    int DurationSeconds,
    bool IsAutoSubmitted,
    int TotalScore,
    string? BandCode,
    string? BandName,
    IReadOnlyList<AttemptSkillScoreItem> SkillScores,
    int? PreviousAttemptScore,
    int? ScoreDelta,
    int FreeResponseCount = 0,
    int GradedFreeResponseCount = 0,
    int FailedFreeResponseCount = 0);

public sealed record ScoreBandItem(
    string Code,
    string Name,
    int MinTotal,
    int MaxTotal,
    int DisplayOrder);

public sealed record ExamHistoryItem(
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
    IReadOnlyList<AttemptSkillScoreItem> SkillScores,
    AttemptStatus Status);

public sealed record ExamHistoryView(
    CertificateOption Certificate,
    int TotalAttempts,
    int? BestScore,
    int? AverageScore,
    string? BestScoreBandCode,
    string? BestScoreBandName,
    IReadOnlyList<ScoreBandItem> CefrBands,
    IReadOnlyList<ExamHistoryItem> Attempts);

public sealed record AttemptReviewOption(
    int OptionId,
    string Label,
    string Text,
    bool IsCorrect,
    string? Rationale);

public sealed record AttemptReviewQuestion(
    int QuestionId,
    int DisplayOrder,
    int TotalQuestions,
    int SectionId,
    string SectionCode,
    string SectionName,
    string SkillCode,
    string SkillName,
    Modality Modality,
    string Stem,
    string? ImagePath,
    string? ImageAlt,
    string? AudioPath,
    int? AudioStartMs,
    int? AudioEndMs,
    string? Transcript,
    string? Passage,
    string? Explanation,
    IReadOnlyList<AttemptReviewOption> Options,
    int? SelectedOptionId,
    bool? IsCorrect,
    bool IsFlagged);

public sealed record ReviewNavigatorItem(
    int QuestionId,
    int DisplayOrder,
    int SectionId,
    string SectionCode,
    Modality Modality,
    bool IsAnswered,
    bool IsFlagged,
    bool? IsCorrect);

public sealed record AttemptReviewView(
    int AttemptId,
    int ExamId,
    string ExamName,
    int CertId,
    string CertCode,
    string CertName,
    AttemptStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset SubmittedAt,
    int DurationSeconds,
    int TotalScore,
    string? BandCode,
    string? BandName,
    IReadOnlyList<AttemptSkillScoreItem> SkillScores,
    int TotalQuestions,
    int AnsweredCount,
    int CorrectCount,
    int IncorrectCount,
    int BlankCount,
    int FlaggedCount,
    IReadOnlyList<AttemptReviewQuestion> Questions,
    IReadOnlyList<ReviewNavigatorItem> Navigator);
