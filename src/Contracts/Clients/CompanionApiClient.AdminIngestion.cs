using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Contracts;

public sealed partial class CompanionApiClient
{
    private static readonly JsonSerializerOptions IngestionJson = new(JsonSerializerDefaults.Web);

    public Task<IngestionBatchListDto> GetWorkbenchBatchesAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<IngestionBatchListDto>("api/admin/ingestion", bearerToken, cancellationToken);

    public Task<IngestionBatchDetailDto> GetWorkbenchBatchAsync(int batchId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<IngestionBatchDetailDto>($"api/admin/ingestion/{batchId}", bearerToken, cancellationToken);

    public async Task<IngestionUploadResultDto> UploadWorkbenchFileAsync(
        string skill, Stream content, string fileName, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        var file = new StreamContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"api/admin/ingestion/upload?skill={Uri.EscapeDataString(skill)}")
        {
            Content = form,
        };
        AttachBearer(request, bearerToken);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<IngestionUploadResultDto>(IngestionJson, cancellationToken))!;
    }

    public async Task<IngestionRecordDto> ResubmitWorkbenchRecordAsync(
        int batchId, int line, string correctedRecordJson, string bearerToken, CancellationToken cancellationToken = default)
    {
        // The record is a JSON document in its own right, so it travels as a string member rather than
        // as the request body: the body has to stay a small, fixed shape the API can validate.
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"api/admin/ingestion/{batchId}/records/{line}/resubmit")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { record = correctedRecordJson }, IngestionJson),
                Encoding.UTF8,
                "application/json"),
        };
        AttachBearer(request, bearerToken);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<IngestionRecordDto>(IngestionJson, cancellationToken))!;
    }

    public async Task ClearWorkbenchItemAsync(
        int batchId, int itemId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"api/admin/ingestion/{batchId}/items/{itemId}/clear");
        AttachBearer(request, bearerToken);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<IngestionBatchListDto> GetWorkbenchBatchesAsync(CancellationToken cancellationToken = default) =>
        GetWorkbenchBatchesAsync(string.Empty, cancellationToken);

    public Task<IngestionBatchDetailDto> GetWorkbenchBatchAsync(int batchId, CancellationToken cancellationToken = default) =>
        GetWorkbenchBatchAsync(batchId, string.Empty, cancellationToken);

    public Task<IngestionUploadResultDto> UploadWorkbenchFileAsync(
        string skill, Stream content, string fileName, CancellationToken cancellationToken = default) =>
        UploadWorkbenchFileAsync(skill, content, fileName, string.Empty, cancellationToken);

    public Task<IngestionRecordDto> ResubmitWorkbenchRecordAsync(
        int batchId, int line, string correctedRecordJson, CancellationToken cancellationToken = default) =>
        ResubmitWorkbenchRecordAsync(batchId, line, correctedRecordJson, string.Empty, cancellationToken);

    public Task ClearWorkbenchItemAsync(int batchId, int itemId, CancellationToken cancellationToken = default) =>
        ClearWorkbenchItemAsync(batchId, itemId, string.Empty, cancellationToken);
}
