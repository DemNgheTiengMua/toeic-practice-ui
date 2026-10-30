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

    // ---- Lossless Exam Export ----

    public async Task<(Stream Stream, string ContentType, string FileName)> ExportExamPackageAsync(int examId, string? format = "zip", CancellationToken cancellationToken = default)
    {
        var exam = await db.Exams.AsNoTracking()
            .Include(e => e.Cert)
            .FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken);

        if (exam == null)
        {
            throw new ExamNotFoundException(examId);
        }

        var groups = await db.QuestionGroups.AsNoTracking()
            .Include(g => g.Section)
            .Include(g => g.QuestionGroups)
                .ThenInclude(q => q.QuestionOptionQuestionNavigations)
            .Include(g => g.QuestionGroups)
                .ThenInclude(q => q.Category)
            .Where(g => g.ExamId == examId)
            .OrderBy(g => g.DisplayOrder)
            // Two collection levels (questions, then options): split, or EF warns 20504 and the
            // rows multiply (see the readiness audit). Children are re-ordered in memory below.
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var isZip = string.Equals(format, "zip", StringComparison.OrdinalIgnoreCase);

        var manifest = new ImportManifest
        {
            Version = "1.0",
            Exam = new ImportManifestExam
            {
                Title = exam.Name,
                Purpose = exam.Purpose,
                DeliveryMode = exam.DeliveryMode,
                DurationMinutes = exam.DurationMinutes,
                SourceName = exam.SourceName,
                Description = exam.Description,
                QuestionCount = exam.QuestionCount,
            },
            Sections = groups
                .GroupBy(g => g.Section)
                .OrderBy(sg => sg.Key.DisplayOrder)
                .Select(sg => new ImportManifestSection
                {
                    SectionCode = sg.Key.Code,
                    Groups = sg.OrderBy(g => g.DisplayOrder).Select(g =>
                    {
                        string? audioRel = null;
                        string? imageRel = null;
                        if (!string.IsNullOrWhiteSpace(g.AudioPath))
                        {
                            audioRel = $"media/{Path.GetFileName(g.AudioPath)}";
                        }
                        if (!string.IsNullOrWhiteSpace(g.ImagePath))
                        {
                            imageRel = $"media/{Path.GetFileName(g.ImagePath)}";
                        }

                        return new ImportManifestGroup
                        {
                            UnitNumber = g.UnitNumber,
                            Passage = g.Passage,
                            AudioFile = isZip ? audioRel : null,
                            AudioStartMs = g.AudioStartMs,
                            AudioEndMs = g.AudioEndMs,
                            ImageFile = isZip ? imageRel : null,
                            ImageAlt = g.ImageAlt,
                            Transcript = g.Transcript,
                            PrepSeconds = g.PrepSeconds,
                            ResponseSeconds = g.ResponseSeconds,
                            DisplayOrder = g.DisplayOrder,
                            Questions = g.QuestionGroups.OrderBy(q => q.DisplayOrder).Select(q =>
                            {
                                List<string>? acceptedAnswers = null;
                                if (!string.IsNullOrWhiteSpace(q.AcceptedAnswersJson))
                                {
                                    acceptedAnswers = AttemptEngine.ParseAcceptedAnswers(q.AcceptedAnswersJson);
                                }

                                List<string>? requiredKeywords = null;
                                if (!string.IsNullOrWhiteSpace(q.RequiredKeywords))
                                {
                                    requiredKeywords = AttemptEngine.ParseKeywords(q.RequiredKeywords);
                                }

                                return new ImportManifestQuestion
                                {
                                    Stem = q.Stem,
                                    DifficultyLevel = q.DifficultyLevel,
                                    QuestionType = q.QuestionType,
                                    CategoryCode = q.Category?.Code,
                                    Explanation = q.Explanation,
                                    Hint = q.Hint,
                                    PrepSeconds = q.PrepSeconds,
                                    ResponseSeconds = q.ResponseSeconds,
                                    MinWords = q.MinWords,
                                    MaxWords = q.MaxWords,
                                    DisplayOrder = q.DisplayOrder,
                                    MaxSelections = q.MaxSelections,
                                    AllowNumber = q.AllowNumber,
                                    AcceptedAnswers = acceptedAnswers,
                                    RequiredKeywords = requiredKeywords,
                                    Options = q.QuestionOptionQuestionNavigations.Count == 0
                                        ? null
                                        : q.QuestionOptionQuestionNavigations.OrderBy(o => o.Label).Select(o => new ImportManifestOption
                                        {
                                            Label = o.Label,
                                            Text = o.Text,
                                            IsCorrect = o.IsCorrect,
                                            Rationale = o.Rationale,
                                        }).ToList(),
                                };
                            }).ToList(),
                        };
                    }).ToList(),
                }).ToList(),
        };

        var safeTitle = string.Join("_", exam.Name.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");

        if (!isZip)
        {
            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, ImportManifest.Json);
            var ms = new MemoryStream(jsonBytes);
            return (ms, "application/json", $"{safeTitle}_manifest.json");
        }

        var zipMs = new MemoryStream();
        using (var archive = new ZipArchive(zipMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var entryStream = manifestEntry.Open())
            {
                await JsonSerializer.SerializeAsync(entryStream, manifest, ImportManifest.Json, cancellationToken);
            }

            if (fileStore != null)
            {
                var mediaPaths = groups
                    .SelectMany(g => new[] { g.AudioPath, g.ImagePath })
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct();

                foreach (var path in mediaPaths)
                {
                    var cleanPath = path!.TrimStart('/');
                    if (cleanPath.StartsWith("media/", StringComparison.OrdinalIgnoreCase))
                    {
                        cleanPath = cleanPath["media/".Length..];
                    }

                    var media = await fileStore.GetMediaAsync(cleanPath, cancellationToken);
                    if (media != null)
                    {
                        var entryName = Path.GetFileName(cleanPath);
                        var mediaEntry = archive.CreateEntry($"media/{entryName}");
                        using var mediaStream = mediaEntry.Open();
                        await media.Stream.CopyToAsync(mediaStream, cancellationToken);
                    }
                }
            }
        }

        zipMs.Position = 0;
        return (zipMs, "application/zip", $"{safeTitle}_package.zip");
    }
}
