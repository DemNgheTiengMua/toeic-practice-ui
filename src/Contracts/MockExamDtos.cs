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
    string Modality,
    string SectionCode,
    string SectionName,
    int? ExpectedQuestionCount,
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
    string Status,
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
    string Modality,
    string Stem,
    string? ImagePath,
    string? ImageAlt,
    string? AudioPath,
    string? Transcript,
    string? Passage,
    IReadOnlyList<AttemptOptionDto> Options,
    int? SelectedOptionId,
    bool IsFlagged);

public sealed record NavigatorItemDto(
    int QuestionId,
    int DisplayOrder,
    int SectionId,
    string SectionCode,
    string Modality,
    bool IsAnswered,
    bool IsFlagged,
    bool IsCurrent);

public sealed record AttemptTakeDto(
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
    AttemptQuestionDto CurrentQuestion,
    IReadOnlyList<NavigatorItemDto> Navigator,
    bool IsAutoSubmitted);

public sealed record SaveAnswerRequest(
    int? SelectedOptionId,
    bool IsFlagged);

public sealed record SaveAnswerResultDto(
    int QuestionId,
    int? SelectedOptionId,
    bool IsFlagged,
    int RemainingSeconds,
    bool IsAutoSubmitted);

public sealed record SubmitAttemptResponse(
    int AttemptId,
    string Status,
    int TotalScore,
    string? BandCode,
    string? BandName,
    DateTimeOffset SubmittedAt,
    bool IsAutoSubmitted);

public sealed record AttemptSkillScoreDto(
    int SkillId,
    string SkillCode,
    string SkillName,
    string Modality,
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
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset SubmittedAt,
    int DurationSeconds,
    bool IsAutoSubmitted,
    int TotalScore,
    string? BandCode,
    string? BandName,
    IReadOnlyList<AttemptSkillScoreDto> SkillScores,
    int? PreviousAttemptScore,
    int? ScoreDelta);
