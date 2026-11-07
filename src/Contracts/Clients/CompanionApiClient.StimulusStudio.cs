namespace Contracts;

public sealed partial class CompanionApiClient
{
    public Task<StimulusStudioOptionsDto> GetStimulusStudioOptionsAsync(string skill, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<StimulusStudioOptionsDto>(
            $"api/admin/studio/stimulus/options?skill={System.Uri.EscapeDataString(skill)}",
            bearerToken,
            cancellationToken);

    public Task<StimulusDraftAnalysisDto> AnalyseStimulusDraftAsync(StimulusDraftRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<StimulusDraftRequest, StimulusDraftAnalysisDto>(
            "api/admin/studio/stimulus/analyse", request, bearerToken, cancellationToken);

    public Task<StimulusBundleDto> SaveStimulusBundleAsync(StimulusDraftRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<StimulusDraftRequest, StimulusBundleDto>(
            "api/admin/studio/stimulus", request, bearerToken, cancellationToken);

    public Task<StimulusStudioOptionsDto> GetStimulusStudioOptionsAsync(string skill, CancellationToken cancellationToken = default) =>
        GetStimulusStudioOptionsAsync(skill, string.Empty, cancellationToken);

    public Task<StimulusDraftAnalysisDto> AnalyseStimulusDraftAsync(StimulusDraftRequest request, CancellationToken cancellationToken = default) =>
        AnalyseStimulusDraftAsync(request, string.Empty, cancellationToken);

    public Task<StimulusBundleDto> SaveStimulusBundleAsync(StimulusDraftRequest request, CancellationToken cancellationToken = default) =>
        SaveStimulusBundleAsync(request, string.Empty, cancellationToken);
}
