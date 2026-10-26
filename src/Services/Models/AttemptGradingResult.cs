using System;
using System.Collections.Generic;
using BusinessObjects.Entities;

namespace Services;

public record AttemptGradingResult(
    ExamAttempt Attempt,
    int TotalScore,
    string? BandCode,
    IReadOnlyList<AttemptSkillScore> SkillScores,
    IReadOnlyList<AttemptAnswer> Answers,
    DateTimeOffset SubmittedAt,
    bool IsAutoSubmitted);
