using System.IO.Compression;
using System.Text.Json;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Services;

public sealed partial class AdminLessonService(
    CompanionDbContext db,
    IFileStore fileStore,
    TimeProvider clock,
    ILogger<AdminLessonService>? log = null) : IAdminLessonService
{
    private const int MaxPackageEntries = 500;
    private const long MaxManifestBytes = 5L * 1024 * 1024;
    private const long MaxMediaFileBytes = 30L * 1024 * 1024;
    private const long MaxUnpackedBytes = 100L * 1024 * 1024;

    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".svg", ".webp"
    };

    private static readonly HashSet<string> AllowedCardTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "rule", "pitfall", "example", "micro_check"
    };

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}
