namespace Contracts;

public sealed partial class CompanionApiClient
{
    public async Task<(Stream Stream, string ContentType)> GetImportMediaAsync(int batchId, string fileName, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/media/imports/{batchId}/{Uri.EscapeDataString(fileName)}");
        AttachBearer(request, bearerToken);

        // The response stays open for the caller to stream; it is disposed only if it is a failure.
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        try
        {
            await EnsureSuccessAsync(response, cancellationToken);
        }
        catch
        {
            response.Dispose();
            throw;
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return (stream, response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream");
    }

    public async Task<MediaStreamResponse> GetMediaAsync(MediaArea area, int ownerId, string fileName, string? range, string bearerToken, CancellationToken cancellationToken = default)
    {
        var folder = area == MediaArea.Imports ? "imports" : "exams";
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/media/{folder}/{ownerId}/{Uri.EscapeDataString(fileName)}");
        AttachBearer(request, bearerToken);
        if (!string.IsNullOrEmpty(range))
        {
            request.Headers.TryAddWithoutValidation("Range", range);
        }

        // The response stays open for the caller to stream; it is disposed only if it is a failure.
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        try
        {
            await EnsureSuccessAsync(response, cancellationToken);
        }
        catch
        {
            response.Dispose();
            throw;
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return new MediaStreamResponse(
            stream,
            response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream",
            (int)response.StatusCode,
            response.Content.Headers.ContentRange?.ToString(),
            response.Content.Headers.ContentLength);
    }

    public async Task<(Stream Stream, string ContentType)> GetExamMediaAsync(int examId, string fileName, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/media/exams/{examId}/{Uri.EscapeDataString(fileName)}");
        AttachBearer(request, bearerToken);

        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        try
        {
            await EnsureSuccessAsync(response, cancellationToken);
        }
        catch
        {
            response.Dispose();
            throw;
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return (stream, response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream");
    }

    public Task<(Stream Stream, string ContentType)> GetImportMediaAsync(int batchId, string fileName, CancellationToken cancellationToken = default) =>
        GetImportMediaAsync(batchId, fileName, string.Empty, cancellationToken);

    public Task<(Stream Stream, string ContentType)> GetExamMediaAsync(int examId, string fileName, CancellationToken cancellationToken = default) =>
        GetExamMediaAsync(examId, fileName, string.Empty, cancellationToken);

}
