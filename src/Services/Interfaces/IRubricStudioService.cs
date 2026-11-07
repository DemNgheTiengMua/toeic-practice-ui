using System.Threading;
using System.Threading.Tasks;
using Contracts;

namespace Services;

/// <summary>
/// #208: the rubric and grader calibration studio. <see cref="RubricStudioService"/> is the only
/// implementation; the interface exists so the API's endpoints and the MVC proxy speak one shape.
/// </summary>
public interface IRubricStudioService
{
    Task<RubricStudioDto> GetAsync(int certId, int skillId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes all 28 cells and the anchors in one transaction, or nothing: a half-saved grid is the
    /// shape that leaves an author with a rubric neither they nor the grader wrote.
    /// </summary>
    Task<RubricStudioDto> SaveAsync(SaveRubricRequest request, CancellationToken cancellationToken = default);

    /// <summary>Runs the ladder and reports the verdict. Writes nothing, whatever the verdict.</summary>
    Task<RubricLadderDto> RunLadderAsync(RunRubricLadderRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the ladder and locks only on acceptance. A refusal returns the verdict and writes nothing;
    /// the four failure sentences live on the lock trigger, and this surfaces the runner's own reason.
    /// </summary>
    Task<RubricLadderDto> LockAsync(RunRubricLadderRequest request, CancellationToken cancellationToken = default);
}
