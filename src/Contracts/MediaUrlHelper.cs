namespace Contracts;

public static class MediaUrlHelper
{
    /// <summary>
    /// Normalizes relative storage paths (e.g. "exams/1/audio.mp3" or "imports/2/photo.jpg")
    /// into browser-accessible URL routes (e.g. "/media/exams/1/audio.mp3" or "/media/imports/2/photo.jpg").
    /// Leaves existing absolute URLs and root paths intact.
    /// </summary>
    public static string? NormalizeMediaUrl(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var trimmed = path.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("/media/", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("/audio/", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("/images/", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var noLeadingSlash = trimmed.TrimStart('/');
        if (noLeadingSlash.StartsWith("exams/", StringComparison.OrdinalIgnoreCase) ||
            noLeadingSlash.StartsWith("imports/", StringComparison.OrdinalIgnoreCase))
        {
            return "/media/" + noLeadingSlash;
        }

        return trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
    }
}
