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
    bool IsFlagged);

public sealed record NavigatorItem(
    int QuestionId,
    int DisplayOrder,
    int SectionId,
    string SectionCode,
    Modality Modality,
    bool IsAnswered,
    bool IsFlagged,
    bool IsCurrent);

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
    IReadOnlyList<AttemptQuestionInfo>? Questions = null);

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
    int? ScoreDelta);

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

