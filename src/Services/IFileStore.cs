using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Services;

public sealed record FileStreamResultInfo(Stream Stream, string ContentType, string FileName) : IDisposable
{
    public void Dispose() => Stream.Dispose();
}

public interface IFileStore
{
    Task<string> SaveAudioAsync(int attemptId, int questionId, Stream stream, string contentType, string fileExtension, CancellationToken cancellationToken = default);

    Task<FileStreamResultInfo?> GetAudioAsync(string relativePath, CancellationToken cancellationToken = default);

    Task<bool> DeleteAudioAsync(string relativePath, CancellationToken cancellationToken = default);
}
