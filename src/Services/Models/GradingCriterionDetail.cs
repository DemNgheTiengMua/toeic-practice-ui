using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Services;

/// <summary>One band of a criterion's rubric: what it looks like, and the gates to reach it.</summary>
/// <param name="Band">The CEFR band itself: 0 = Pre-A1/non-scorable ... 6 = C2 (D0.6).</param>
public record GradingCriterionBandDescriptor(byte Band, string Descriptor, string BinaryGates);

/// <summary>
/// One criterion as the grader is asked to score it.
/// </summary>
/// <param name="RubricDescription">
/// E2.1 (#151): the band descriptors #147 made authorable. Without them the grader was told to
/// "score against the rubric" while no rubric text existed, and invented a standard. A criterion with
/// none cannot be scored: <see cref="IsGradeable"/> is false and the prompt leaves it out.
/// </param>
/// <param name="Descriptors">
/// #192: the seven-band grid, in band order. When it is present the prompt is built from it rather
/// than from the prose <paramref name="RubricDescription"/>, because a row per band with its gates is
/// what a grader can actually apply. A grid that has started and stopped short of seven bands is a
/// hole the grader would fill by inference, so <see cref="IsGradeable"/> is false for it too - see
/// <see cref="MissingBands"/>.
/// </param>
public record GradingCriterionDetail(
    int CriterionId,
    string Code,
    string Name,
    decimal MaxScore,
    decimal Weight,
    string? RubricDescription = null,
    IReadOnlyList<GradingCriterionBandDescriptor>? Descriptors = null
)
{
    /// <summary>
    /// Bands 0..6 that the grid does not describe. Empty when there is no grid at all (a criterion
    /// authored before #192, graded on its prose), or when the grid is complete.
    /// </summary>
    public IReadOnlyList<byte> MissingBands => Descriptors is not { Count: > 0 }
        ? Array.Empty<byte>()
        : CanonicalRubric.Bands.Where(b => Descriptors.All(d => d.Band != b)).ToArray();
    /// <summary>
    /// A criterion is gradable when it has standard text to score against: a complete grid, or prose
    /// when no grid was authored at all. A grid that has started and stopped short is NOT gradable,
    /// because the bands it omits would have to be invented by the model -- and a complete grid is
    /// gradable on its own, because seven gates are a tighter standard than a paragraph.
    /// Nothing here infers, ranks or interpolates.
    /// </summary>
    public bool IsGradeable => Descriptors is { Count: > 0 }
        ? MissingBands.Count == 0
        : !string.IsNullOrWhiteSpace(RubricDescription);
}
