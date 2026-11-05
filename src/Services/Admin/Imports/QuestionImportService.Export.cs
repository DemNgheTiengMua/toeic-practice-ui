using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class QuestionImportService
{

    // ---- Lossless exam export ----

    /// <summary>
    /// #189: the manifest is built by <see cref="QuestionExportManifest"/>, which both exporters
    /// share. Where the pool has assembled a paper position the export writes the pool's own version,
    /// so exporting what was imported and importing it again reproduces it rather than the legacy
    /// skeleton it was also written into.
    /// </summary>
    public async Task<(Stream Stream, string ContentType, string FileName)> ExportExamPackageAsync(int examId, string? format = "zip", CancellationToken cancellationToken = default)
    {
        var exam = await db.Exams.AsNoTracking()
            .Include(e => e.Cert)
            .FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken);

        if (exam == null)
        {
            throw new ExamNotFoundException(examId);
        }

        var manifest = await QuestionExportManifest.BuildAsync(db, exam, format, cancellationToken);
        return await WritePackageAsync(manifest, exam.Name, format, fileStore, cancellationToken);
    }

    /// <summary>
    /// Serialises a built manifest, picking up the media a package needs. Shared with
    /// <see cref="AdminExamService"/> so the zip's entry names and the manifest's references cannot
    /// disagree - the one thing a re-import depends on.
    /// </summary>
    internal static async Task<(Stream Stream, string ContentType, string FileName)> WritePackageAsync(
        ImportManifest manifest,
        string examName,
        string? format,
        IFileStore? fileStore,
        CancellationToken cancellationToken)
    {
        var safeTitle = string.Join("_", examName.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
        var isZip = string.Equals(format, "zip", StringComparison.OrdinalIgnoreCase);

        if (!isZip)
        {
            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, ImportManifest.Json);
            return (new MemoryStream(jsonBytes), "application/json", $"{safeTitle}_manifest.json");
        }

        var zipMs = new MemoryStream();
        using (var archive = new ZipArchive(zipMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifestEntry = archive.CreateEntry("manifest.json");
            await using (var entryStream = manifestEntry.Open())
            {
                await JsonSerializer.SerializeAsync(entryStream, manifest, ImportManifest.Json, cancellationToken);
            }

            var mediaPaths = manifest.Sections?
                .SelectMany(s => s.Groups ?? [])
                .SelectMany(g => new[] { g.AudioFile, g.ImageFile })
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct()
                .ToList() ?? [];

            if (fileStore is not null)
            {
                foreach (var path in mediaPaths)
                {
                    var cleanPath = path!.TrimStart('/');
                    if (cleanPath.StartsWith("media/", StringComparison.OrdinalIgnoreCase))
                    {
                        cleanPath = cleanPath["media/".Length..];
                    }

                    var media = await fileStore.GetMediaAsync(cleanPath, cancellationToken);
                    if (media is null)
                    {
                        continue;
                    }

                    var mediaEntry = archive.CreateEntry($"media/{Path.GetFileName(cleanPath)}");
                    await using var mediaStream = mediaEntry.Open();
                    await media.Stream.CopyToAsync(mediaStream, cancellationToken);
                }
            }
        }

        zipMs.Position = 0;
        return (zipMs, "application/zip", $"{safeTitle}_package.zip");
    }
}
