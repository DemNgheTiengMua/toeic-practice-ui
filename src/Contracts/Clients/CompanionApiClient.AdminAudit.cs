using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Contracts;

public sealed partial class CompanionApiClient
{
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    public Task<AuditQueueDto> GetAuditQueueAsync(string bearerToken, int batchId, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<AuditQueueDto>($"api/admin/audit/batches/{batchId}", bearerToken, cancellationToken);

    public Task<AuditQueueDto> DrawSampleAsync(string bearerToken, int batchId, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<AuditQueueDto>($"api/admin/audit/batches/{batchId}/draw", bearerToken, cancellationToken);

    public async Task RecordSolveAsync(
        string bearerToken, int sampleItemId, string label, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"api/admin/audit/samples/{sampleItemId}/solve")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { label }, AuditJson), Encoding.UTF8, "application/json"),
        };
        AttachBearer(request, bearerToken);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>The one call that returns a key. The server refuses it until a solve is recorded.</summary>
    public async Task<AuditKeyRevealDto> RevealSampleKeyAsync(
        string bearerToken, int sampleItemId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"api/admin/audit/samples/{sampleItemId}/reveal");
        AttachBearer(request, bearerToken);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AuditKeyRevealDto>(AuditJson, cancellationToken))!;
    }

    public async Task RecordVerdictAsync(
        string bearerToken, int sampleItemId, string verdict, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"api/admin/audit/samples/{sampleItemId}/verdict")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { verdict }, AuditJson), Encoding.UTF8, "application/json"),
        };
        AttachBearer(request, bearerToken);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<AuditDecisionDto> DecideStratumAsync(
        string bearerToken, int stratumId, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<AuditDecisionDto>($"api/admin/audit/strata/{stratumId}/decide", bearerToken, cancellationToken);

    public Task<AuditQueueDto> GetAuditQueueAsync(int batchId, CancellationToken cancellationToken = default) =>
        GetAuditQueueAsync(string.Empty, batchId, cancellationToken);

    public Task<AuditQueueDto> DrawSampleAsync(int batchId, CancellationToken cancellationToken = default) =>
        DrawSampleAsync(string.Empty, batchId, cancellationToken);

    public Task RecordSolveAsync(int sampleItemId, string label, CancellationToken cancellationToken = default) =>
        RecordSolveAsync(string.Empty, sampleItemId, label, cancellationToken);

    public Task<AuditKeyRevealDto> RevealSampleKeyAsync(int sampleItemId, CancellationToken cancellationToken = default) =>
        RevealSampleKeyAsync(string.Empty, sampleItemId, cancellationToken);

    public Task RecordVerdictAsync(int sampleItemId, string verdict, CancellationToken cancellationToken = default) =>
        RecordVerdictAsync(string.Empty, sampleItemId, verdict, cancellationToken);

    public Task<AuditDecisionDto> DecideStratumAsync(int stratumId, CancellationToken cancellationToken = default) =>
        DecideStratumAsync(string.Empty, stratumId, cancellationToken);
}
