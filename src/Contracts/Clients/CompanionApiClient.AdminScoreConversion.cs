using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contracts;

public sealed partial class CompanionApiClient
{

    public Task<AdminScoreConversionDto> GetAdminScoreConversionAsync(string? certCode, int? examId, string bearerToken, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(certCode)) query.Add($"certCode={Uri.EscapeDataString(certCode)}");
        if (examId.HasValue) query.Add($"examId={examId.Value}");
        var path = query.Count > 0 ? $"api/admin/score-conversion?{string.Join("&", query)}" : "api/admin/score-conversion";
        return GetAuthorizedAsync<AdminScoreConversionDto>(path, bearerToken, cancellationToken);
    }

    public async Task<AdminScoreConversionDto> UpdateAdminScoreConversionAsync(int certId, UpdateScoreConversionRequest request, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, $"api/admin/score-conversion/{certId}")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AdminScoreConversionDto>(Json, cancellationToken))!;
    }

    public async Task<ScorePreviewResultDto> CalculateScorePreviewAsync(ScorePreviewRequestDto request, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/admin/score-conversion/preview")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<ScorePreviewResultDto>(Json, cancellationToken))!;
    }

    public Task<AdminScoreConversionDto> GetAdminScoreConversionAsync(string? certCode, int? examId, CancellationToken cancellationToken = default) =>
        GetAdminScoreConversionAsync(certCode, examId, string.Empty, cancellationToken);

    public Task<AdminScoreConversionDto> UpdateAdminScoreConversionAsync(int certId, UpdateScoreConversionRequest request, CancellationToken cancellationToken = default) =>
        UpdateAdminScoreConversionAsync(certId, request, string.Empty, cancellationToken);

    public Task<ScorePreviewResultDto> CalculateScorePreviewAsync(ScorePreviewRequestDto request, CancellationToken cancellationToken = default) =>
        CalculateScorePreviewAsync(request, string.Empty, cancellationToken);
}
