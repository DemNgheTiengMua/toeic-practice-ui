using System.Threading;
using System.Threading.Tasks;

namespace Contracts;

/// <summary>
/// #204 / S-07: the productive studio's three calls. Separate from <see cref="IStimulusStudioClient"/>
/// because the two author different shapes - a shared passage with 2-4 children, versus one prompt with
/// 1-2 parts - and a client that mixed them would invite a caller to save a task's parts one at a time,
/// which is exactly the half-authored two-part task the ticket refuses.
/// </summary>
public interface IProductiveStudioClient
{
    Task<ProductiveStudioOptionsDto> GetProductiveStudioOptionsAsync(string skill, CancellationToken cancellationToken = default);
    Task<ProductiveStudioOptionsDto> GetProductiveStudioOptionsAsync(string skill, string bearerToken, CancellationToken cancellationToken = default) => GetProductiveStudioOptionsAsync(skill, cancellationToken);

    Task<ProductiveTaskAnalysisDto> AnalyseProductiveTaskAsync(ProductiveTaskDraftRequest request, CancellationToken cancellationToken = default);
    Task<ProductiveTaskAnalysisDto> AnalyseProductiveTaskAsync(ProductiveTaskDraftRequest request, string bearerToken, CancellationToken cancellationToken = default) => AnalyseProductiveTaskAsync(request, cancellationToken);

    Task<ProductiveTaskBundleDto> SaveProductiveTaskAsync(ProductiveTaskDraftRequest request, CancellationToken cancellationToken = default);
    Task<ProductiveTaskBundleDto> SaveProductiveTaskAsync(ProductiveTaskDraftRequest request, string bearerToken, CancellationToken cancellationToken = default) => SaveProductiveTaskAsync(request, cancellationToken);
}
