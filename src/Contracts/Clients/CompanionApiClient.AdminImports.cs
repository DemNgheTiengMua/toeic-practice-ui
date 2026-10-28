using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contracts;

public sealed partial class CompanionApiClient
{

    public Task<AdminImportBatchesDto> GetAdminImportBatchesAsync(int? certId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<AdminImportBatchesDto>(certId is { } id ? $"api/admin/imports?certId={id}" : "api/admin/imports", bearerToken, cancellationToken);

    public Task<AdminImportBatchDetailDto> GetAdminImportBatchAsync(int batchId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<AdminImportBatchDetailDto>($"api/admin/imports/{batchId}", bearerToken, cancellationToken);

    public async Task<AdminImportResultDto> UploadAdminImportAsync(int certId, Stream content, string fileName, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(certId.ToString()), "certId");
        var file = new StreamContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/admin/imports") { Content = form };
        AttachBearer(request, bearerToken);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AdminImportResultDto>(Json, cancellationToken))!;
    }

    public Task<AdminImportBatchesDto> GetAdminImportBatchesAsync(int? certId, CancellationToken cancellationToken = default) =>
        GetAdminImportBatchesAsync(certId, string.Empty, cancellationToken);

    public Task<AdminImportBatchDetailDto> GetAdminImportBatchAsync(int batchId, CancellationToken cancellationToken = default) =>
        GetAdminImportBatchAsync(batchId, string.Empty, cancellationToken);

    public Task<AdminImportResultDto> UploadAdminImportAsync(int certId, Stream content, string fileName, CancellationToken cancellationToken = default) =>
        UploadAdminImportAsync(certId, content, fileName, string.Empty, cancellationToken);
}
