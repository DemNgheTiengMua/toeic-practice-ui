using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contracts;

public sealed partial class CompanionApiClient
{
    public Task<WeaknessAnalysisDto> GetWeaknessAnalysisAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<WeaknessAnalysisDto>("api/learner/weakness-analysis", bearerToken, cancellationToken);

    public async Task<ResolveRecommendationResultDto> ResolveRecommendationAsync(int recId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/recommendations/{recId}/resolve");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<ResolveRecommendationResultDto>(Json, cancellationToken))!;
    }

    public Task<WeaknessAnalysisDto> GetWeaknessAnalysisAsync(CancellationToken cancellationToken = default) =>
        GetWeaknessAnalysisAsync(string.Empty, cancellationToken);

    public Task<ResolveRecommendationResultDto> ResolveRecommendationAsync(int recId, CancellationToken cancellationToken = default) =>
        ResolveRecommendationAsync(recId, string.Empty, cancellationToken);
}
