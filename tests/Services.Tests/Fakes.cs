using System.Text.RegularExpressions;

namespace Services.Tests;

/// <summary>Records mail instead of sending it. The only double for <see cref="IEmailSender"/>.</summary>
internal sealed class RecordingEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = [];

    public bool Fail { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (Fail)
        {
            throw new EmailDeliveryException("Mail drop unavailable.", new IOException());
        }

        Sent.Add(message);
        return Task.CompletedTask;
    }

    public IEnumerable<EmailMessage> To(string email) =>
        Sent.Where(m => string.Equals(m.To, email, StringComparison.OrdinalIgnoreCase));

    /// <summary>The token from the last link sent to <paramref name="email"/> on <paramref name="path"/>.</summary>
    public string LastToken(string email, string path)
    {
        var message = To(email).Last(m => m.Body.Contains(path, StringComparison.Ordinal));
        var match = Regex.Match(message.Body, Regex.Escape(path) + @"\?token=([A-Za-z0-9_\-]+)");
        return match.Success
            ? match.Groups[1].Value
            : throw new InvalidOperationException($"No {path} link in: {message.Body}");
    }
}

/// <summary>A clock the test moves by hand, so expiry is observable without sleeping.</summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset now = start;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan by) => now += by;
}

/// <summary>Keeps imported media in memory. <see cref="FailOnSave"/> makes the Nth save throw, so a
/// test can watch the import undo the files it already wrote.</summary>
internal sealed class RecordingFileStore : IFileStore
{
    public Dictionary<string, byte[]> Files { get; } = [];

    public int? FailOnSave { get; set; }

    private int saves;

    public Task<string> SaveImportMediaAsync(int batchId, Stream stream, string fileExtension, CancellationToken cancellationToken = default)
    {
        if (++saves == FailOnSave)
        {
            throw new IOException("Disk full.");
        }

        var path = $"imports/{batchId}/{Guid.NewGuid():N}{fileExtension}";
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        Files[path] = buffer.ToArray();
        return Task.FromResult(path);
    }

    public Task<string> SaveExamMediaAsync(int examId, Stream stream, string fileExtension, CancellationToken cancellationToken = default)
    {
        var path = $"exams/{examId}/{Guid.NewGuid():N}{fileExtension}";
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        Files[path] = buffer.ToArray();
        return Task.FromResult(path);
    }

    public Task<string> SaveAudioAsync(int attemptId, int questionId, Stream stream, string contentType, string fileExtension, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<FileStreamResultInfo?> GetAudioAsync(string relativePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(Files.TryGetValue(relativePath, out var bytes)
            ? new FileStreamResultInfo(new MemoryStream(bytes), "application/octet-stream", Path.GetFileName(relativePath))
            : null);

    public Task<bool> DeleteAudioAsync(string relativePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(Files.Remove(relativePath));
}
