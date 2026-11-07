using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Contracts;

/// <summary>
/// #208: the rubric and grader calibration studio. Three calls, and the split is the ticket's:
/// <list type="number">
/// <item><b>The grid.</b> Read a skill's four criteria with their 28 cells and their anchors, or save
/// them. Saving writes nothing when the grid is incomplete.</item>
/// <item><b>The ladder.</b> Run seven responses through the repo's one grading seam and plot the
/// composite per band. Split from the save because a ladder is expensive (seven grader calls) and
/// because a save must not silently re-run a model.</item>
/// <item><b>The lock.</b> Refused unless the ladder accepted; the refusal names monotonicity, separation
/// or the trap ceiling.</item>
/// </list>
/// </summary>
public interface IRubricStudioClient
{
    Task<RubricStudioDto> GetRubricStudioAsync(int certId, int skillId, CancellationToken cancellationToken = default);
    Task<RubricStudioDto> GetRubricStudioAsync(int certId, int skillId, string bearerToken, CancellationToken cancellationToken = default) => GetRubricStudioAsync(certId, skillId, cancellationToken);

    Task<RubricStudioDto> SaveRubricAsync(SaveRubricRequest request, CancellationToken cancellationToken = default);
    Task<RubricStudioDto> SaveRubricAsync(SaveRubricRequest request, string bearerToken, CancellationToken cancellationToken = default) => SaveRubricAsync(request, cancellationToken);

    Task<RubricLadderDto> RunRubricLadderAsync(RunRubricLadderRequest request, CancellationToken cancellationToken = default);
    Task<RubricLadderDto> RunRubricLadderAsync(RunRubricLadderRequest request, string bearerToken, CancellationToken cancellationToken = default) => RunRubricLadderAsync(request, cancellationToken);

    Task<RubricLadderDto> LockRubricAsync(RunRubricLadderRequest request, CancellationToken cancellationToken = default);
    Task<RubricLadderDto> LockRubricAsync(RunRubricLadderRequest request, string bearerToken, CancellationToken cancellationToken = default) => LockRubricAsync(request, cancellationToken);
}
