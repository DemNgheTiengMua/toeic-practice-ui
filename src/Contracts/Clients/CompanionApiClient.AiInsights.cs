using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contracts;

public sealed partial class CompanionApiClient
{
    public Task<AiInsightDto> GetPracticeInsightAsync(int sessionId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<AiInsightDto>($"api/learner/insights/practice/{sessionId}", bearerToken, cancellationToken);

    public async Task RetryPracticeInsightAsync(int sessionId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/insights/practice/{sessionId}/retry");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task RetryInsightAsync(int insightId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/learner/insights/{insightId}/retry");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<AiInsightDto> GetDashboardInsightAsync(string bearerToken, int? certId = null, CancellationToken cancellationToken = default)
    {
        var path = certId.HasValue ? $"api/learner/insights/dashboard?certId={certId.Value}" : "api/learner/insights/dashboard";
        return GetAuthorizedAsync<AiInsightDto>(path, bearerToken, cancellationToken);
    }

    public async Task RetryDashboardInsightAsync(string bearerToken, int? certId = null, CancellationToken cancellationToken = default)
    {
        var path = certId.HasValue ? $"api/learner/insights/dashboard/retry?certId={certId.Value}" : "api/learner/insights/dashboard/retry";
        using var message = new HttpRequestMessage(HttpMethod.Post, path);
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<AiInsightDto> GetPathInsightAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<AiInsightDto>("api/learner/insights/path", bearerToken, cancellationToken);

    public async Task RetryPathInsightAsync(string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/learner/insights/path/retry");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<AiInsightDto> GetPracticeInsightAsync(int sessionId, CancellationToken cancellationToken = default) =>
        GetPracticeInsightAsync(sessionId, string.Empty, cancellationToken);

    public Task RetryPracticeInsightAsync(int sessionId, CancellationToken cancellationToken = default) =>
        RetryPracticeInsightAsync(sessionId, string.Empty, cancellationToken);

    public Task RetryInsightAsync(int insightId, CancellationToken cancellationToken = default) =>
        RetryInsightAsync(insightId, string.Empty, cancellationToken);

    public Task<AiInsightDto> GetDashboardInsightAsync(int? certId = null, CancellationToken cancellationToken = default) =>
        GetDashboardInsightAsync(string.Empty, certId, cancellationToken);

    public Task RetryDashboardInsightAsync(int? certId = null, CancellationToken cancellationToken = default) =>
        RetryDashboardInsightAsync(string.Empty, certId, cancellationToken);

    public Task<AiInsightDto> GetPathInsightAsync(CancellationToken cancellationToken = default) =>
        GetPathInsightAsync(string.Empty, cancellationToken);

    public Task RetryPathInsightAsync(CancellationToken cancellationToken = default) =>
        RetryPathInsightAsync(string.Empty, cancellationToken);
}
