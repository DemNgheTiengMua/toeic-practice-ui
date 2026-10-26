using System;
using System.Collections.Generic;

namespace BusinessObjects;

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
