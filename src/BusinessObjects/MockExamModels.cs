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
    string Modality,
    string SectionCode,
    string SectionName,
    int? ExpectedQuestionCount,
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
    string Status,
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
    string Modality,
    string Stem,
    string? ImagePath,
    string? ImageAlt,
    string? AudioPath,
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
    string Modality,
    bool IsAnswered,
    bool IsFlagged,
    bool IsCurrent);

public sealed record AttemptTakeView(
    int AttemptId,
    int ExamId,
    string ExamName,
    string CertCode,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    int RemainingSeconds,
    int TotalQuestions,
    int AnsweredCount,
    int FlaggedCount,
    AttemptQuestionInfo CurrentQuestion,
    IReadOnlyList<NavigatorItem> Navigator,
    bool IsAutoSubmitted);

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
    string Modality,
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
    string Status,
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
