using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Services;

/// <summary>
/// #276: the stage-two draw as a caller sees it - the routed pair of bands, the per-pair counts, and the
/// same-band cascade, over the real pool.
/// <para>
/// <see cref="PlacementStageTwoDraw"/> already models the three outcomes: complete, degraded (shorter but
/// measurable) and refused (below D10's floor). The caller reuses that distinction rather than inventing
/// a second rule - the whole reason <see cref="EnsureSittableAsync"/> exists is so a refusal is named at
/// the boundary instead of becoming a paper the 70% rule cannot read.
/// </para>
/// </summary>
public interface IPlacementStageTwoDrawService
{
    /// <summary>Draws stage two for every objective skill that has a stage-one score.</summary>
    Task<PlacementStageTwoDraw> DrawAsync(
        int userId,
        int certId,
        string contentTier,
        IReadOnlyDictionary<int, int> stageOneScoreBySkillId,
        IReadOnlyList<int>? servedItemIds = null,
        IReadOnlyDictionary<int, int>? preferredTopicBySkillId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The same draw, but a sitting that cannot be measured is refused rather than returned short.
    /// A refusal writes nothing, so it cannot strand a half-started sitting.
    /// </summary>
    Task<PlacementStageTwoDraw> EnsureSittableAsync(
        int userId,
        int certId,
        string contentTier,
        IReadOnlyDictionary<int, int> stageOneScoreBySkillId,
        IReadOnlyList<int>? servedItemIds = null,
        IReadOnlyDictionary<int, int>? preferredTopicBySkillId = null,
        CancellationToken cancellationToken = default);
}
