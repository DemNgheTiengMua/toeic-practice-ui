using System.Collections.Generic;

namespace BusinessObjects;

public sealed record PlacementGradingStatusView(
    int PlacementId,
    int AttemptId,
    bool IsGraded,
    int FreeResponseCount,
    int GradedFreeResponseCount,
    int FailedFreeResponseCount,
    int? TotalScore,
    string? BandCode,
    string? BandName,
    IReadOnlyList<PlacementSkillScoreInfo> SkillScores,
    string StageDescriptor);
