using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Services;

public static class AudioValidation
{
    public const long MaxAudioSizeBytes = 10 * 1024 * 1024; // 10 MB

    public static readonly Dictionary<string, string> ExtensionToMime = new(StringComparer.OrdinalIgnoreCase)
    {
        [".webm"] = "audio/webm",
        [".wav"] = "audio/wav",
        [".mp3"] = "audio/mpeg",
        [".ogg"] = "audio/ogg",
        [".m4a"] = "audio/mp4",
        [".mp4"] = "audio/mp4",
        [".aac"] = "audio/aac",
    };

    public static readonly Dictionary<string, string> MimeToExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        ["audio/webm"] = ".webm",
        ["audio/wav"] = ".wav",
        ["audio/wave"] = ".wav",
        ["audio/x-wav"] = ".wav",
        ["audio/mp3"] = ".mp3",
        ["audio/mpeg"] = ".mp3",
        ["audio/ogg"] = ".ogg",
        ["audio/x-m4a"] = ".m4a",
        ["audio/m4a"] = ".m4a",
        ["audio/mp4"] = ".mp4",
        ["audio/aac"] = ".aac",
    };

    public static readonly HashSet<string> AllowedContentTypes = new(MimeToExtension.Keys, StringComparer.OrdinalIgnoreCase);
    public static readonly HashSet<string> AllowedExtensions = new(ExtensionToMime.Keys, StringComparer.OrdinalIgnoreCase);

    public static (bool IsValid, string? ErrorMessage) Validate(long length, string? contentType, string? fileName)
    {
        if (length <= 0)
        {
            return (false, "Audio file cannot be empty.");
        }

        if (length > MaxAudioSizeBytes)
        {
            return (false, $"Audio file size exceeds limit of {MaxAudioSizeBytes / (1024 * 1024)} MB.");
        }

        var ext = Path.GetExtension(fileName ?? string.Empty);
        var hasExt = !string.IsNullOrEmpty(ext);
        var extValid = hasExt && AllowedExtensions.Contains(ext);

        var hasMime = !string.IsNullOrEmpty(contentType);
        var mimeValid = hasMime && AllowedContentTypes.Contains(contentType!);

        if (hasExt && !extValid)
        {
            return (false, $"Invalid audio file format or extension '{ext}'. Allowed formats: {string.Join(", ", AllowedExtensions)}.");
        }

        if (hasMime && !mimeValid)
        {
            return (false, $"Invalid audio file format or MIME type '{contentType}'. Allowed formats: {string.Join(", ", AllowedContentTypes)}.");
        }

        if (!hasExt && !hasMime)
        {
            return (false, "Invalid audio file format. Allowed formats: webm, wav, mp3, ogg, m4a, mp4, aac.");
        }

        return (true, null);
    }
}

public class LocalFileStore : IFileStore
{
    private readonly string _baseDirectory;

    public LocalFileStore(string baseDirectory)
    {
        _baseDirectory = Path.GetFullPath(baseDirectory);
        if (!Directory.Exists(_baseDirectory))
        {
            Directory.CreateDirectory(_baseDirectory);
        }
    }

    public string BaseDirectory => _baseDirectory;

    private string? ResolveSafeFullPath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        // Prevent path traversal
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_baseDirectory, normalized));

        if (!fullPath.StartsWith(_baseDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return fullPath;
    }

    public async Task<string> SaveAudioAsync(
        int attemptId,
        int questionId,
        Stream stream,
        string contentType,
        string fileExtension,
        CancellationToken cancellationToken = default)
    {
        var hasFailMarker = (fileExtension?.Contains("[simulate_fail]", StringComparison.OrdinalIgnoreCase) == true);
        var marker = hasFailMarker ? "_[simulate_fail]" : "";
        var cleanExt = fileExtension?.Replace("[simulate_fail]", "", StringComparison.OrdinalIgnoreCase) ?? "";
        if (!cleanExt.StartsWith('.'))
        {
            cleanExt = "." + cleanExt;
        }

        if (string.IsNullOrEmpty(cleanExt) || cleanExt == ".")
        {
            cleanExt = GetExtensionFromContentType(contentType) ?? ".webm";
        }

        var attemptDir = Path.Combine(_baseDirectory, "attempts", attemptId.ToString());
        Directory.CreateDirectory(attemptDir);

        var safeFileName = $"q_{questionId}{marker}_{Guid.NewGuid():N}{cleanExt}";
        var fullPath = Path.Combine(attemptDir, safeFileName);

        using (var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await stream.CopyToAsync(fileStream, cancellationToken);
        }

        var relativePath = Path.Combine("attempts", attemptId.ToString(), safeFileName).Replace('\\', '/');
        return relativePath;
    }

    public async Task<string> SaveImportMediaAsync(int batchId, Stream stream, string fileExtension, CancellationToken cancellationToken = default)
    {
        var batchDir = Path.Combine(_baseDirectory, "imports", batchId.ToString());
        Directory.CreateDirectory(batchDir);

        // The author's file name is never reused on disk: only the (already allow-listed) extension.
        var safeFileName = $"{Guid.NewGuid():N}{fileExtension.ToLowerInvariant()}";
        var fullPath = Path.Combine(batchDir, safeFileName);

        using (var fileStream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await stream.CopyToAsync(fileStream, cancellationToken);
        }

        return Path.Combine("imports", batchId.ToString(), safeFileName).Replace('\\', '/');
    }

    public async Task<string> SaveExamMediaAsync(int examId, Stream stream, string fileExtension, CancellationToken cancellationToken = default)
    {
        var examDir = Path.Combine(_baseDirectory, "exams", examId.ToString());
        Directory.CreateDirectory(examDir);

        var cleanExt = fileExtension.ToLowerInvariant();
        if (!cleanExt.StartsWith('.')) cleanExt = "." + cleanExt;
        var safeFileName = $"{Guid.NewGuid():N}{cleanExt}";
        var fullPath = Path.Combine(examDir, safeFileName);

        using (var fileStream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await stream.CopyToAsync(fileStream, cancellationToken);
        }

        return Path.Combine("exams", examId.ToString(), safeFileName).Replace('\\', '/');
    }

    public Task<FileStreamResultInfo?> GetAudioAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveSafeFullPath(relativePath);
        if (fullPath is null || !File.Exists(fullPath))
        {
            return Task.FromResult<FileStreamResultInfo?>(null);
        }

        var contentType = GetContentTypeFromFileName(fullPath);
        var fileName = Path.GetFileName(fullPath);
        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);

        return Task.FromResult<FileStreamResultInfo?>(new FileStreamResultInfo(stream, contentType, fileName));
    }

    public Task<bool> DeleteAudioAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveSafeFullPath(relativePath);
        if (fullPath is not null && File.Exists(fullPath))
        {
            File.Delete(fullPath);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public static string GetContentTypeFromFileName(string path)
    {
        var ext = Path.GetExtension(path);
        return AudioValidation.ExtensionToMime.TryGetValue(ext, out var mime) ? mime
            : ImageMime.TryGetValue(ext, out var image) ? image
            : "application/octet-stream";
    }

    // Question images arrive through the import package (ADR-0007) and share this store.
    private static readonly Dictionary<string, string> ImageMime = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
    };

    public static string? GetExtensionFromContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return null;
        return AudioValidation.MimeToExtension.TryGetValue(contentType, out var ext) ? ext : null;
    }
}
