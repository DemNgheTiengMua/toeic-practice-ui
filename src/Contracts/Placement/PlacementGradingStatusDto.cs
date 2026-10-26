using System.Collections.Generic;

namespace Contracts;

public sealed record PlacementGradingStatusDto(
    int PlacementId,
    int AttemptId,
    bool IsGraded,
    int FreeResponseCount,
    int GradedFreeResponseCount,
    int FailedFreeResponseCount,
    int? TotalScore,
    string? BandCode,
    string? BandName,
    IReadOnlyList<PlacementSkillScoreInfoDto> SkillScores,
    string StageDescriptor);
