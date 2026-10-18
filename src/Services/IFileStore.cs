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

    /// <summary>Stores one media file from a question import package under a fresh name and returns
    /// its relative path (imports/{batchId}/...). Read and delete it with the audio or media methods.</summary>
    Task<string> SaveImportMediaAsync(int batchId, Stream stream, string fileExtension, CancellationToken cancellationToken = default);

    /// <summary>Stores an exam media file (image or audio) under exams/{examId}/ and returns its relative path.</summary>
    Task<string> SaveExamMediaAsync(int examId, Stream stream, string fileExtension, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<FileStreamResultInfo?> GetMediaAsync(string relativePath, CancellationToken cancellationToken = default) =>
        GetAudioAsync(relativePath, cancellationToken);

    Task<bool> DeleteMediaAsync(string relativePath, CancellationToken cancellationToken = default) =>
        DeleteAudioAsync(relativePath, cancellationToken);
}
