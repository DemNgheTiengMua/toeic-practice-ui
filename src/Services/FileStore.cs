using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Services;

public static class AudioValidation
{
    public const long MaxAudioSizeBytes = 10 * 1024 * 1024; // 10 MB

    public static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "audio/webm",
        "audio/wav",
        "audio/wave",
        "audio/x-wav",
        "audio/mp3",
        "audio/mpeg",
        "audio/ogg",
        "audio/x-m4a",
        "audio/m4a",
        "audio/mp4",
        "audio/aac"
    };

    public static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".webm",
        ".wav",
        ".mp3",
        ".ogg",
        ".m4a",
        ".mp4",
        ".aac"
    };

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
        var extValid = !string.IsNullOrEmpty(ext) && AllowedExtensions.Contains(ext);
        var mimeValid = !string.IsNullOrEmpty(contentType) && AllowedContentTypes.Contains(contentType);

        if (!extValid && !mimeValid)
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

    public Task<FileStreamResultInfo?> GetAudioAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return Task.FromResult<FileStreamResultInfo?>(null);
        }

        // Prevent path traversal
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_baseDirectory, normalized));

        if (!fullPath.StartsWith(_baseDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<FileStreamResultInfo?>(null);
        }

        if (!File.Exists(fullPath))
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
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return Task.FromResult(false);
        }

        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_baseDirectory, normalized));

        if (!fullPath.StartsWith(_baseDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(false);
        }

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public static string GetContentTypeFromFileName(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".webm" => "audio/webm",
            ".wav" => "audio/wav",
            ".mp3" => "audio/mpeg",
            ".ogg" => "audio/ogg",
            ".m4a" => "audio/mp4",
            ".mp4" => "audio/mp4",
            ".aac" => "audio/aac",
            _ => "application/octet-stream"
        };
    }

    public static string? GetExtensionFromContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return null;
        return contentType.ToLowerInvariant() switch
        {
            "audio/webm" => ".webm",
            "audio/wav" or "audio/wave" or "audio/x-wav" => ".wav",
            "audio/mp3" or "audio/mpeg" => ".mp3",
            "audio/ogg" => ".ogg",
            "audio/x-m4a" or "audio/m4a" or "audio/mp4" => ".m4a",
            "audio/aac" => ".aac",
            _ => null
        };
    }
}
