using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Services;

/// <summary>
/// One criterion as the grader is asked to score it.
/// </summary>
/// <param name="RubricDescription">
/// E2.1 (#151): the band descriptors #147 made authorable. Without them the grader was told to
/// "score against the rubric" while no rubric text existed, and invented a standard. A criterion with
/// none cannot be scored: <see cref="IsGradeable"/> is false and the prompt leaves it out.
/// </param>
public record GradingCriterionDetail(
    int CriterionId,
    string Code,
    string Name,
    decimal MaxScore,
    decimal Weight,
    string? RubricDescription = null
)
{
    public bool IsGradeable => !string.IsNullOrWhiteSpace(RubricDescription);
}
