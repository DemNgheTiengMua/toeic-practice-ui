using System;
using System.Collections.Generic;

namespace Contracts;

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
