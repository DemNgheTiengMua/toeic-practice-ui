using System;

namespace Contracts;

/// <summary>#153: the one-step onboarding form. <c>CurrentBandCode</c> and <c>TargetDate</c> are
/// nullable: null current band means "use the assessed band", null date means "no deadline".</summary>
public sealed record SetGoalRequest(
    string? CurrentBandCode,
    string TargetBandCode,
    DateOnly? TargetDate,
    decimal? WeeklyStudyHours);

public sealed record SetGoalResultDto(
    int UserId,
    int CertId,
    string TargetBandCode,
    DateTimeOffset SetAt,
    GoalStatusDto Goal);
