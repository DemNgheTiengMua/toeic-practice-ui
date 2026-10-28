using System.IO.Compression;
using System.Text.Json;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Services;

public sealed partial class AdminLessonService
{

    public async Task<AdminLessonImportResultDto> ImportLessonsAsync(
        int certId,
        bool overwrite,
        Stream stream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        if (!await db.Certificates.AnyAsync(c => c.CertId == certId, cancellationToken))
        {
            throw new AdminLessonException(ApiError.Codes.CertificateUnavailable, $"Certificate {certId} not found.", 404);
        }

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext is not (".json" or ".zip"))
        {
            throw new AdminLessonException(ApiError.Codes.Validation, "Upload a .json manifest or a .zip package.", 400);
        }

        byte[]? manifestBytes = null;
        var mediaMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (ext == ".json")
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, cancellationToken);
            if (ms.Length == 0)
            {
                throw new AdminLessonException(ApiError.Codes.Validation, "The uploaded JSON file is empty.", 400);
            }
            if (ms.Length > MaxManifestBytes)
            {
                throw new AdminLessonException(ApiError.Codes.Validation, "The manifest exceeds the maximum allowed size (5MB).", 400);
            }
            manifestBytes = ms.ToArray();
        }
        else
        {
            // .zip package
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            if (buffer.Length == 0)
            {
                throw new AdminLessonException(ApiError.Codes.Validation, "The uploaded ZIP package is empty.", 400);
            }

            ZipArchive zip;
            try
            {
                zip = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true);
            }
            catch (InvalidDataException)
            {
                throw new AdminLessonException(ApiError.Codes.Validation, "The upload is not a readable .zip package.", 400);
            }

            using (zip)
            {
                if (zip.Entries.Count > MaxPackageEntries)
                {
                    throw new AdminLessonException(ApiError.Codes.Validation, $"The package contains more than {MaxPackageEntries} files.", 400);
                }

                long totalUnpacked = 0;
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                    {
                        continue;
                    }

                    var cleanPath = entry.FullName.Replace('\\', '/').TrimStart('/');
                    var isManifest = string.Equals(cleanPath, "manifest.json", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(Path.GetFileName(cleanPath), "manifest.json", StringComparison.OrdinalIgnoreCase);

                    var limit = isManifest ? MaxManifestBytes : MaxMediaFileBytes;
                    if (entry.Length > limit || totalUnpacked + entry.Length > MaxUnpackedBytes)
                    {
                        throw new AdminLessonException(ApiError.Codes.Validation, $"File '{cleanPath}' in package exceeds size limit.", 400);
                    }

                    using var entryStream = entry.Open();
                    using var entryMs = new MemoryStream();
                    await entryStream.CopyToAsync(entryMs, cancellationToken);
                    var entryBytes = entryMs.ToArray();

                    totalUnpacked += entryBytes.Length;
                    if (totalUnpacked > MaxUnpackedBytes)
                    {
                        throw new AdminLessonException(ApiError.Codes.Validation, "Total unpacked size exceeds package limit.", 400);
                    }

                    if (isManifest && manifestBytes is null)
                    {
                        manifestBytes = entryBytes;
                    }
                    else
                    {
                        var entryExt = Path.GetExtension(cleanPath).ToLowerInvariant();
                        if (AllowedImageExtensions.Contains(entryExt))
                        {
                            using var imageStream = new MemoryStream(entryBytes);
                            var savedPath = await fileStore.SaveImportMediaAsync(0, imageStream, entryExt, cancellationToken);
                            var mediaUrl = $"/api/media/{savedPath.Replace('\\', '/')}";

                            mediaMap[cleanPath] = mediaUrl;
                            mediaMap[Path.GetFileName(cleanPath)] = mediaUrl;
                        }
                    }
                }
            }

            if (manifestBytes is null)
            {
                throw new AdminLessonException(ApiError.Codes.Validation, "The package has no manifest.json file.", 400);
            }
        }

        if (manifestBytes is not null && manifestBytes.Length >= 3 && manifestBytes[0] == 0xEF && manifestBytes[1] == 0xBB && manifestBytes[2] == 0xBF)
        {
            manifestBytes = manifestBytes[3..];
        }

        LessonManifestDto manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<LessonManifestDto>(manifestBytes, ManifestJsonOptions)
                       ?? throw new JsonException("Manifest could not be deserialized.");
        }
        catch (JsonException ex)
        {
            throw new AdminLessonException(ApiError.Codes.Validation, $"Manifest JSON is malformed: {ex.Message}", 400);
        }

        if (manifest.Lessons is null || manifest.Lessons.Count == 0)
        {
            throw new AdminLessonException(ApiError.Codes.Validation, "Manifest must contain at least one lesson.", 400);
        }

        var skills = await db.Skills.Where(s => s.CertId == certId).ToListAsync(cancellationToken);
        var categories = await db.QuestionCategories.Where(qc => qc.CertId == certId).ToListAsync(cancellationToken);
        var existingLessons = await db.Lessons.Where(l => l.CertId == certId).ToListAsync(cancellationToken);

        var errors = new List<string>();

        // Pre-validate all lessons
        for (int i = 0; i < manifest.Lessons.Count; i++)
        {
            var item = manifest.Lessons[i];
            var prefix = $"Lesson #{i + 1} ('{item.Title}')";

            if (string.IsNullOrWhiteSpace(item.Title))
            {
                errors.Add($"{prefix}: Title is required.");
            }
            else if (item.Title.Length > 160)
            {
                errors.Add($"{prefix}: Title exceeds maximum 160 characters.");
            }

            if (string.IsNullOrWhiteSpace(item.Skill))
            {
                errors.Add($"{prefix}: Skill is required.");
            }
            else
            {
                var skillMatch = skills.FirstOrDefault(s =>
                    string.Equals(s.Code, item.Skill, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s.Modality, item.Skill, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s.Name, item.Skill, StringComparison.OrdinalIgnoreCase));

                if (skillMatch is null)
                {
                    errors.Add($"{prefix}: Skill '{item.Skill}' does not belong to the selected certificate.");
                }
            }

            if (!string.IsNullOrWhiteSpace(item.CategoryCode))
            {
                var catMatch = categories.FirstOrDefault(c =>
                    string.Equals(c.Code, item.CategoryCode, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.Name, item.CategoryCode, StringComparison.OrdinalIgnoreCase));

                if (catMatch is null)
                {
                    errors.Add($"{prefix}: Question category '{item.CategoryCode}' does not belong to the selected certificate.");
                }
            }

            var existingMatch = existingLessons.FirstOrDefault(l =>
                string.Equals(l.Title, item.Title?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (existingMatch is not null && !overwrite)
            {
                errors.Add($"{prefix}: A lesson with this title already exists in the certificate. Enable 'Overwrite if exists' to update it.");
            }

            if (item.Cards is null || item.Cards.Count == 0)
            {
                errors.Add($"{prefix}: Must contain at least one card.");
            }
            else
            {
                for (int c = 0; c < item.Cards.Count; c++)
                {
                    var card = item.Cards[c];
                    var cardPrefix = $"{prefix} Card #{c + 1}";

                    if (string.IsNullOrWhiteSpace(card.Type) || !AllowedCardTypes.Contains(card.Type.Trim()))
                    {
                        errors.Add($"{cardPrefix}: CardType must be 'rule', 'pitfall', 'example', or 'micro_check'.");
                    }

                    if (string.IsNullOrWhiteSpace(card.Title))
                    {
                        errors.Add($"{cardPrefix}: Title is required.");
                    }
                    else if (card.Title.Length > 160)
                    {
                        errors.Add($"{cardPrefix}: Title exceeds 160 characters.");
                    }

                    if (string.IsNullOrWhiteSpace(card.ContentMarkdown))
                    {
                        errors.Add($"{cardPrefix}: ContentMarkdown is required.");
                    }

                    if (string.Equals(card.Type?.Trim(), "micro_check", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(card.QuestionPrompt))
                        {
                            errors.Add($"{cardPrefix}: QuestionPrompt is required for micro_check.");
                        }
                        if (string.IsNullOrWhiteSpace(card.OptionA) || card.OptionA.Length > 250)
                        {
                            errors.Add($"{cardPrefix}: OptionA is required and cannot exceed 250 characters.");
                        }
                        if (string.IsNullOrWhiteSpace(card.OptionB) || card.OptionB.Length > 250)
                        {
                            errors.Add($"{cardPrefix}: OptionB is required and cannot exceed 250 characters.");
                        }
                        if (string.IsNullOrWhiteSpace(card.OptionC) || card.OptionC.Length > 250)
                        {
                            errors.Add($"{cardPrefix}: OptionC is required and cannot exceed 250 characters.");
                        }
                        if (string.IsNullOrWhiteSpace(card.OptionD) || card.OptionD.Length > 250)
                        {
                            errors.Add($"{cardPrefix}: OptionD is required and cannot exceed 250 characters.");
                        }

                        var correct = card.CorrectOption?.Trim().ToUpperInvariant();
                        if (string.IsNullOrWhiteSpace(correct) || correct is not ("A" or "B" or "C" or "D"))
                        {
                            errors.Add($"{cardPrefix}: CorrectOption must be one of 'A', 'B', 'C', or 'D'.");
                        }

                        if (string.IsNullOrWhiteSpace(card.Explanation))
                        {
                            errors.Add($"{cardPrefix}: Explanation is required for micro_check.");
                        }
                    }
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new AdminLessonException(ApiError.Codes.Validation, string.Join("; ", errors), 400);
        }

        // Apply media replacements in ContentMarkdown
        string ReplaceMediaUrls(string content)
        {
            if (mediaMap.Count == 0 || string.IsNullOrEmpty(content))
            {
                return content;
            }

            var result = content;
            foreach (var (original, replacement) in mediaMap)
            {
                result = result.Replace(original, replacement, StringComparison.OrdinalIgnoreCase);
            }
            return result;
        }

        int createdCount = 0;
        int updatedCount = 0;
        int totalCardsImported = 0;
        var messages = new List<string>();

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        foreach (var item in manifest.Lessons)
        {
            var skill = skills.First(s =>
                string.Equals(s.Code, item.Skill, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.Modality, item.Skill, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.Name, item.Skill, StringComparison.OrdinalIgnoreCase));

            int? categoryId = null;
            if (!string.IsNullOrWhiteSpace(item.CategoryCode))
            {
                var cat = categories.FirstOrDefault(c =>
                    string.Equals(c.Code, item.CategoryCode, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.Name, item.CategoryCode, StringComparison.OrdinalIgnoreCase));
                categoryId = cat?.CategoryId;
            }

            var existing = existingLessons.FirstOrDefault(l =>
                string.Equals(l.Title, item.Title?.Trim(), StringComparison.OrdinalIgnoreCase));

            Lesson lesson;
            if (existing is not null && overwrite)
            {
                lesson = existing;
                lesson.SkillId = skill.SkillId;
                lesson.CategoryId = categoryId;
                lesson.Summary = string.IsNullOrWhiteSpace(item.Summary) ? null : item.Summary.Trim();
                lesson.EstimatedMinutes = item.EstimatedMinutes > 0 ? item.EstimatedMinutes : 5;
                lesson.DisplayOrder = item.DisplayOrder;

                // Reset LastViewedCardId to avoid FK violations
                await db.LearnerLessonProgresses
                    .Where(p => p.LessonId == lesson.LessonId)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastViewedCardId, (int?)null), cancellationToken);

                var oldCards = await db.LessonCards.Where(c => c.LessonId == lesson.LessonId).ToListAsync(cancellationToken);
                db.LessonCards.RemoveRange(oldCards);
                await db.SaveChangesAsync(cancellationToken);

                updatedCount++;
                messages.Add($"Updated lesson '{lesson.Title}' with {item.Cards.Count} cards.");
            }
            else
            {
                lesson = new Lesson
                {
                    CertId = certId,
                    SkillId = skill.SkillId,
                    CategoryId = categoryId,
                    Title = item.Title.Trim(),
                    Summary = string.IsNullOrWhiteSpace(item.Summary) ? null : item.Summary.Trim(),
                    EstimatedMinutes = item.EstimatedMinutes > 0 ? item.EstimatedMinutes : 5,
                    DisplayOrder = item.DisplayOrder,
                    CreatedAt = clock.GetUtcNow(),
                };
                db.Lessons.Add(lesson);
                await db.SaveChangesAsync(cancellationToken);

                createdCount++;
                messages.Add($"Created lesson '{lesson.Title}' with {item.Cards.Count} cards.");
            }

            for (int i = 0; i < item.Cards.Count; i++)
            {
                var cardDto = item.Cards[i];
                db.LessonCards.Add(BuildLessonCard(
                    lesson.LessonId,
                    certId,
                    cardDto.Type,
                    cardDto.Title,
                    ReplaceMediaUrls(cardDto.ContentMarkdown.Trim()),
                    cardDto.QuestionPrompt,
                    cardDto.OptionA,
                    cardDto.OptionB,
                    cardDto.OptionC,
                    cardDto.OptionD,
                    cardDto.CorrectOption,
                    cardDto.Explanation,
                    cardDto.Rationale,
                    i));
            }

            totalCardsImported += item.Cards.Count;
            await db.SaveChangesAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);

        log?.LogInformation("Imported lessons for Cert {CertId}: {Created} created, {Updated} updated, {TotalCards} cards total", certId, createdCount, updatedCount, totalCardsImported);
        return new AdminLessonImportResultDto(createdCount, updatedCount, totalCardsImported, messages);
    }
}
