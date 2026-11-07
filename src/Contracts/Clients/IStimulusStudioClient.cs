using System.Threading;
using System.Threading.Tasks;

namespace Contracts;

/// <summary>
/// #203 / S-05: the studio's three calls. Separate from <see cref="IAdminItemsClient"/> because the
/// bundle is one document rather than a sequence of item writes, and a client that mixed the two would
/// invite a caller to save four children by four calls - which is exactly the half-committed bundle the
/// studio exists to prevent.
/// </summary>
public interface IStimulusStudioClient
{
    Task<StimulusStudioOptionsDto> GetStimulusStudioOptionsAsync(string skill, CancellationToken cancellationToken = default);
    Task<StimulusStudioOptionsDto> GetStimulusStudioOptionsAsync(string skill, string bearerToken, CancellationToken cancellationToken = default) => GetStimulusStudioOptionsAsync(skill, cancellationToken);

    Task<StimulusDraftAnalysisDto> AnalyseStimulusDraftAsync(StimulusDraftRequest request, CancellationToken cancellationToken = default);
    Task<StimulusDraftAnalysisDto> AnalyseStimulusDraftAsync(StimulusDraftRequest request, string bearerToken, CancellationToken cancellationToken = default) => AnalyseStimulusDraftAsync(request, cancellationToken);

    Task<StimulusBundleDto> SaveStimulusBundleAsync(StimulusDraftRequest request, CancellationToken cancellationToken = default);
    Task<StimulusBundleDto> SaveStimulusBundleAsync(StimulusDraftRequest request, string bearerToken, CancellationToken cancellationToken = default) => SaveStimulusBundleAsync(request, cancellationToken);
}
