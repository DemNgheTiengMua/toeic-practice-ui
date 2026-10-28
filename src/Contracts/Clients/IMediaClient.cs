namespace Contracts;

public interface IMediaClient
{
    Task<(Stream Stream, string ContentType)> GetImportMediaAsync(int batchId, string fileName, CancellationToken cancellationToken = default);
    Task<(Stream Stream, string ContentType)> GetImportMediaAsync(int batchId, string fileName, string bearerToken, CancellationToken cancellationToken = default) => GetImportMediaAsync(batchId, fileName, cancellationToken);
    Task<(Stream Stream, string ContentType)> GetExamMediaAsync(int examId, string fileName, CancellationToken cancellationToken = default);
    Task<(Stream Stream, string ContentType)> GetExamMediaAsync(int examId, string fileName, string bearerToken, CancellationToken cancellationToken = default) => GetExamMediaAsync(examId, fileName, cancellationToken);

    /// <summary>Fetches a stored media file (an import batch's or an exam's) forwarding the browser's
    /// Range header, so audio can start mid-track (slices) and seek through the proxy.</summary>
    async Task<MediaStreamResponse> GetMediaAsync(MediaArea area, int ownerId, string fileName, string? range, string bearerToken, CancellationToken cancellationToken = default)
    {
        var (stream, contentType) = area == MediaArea.Imports
            ? await GetImportMediaAsync(ownerId, fileName, bearerToken, cancellationToken)
            : await GetExamMediaAsync(ownerId, fileName, bearerToken, cancellationToken);
        return new MediaStreamResponse(stream, contentType, 200, null, null);
    }
}
