using System;

namespace BusinessObjects;

/// <summary>#153: what the goal screen needs back after a save — the row it wrote, plus the verdict
/// and the two numbers behind it.</summary>
public sealed record SaveGoalResult(
    int UserId,
    int CertId,
    string TargetBandCode,
    DateTimeOffset SetAt,
    GoalStatus Feasibility);
