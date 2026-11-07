namespace Contracts;

public sealed partial class CompanionApiClient
{
    public Task<ProductiveStudioOptionsDto> GetProductiveStudioOptionsAsync(string skill, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<ProductiveStudioOptionsDto>(
            $"api/admin/studio/productive/options?skill={System.Uri.EscapeDataString(skill)}",
            bearerToken,
            cancellationToken);

    public Task<ProductiveTaskAnalysisDto> AnalyseProductiveTaskAsync(ProductiveTaskDraftRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<ProductiveTaskDraftRequest, ProductiveTaskAnalysisDto>(
            "api/admin/studio/productive/analyse", request, bearerToken, cancellationToken);

    public Task<ProductiveTaskBundleDto> SaveProductiveTaskAsync(ProductiveTaskDraftRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<ProductiveTaskDraftRequest, ProductiveTaskBundleDto>(
            "api/admin/studio/productive", request, bearerToken, cancellationToken);

    public Task<ProductiveStudioOptionsDto> GetProductiveStudioOptionsAsync(string skill, CancellationToken cancellationToken = default) =>
        GetProductiveStudioOptionsAsync(skill, string.Empty, cancellationToken);

    public Task<ProductiveTaskAnalysisDto> AnalyseProductiveTaskAsync(ProductiveTaskDraftRequest request, CancellationToken cancellationToken = default) =>
        AnalyseProductiveTaskAsync(request, string.Empty, cancellationToken);

    public Task<ProductiveTaskBundleDto> SaveProductiveTaskAsync(ProductiveTaskDraftRequest request, CancellationToken cancellationToken = default) =>
        SaveProductiveTaskAsync(request, string.Empty, cancellationToken);
}
