using System.Threading;
using System.Threading.Tasks;
using Contracts;

namespace Services;

/// <summary>
/// #204 / S-07: the productive-task studio. One task in, one task out.
/// <para>
/// <see cref="AnalyseAsync"/> is what the screen calls on every edit: it runs the same seam the save
/// runs and returns the refusals, the unrun checks and - the point of the ticket - the placement caps
/// the author must see <em>before</em> doing the work. A screen that computed any of those itself
/// would be a second answer to one question.
/// </para>
/// </summary>
public interface IProductiveStudioService
{
    /// <summary>The closed four topics of one productive skill, the six levels and D0.4's types with their bounds.</summary>
    Task<ProductiveStudioOptionsDto> GetOptionsAsync(string modality, CancellationToken cancellationToken = default);

    /// <summary>
    /// Judges a draft without writing anything, and reports the placement caps whether or not the task
    /// is currently legal. The gate each refusal came from is named, because an author needs to know
    /// whether to change the prompt, the range or the window.
    /// </summary>
    Task<ProductiveTaskAnalysisDto> AnalyseAsync(ProductiveTaskDraftRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the prompt and its parts in one transaction, both as <c>draft</c> and <c>demo</c>. A task
    /// the gates refuse writes nothing and the refusal names the gate.
    /// </summary>
    Task<ProductiveTaskBundleDto> SaveAsync(ProductiveTaskDraftRequest request, CancellationToken cancellationToken = default);
}
