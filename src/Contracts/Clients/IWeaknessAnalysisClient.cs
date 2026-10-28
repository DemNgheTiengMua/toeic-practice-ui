namespace Contracts;

public interface IWeaknessAnalysisClient
{
    Task<WeaknessAnalysisDto> GetWeaknessAnalysisAsync(CancellationToken cancellationToken = default);
    Task<WeaknessAnalysisDto> GetWeaknessAnalysisAsync(string bearerToken, CancellationToken cancellationToken = default) => GetWeaknessAnalysisAsync(cancellationToken);
    Task<ResolveRecommendationResultDto> ResolveRecommendationAsync(int recId, CancellationToken cancellationToken = default);
    Task<ResolveRecommendationResultDto> ResolveRecommendationAsync(int recId, string bearerToken, CancellationToken cancellationToken = default) => ResolveRecommendationAsync(recId, cancellationToken);
    Task<AiInsightDto> GetDashboardInsightAsync(int? certId = null, CancellationToken cancellationToken = default);
    Task<AiInsightDto> GetDashboardInsightAsync(string bearerToken, int? certId = null, CancellationToken cancellationToken = default) => GetDashboardInsightAsync(certId, cancellationToken);
}
