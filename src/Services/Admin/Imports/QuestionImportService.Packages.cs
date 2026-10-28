using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Services;

public sealed partial class QuestionImportService
{

    private static async Task<ImportPackage?> ReadPackageAsync(string fileName, Stream content, ImportReport report, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not (".json" or ".zip"))
        {
            report.Error(null, "Upload a .json manifest or a .zip package.");
            return null;
        }

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0)
        {
            report.Error(null, "The uploaded file is empty.");
            return null;
        }

        if (buffer.Length > MaxUploadBytes)
        {
            report.Error(null, $"The upload is larger than {MaxUploadBytes / (1024 * 1024)} MB.");
            return null;
        }

        buffer.Position = 0;
        if (extension == ".json")
        {
            if (buffer.Length > MaxManifestBytes)
            {
                report.Error(null, $"The manifest is larger than {MaxManifestBytes / (1024 * 1024)} MB.");
                return null;
            }

            var manifest = ParseManifest(buffer.ToArray(), report);
            return manifest is null ? null : new ImportPackage(manifest, null);
        }

        return ReadZip(buffer, report);
    }

    private static ImportPackage? ReadZip(MemoryStream buffer, ImportReport report)
    {
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            report.Error(null, "The upload is not a readable .zip package.");
            return null;
        }

        using (zip)
        {
            if (zip.Entries.Count > MaxPackageEntries)
            {
                report.Error(null, $"The package holds more than {MaxPackageEntries} files.");
                return null;
            }

            byte[]? manifestBytes = null;
            var media = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            long unpacked = 0;
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    continue; // directory entry
                }

                var path = NormaliseEntryPath(entry.FullName);
                var isManifest = string.Equals(path, "manifest.json", StringComparison.OrdinalIgnoreCase);
                var limit = isManifest ? MaxManifestBytes : MaxMediaFileBytes;

                // Length is what the archive claims; ReadEntry enforces the same limit on the bytes
                // actually inflated, so a lying header cannot expand past it.
                if (entry.Length > limit || unpacked + entry.Length > MaxUnpackedBytes)
                {
                    report.Error(null, $"'{path}' is too large to import.");
                    return null;
                }

                byte[] bytes;
                try
                {
                    bytes = ReadEntry(entry, limit);
                }
                catch (InvalidDataException)
                {
                    report.Error(null, $"'{path}' in the package is corrupt or too large to import.");
                    return null;
                }

                unpacked += bytes.Length;
                if (unpacked > MaxUnpackedBytes)
                {
                    report.Error(null, $"The package unpacked size exceeds {MaxUnpackedBytes / (1024 * 1024)} MB.");
                    return null;
                }

                if (isManifest)
                {
                    manifestBytes = bytes;
                }
                else if (path is not null)
                {
                    media[path] = bytes;
                }
            }

            if (manifestBytes is null)
            {
                report.Error(null, "The package has no manifest.json at its top level.");
                return null;
            }

            var manifest = ParseManifest(manifestBytes, report);
            return manifest is null ? null : new ImportPackage(manifest, media);
        }
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry, long limit)
    {
        using var source = entry.Open();
        using var target = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = source.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (target.Length + read > limit)
            {
                throw new InvalidDataException("Entry exceeds its size limit.");
            }

            target.Write(chunk, 0, read);
        }

        return target.ToArray();
    }

    private static ImportManifest? ParseManifest(byte[] bytes, ImportReport report)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<ImportManifest>(bytes, ImportManifest.Json);
            if (manifest is null)
            {
                report.Error(null, "manifest.json is empty.");
            }

            return manifest;
        }
        catch (JsonException ex)
        {
            // JsonException's message names the path and position, which is what an author needs.
            report.Error(null, $"manifest.json is not valid JSON for the ingest format: {ex.Message}");
            return null;
        }
    }

    /// <summary>"media\a.wav", "./media/a.wav" and "media/a.wav" are one entry; a path that climbs
    /// out of the package or is rooted is not a media path at all (null).</summary>
    private static string? NormaliseEntryPath(string raw)
    {
        var clean = raw.Replace('\\', '/');
        var parts = clean.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p != ".").ToList();
        if (parts.Count == 0 || parts.Any(p => p == "..") || clean.StartsWith('/') || raw.Contains(':'))
        {
            return null;
        }

        return string.Join('/', parts);
    }
}
