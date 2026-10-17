using System.IO.Compression;
using System.Text.Json;
using Contracts;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Services;

public sealed class AdminLessonService(
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

    public async Task<IReadOnlyList<AdminLessonSummaryDto>> GetLessonsAsync(int? certId = null, int? skillId = null, CancellationToken cancellationToken = default)
    {
        var query = db.Lessons.AsNoTracking().AsQueryable();

        if (certId.HasValue)
        {
            query = query.Where(l => l.CertId == certId.Value);
        }

        if (skillId.HasValue)
        {
            query = query.Where(l => l.SkillId == skillId.Value);
        }

        return await query
            .OrderBy(l => l.DisplayOrder)
            .ThenBy(l => l.LessonId)
            .Select(l => new AdminLessonSummaryDto(
                l.LessonId,
                l.CertId,
                l.Cert.Code,
                l.SkillId,
                l.Skill.Code,
                l.Skill.Name,
                l.CategoryId,
                l.Category != null ? l.Category.Code : null,
                l.Category != null ? l.Category.Name : null,
                l.Title,
                l.Summary,
                l.EstimatedMinutes,
                l.DisplayOrder,
                l.LessonCards.Count,
                l.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<AdminLessonDetailDto?> GetLessonAsync(int lessonId, CancellationToken cancellationToken = default)
    {
        var lesson = await db.Lessons.AsNoTracking()
            .Include(l => l.Cert)
            .Include(l => l.Skill)
            .Include(l => l.Category)
            .Include(l => l.LessonCards)
            .FirstOrDefaultAsync(l => l.LessonId == lessonId, cancellationToken);

        if (lesson is null)
        {
            return null;
        }

        var cards = lesson.LessonCards
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.CardId)
            .Select(c => new LessonCardDto(
                c.CardId,
                c.CardType,
                c.Title,
                c.ContentMarkdown,
                c.QuestionPrompt,
                c.OptionA,
                c.OptionB,
                c.OptionC,
                c.OptionD,
                c.CorrectOption,
                c.Explanation,
                c.DisplayOrder,
                c.Rationale))
            .ToList();

        return new AdminLessonDetailDto(
            lesson.LessonId,
            lesson.CertId,
            lesson.Cert.Code,
            lesson.SkillId,
            lesson.Skill.Code,
            lesson.CategoryId,
            lesson.Category?.Code,
            lesson.Title,
            lesson.Summary,
            lesson.EstimatedMinutes,
            lesson.DisplayOrder,
            lesson.CreatedAt,
            cards);
    }

    public async Task<AdminLessonDetailDto> CreateLessonAsync(AdminLessonUpsertDto request, CancellationToken cancellationToken = default)
    {
        await ValidateUpsertAsync(request, cancellationToken);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var lesson = new Lesson
        {
            CertId = request.CertId,
            SkillId = request.SkillId,
            CategoryId = request.CategoryId,
            Title = request.Title.Trim(),
            Summary = string.IsNullOrWhiteSpace(request.Summary) ? null : request.Summary.Trim(),
            EstimatedMinutes = request.EstimatedMinutes,
            DisplayOrder = request.DisplayOrder,
            CreatedAt = clock.GetUtcNow(),
        };

        db.Lessons.Add(lesson);
        await db.SaveChangesAsync(cancellationToken);

        for (int i = 0; i < request.Cards.Count; i++)
        {
            var cardDto = request.Cards[i];
            var card = new LessonCard
            {
                LessonId = lesson.LessonId,
                CertId = lesson.CertId,
                CardType = cardDto.CardType.Trim().ToLowerInvariant(),
                Title = cardDto.Title.Trim(),
                ContentMarkdown = cardDto.ContentMarkdown.Trim(),
                QuestionPrompt = string.IsNullOrWhiteSpace(cardDto.QuestionPrompt) ? null : cardDto.QuestionPrompt.Trim(),
                OptionA = string.IsNullOrWhiteSpace(cardDto.OptionA) ? null : cardDto.OptionA.Trim(),
                OptionB = string.IsNullOrWhiteSpace(cardDto.OptionB) ? null : cardDto.OptionB.Trim(),
                OptionC = string.IsNullOrWhiteSpace(cardDto.OptionC) ? null : cardDto.OptionC.Trim(),
                OptionD = string.IsNullOrWhiteSpace(cardDto.OptionD) ? null : cardDto.OptionD.Trim(),
                CorrectOption = string.IsNullOrWhiteSpace(cardDto.CorrectOption) ? null : cardDto.CorrectOption.Trim().ToUpperInvariant(),
                Explanation = string.IsNullOrWhiteSpace(cardDto.Explanation) ? null : cardDto.Explanation.Trim(),
                Rationale = string.IsNullOrWhiteSpace(cardDto.Rationale) ? null : cardDto.Rationale.Trim(),
                DisplayOrder = i,
            };
            db.LessonCards.Add(card);
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        log?.LogInformation("Created lesson {LessonId} '{Title}' for Cert {CertId}", lesson.LessonId, lesson.Title, lesson.CertId);
        return (await GetLessonAsync(lesson.LessonId, cancellationToken))!;
    }

    public async Task<AdminLessonDetailDto> UpdateLessonAsync(int lessonId, AdminLessonUpsertDto request, CancellationToken cancellationToken = default)
    {
        var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.LessonId == lessonId, cancellationToken);
        if (lesson is null)
        {
            throw new AdminLessonException(ApiError.Codes.LessonNotFound, $"Lesson {lessonId} was not found.", 404);
        }

        if (lesson.CertId != request.CertId)
        {
            throw new AdminLessonException(ApiError.Codes.Validation, "Certificate cannot be changed for an existing lesson.", 400);
        }

        await ValidateUpsertAsync(request, cancellationToken);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        lesson.SkillId = request.SkillId;
        lesson.CategoryId = request.CategoryId;
        lesson.Title = request.Title.Trim();
        lesson.Summary = string.IsNullOrWhiteSpace(request.Summary) ? null : request.Summary.Trim();
        lesson.EstimatedMinutes = request.EstimatedMinutes;
        lesson.DisplayOrder = request.DisplayOrder;

        // Reset LastViewedCardId to avoid FK violations when removing existing cards
        await db.LearnerLessonProgresses
            .Where(p => p.LessonId == lessonId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastViewedCardId, (int?)null), cancellationToken);

        var existingCards = await db.LessonCards.Where(c => c.LessonId == lessonId).ToListAsync(cancellationToken);
        db.LessonCards.RemoveRange(existingCards);
        await db.SaveChangesAsync(cancellationToken);

        for (int i = 0; i < request.Cards.Count; i++)
        {
            var cardDto = request.Cards[i];
            var card = new LessonCard
            {
                LessonId = lesson.LessonId,
                CertId = lesson.CertId,
                CardType = cardDto.CardType.Trim().ToLowerInvariant(),
                Title = cardDto.Title.Trim(),
                ContentMarkdown = cardDto.ContentMarkdown.Trim(),
                QuestionPrompt = string.IsNullOrWhiteSpace(cardDto.QuestionPrompt) ? null : cardDto.QuestionPrompt.Trim(),
                OptionA = string.IsNullOrWhiteSpace(cardDto.OptionA) ? null : cardDto.OptionA.Trim(),
                OptionB = string.IsNullOrWhiteSpace(cardDto.OptionB) ? null : cardDto.OptionB.Trim(),
                OptionC = string.IsNullOrWhiteSpace(cardDto.OptionC) ? null : cardDto.OptionC.Trim(),
                OptionD = string.IsNullOrWhiteSpace(cardDto.OptionD) ? null : cardDto.OptionD.Trim(),
                CorrectOption = string.IsNullOrWhiteSpace(cardDto.CorrectOption) ? null : cardDto.CorrectOption.Trim().ToUpperInvariant(),
                Explanation = string.IsNullOrWhiteSpace(cardDto.Explanation) ? null : cardDto.Explanation.Trim(),
                Rationale = string.IsNullOrWhiteSpace(cardDto.Rationale) ? null : cardDto.Rationale.Trim(),
                DisplayOrder = i,
            };
            db.LessonCards.Add(card);
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        log?.LogInformation("Updated lesson {LessonId} '{Title}'", lesson.LessonId, lesson.Title);
        return (await GetLessonAsync(lesson.LessonId, cancellationToken))!;
    }

    public async Task DeleteLessonAsync(int lessonId, CancellationToken cancellationToken = default)
    {
        var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.LessonId == lessonId, cancellationToken);
        if (lesson is null)
        {
            throw new AdminLessonException(ApiError.Codes.LessonNotFound, $"Lesson {lessonId} was not found.", 404);
        }

        var isReferencedInPath = await db.PathModuleItems.AnyAsync(p => p.RefLessonId == lessonId, cancellationToken);
        if (isReferencedInPath)
        {
            throw new AdminLessonException(
                ApiError.Codes.Validation,
                "Cannot delete lesson because it is referenced by an active learning path item. Remove it from the path template first.",
                400);
        }

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var progresses = await db.LearnerLessonProgresses.Where(p => p.LessonId == lessonId).ToListAsync(cancellationToken);
        db.LearnerLessonProgresses.RemoveRange(progresses);

        var cards = await db.LessonCards.Where(c => c.LessonId == lessonId).ToListAsync(cancellationToken);
        db.LessonCards.RemoveRange(cards);

        db.Lessons.Remove(lesson);

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        log?.LogInformation("Deleted lesson {LessonId}", lessonId);
    }

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
                var card = new LessonCard
                {
                    LessonId = lesson.LessonId,
                    CertId = certId,
                    CardType = cardDto.Type.Trim().ToLowerInvariant(),
                    Title = cardDto.Title.Trim(),
                    ContentMarkdown = ReplaceMediaUrls(cardDto.ContentMarkdown.Trim()),
                    QuestionPrompt = string.IsNullOrWhiteSpace(cardDto.QuestionPrompt) ? null : cardDto.QuestionPrompt.Trim(),
                    OptionA = string.IsNullOrWhiteSpace(cardDto.OptionA) ? null : cardDto.OptionA.Trim(),
                    OptionB = string.IsNullOrWhiteSpace(cardDto.OptionB) ? null : cardDto.OptionB.Trim(),
                    OptionC = string.IsNullOrWhiteSpace(cardDto.OptionC) ? null : cardDto.OptionC.Trim(),
                    OptionD = string.IsNullOrWhiteSpace(cardDto.OptionD) ? null : cardDto.OptionD.Trim(),
                    CorrectOption = string.IsNullOrWhiteSpace(cardDto.CorrectOption) ? null : cardDto.CorrectOption.Trim().ToUpperInvariant(),
                    Explanation = string.IsNullOrWhiteSpace(cardDto.Explanation) ? null : cardDto.Explanation.Trim(),
                    Rationale = string.IsNullOrWhiteSpace(cardDto.Rationale) ? null : cardDto.Rationale.Trim(),
                    DisplayOrder = i,
                };
                db.LessonCards.Add(card);
            }

            totalCardsImported += item.Cards.Count;
            await db.SaveChangesAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);

        log?.LogInformation("Imported lessons for Cert {CertId}: {Created} created, {Updated} updated, {TotalCards} cards total", certId, createdCount, updatedCount, totalCardsImported);
        return new AdminLessonImportResultDto(createdCount, updatedCount, totalCardsImported, messages);
    }

    public async Task<LessonManifestDto> ExportLessonAsync(int lessonId, CancellationToken cancellationToken = default)
    {
        var lesson = await db.Lessons.AsNoTracking()
            .Include(l => l.Skill)
            .Include(l => l.Category)
            .Include(l => l.LessonCards)
            .FirstOrDefaultAsync(l => l.LessonId == lessonId, cancellationToken);

        if (lesson is null)
        {
            throw new AdminLessonException(ApiError.Codes.LessonNotFound, $"Lesson {lessonId} was not found.", 404);
        }

        var cards = lesson.LessonCards
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.CardId)
            .Select(c => new LessonManifestCardDto(
                c.CardType,
                c.Title,
                c.ContentMarkdown,
                c.QuestionPrompt,
                c.OptionA,
                c.OptionB,
                c.OptionC,
                c.OptionD,
                c.CorrectOption,
                c.Explanation,
                c.Rationale))
            .ToList();

        var item = new LessonManifestItemDto(
            lesson.Title,
            lesson.Skill.Code,
            lesson.Category?.Code,
            lesson.Summary,
            lesson.EstimatedMinutes,
            lesson.DisplayOrder,
            cards);

        return new LessonManifestDto(new[] { item });
    }

    public LessonManifestDto GetTemplate()
    {
        return new LessonManifestDto(new[]
        {
            new LessonManifestItemDto(
                "Present Simple vs Present Continuous",
                "reading",
                "GRAMMAR",
                "Master when to use present simple for habits and present continuous for ongoing actions.",
                5,
                0,
                new List<LessonManifestCardDto>
                {
                    new(
                        "rule",
                        "The Core Principle",
                        "### Present Simple vs Present Continuous\n\nUse **Present Simple** for habits, routines, and permanent states.\n\nUse **Present Continuous** for actions happening right now or temporary situations."
                    ),
                    new(
                        "pitfall",
                        "Common Traps: Stative Verbs",
                        "### Beware of Stative Verbs!\n\nVerbs of perception, emotion, and possession (e.g., *know*, *like*, *belong*, *understand*) are rarely used in continuous tenses.\n\n- ❌ *I am knowing the answer.*\n- ✔️ *I know the answer.*"
                    ),
                    new(
                        "example",
                        "Real Exam Context",
                        "### Workplace Scenario\n\n*\"The accounting department **audits** (Present Simple) our branch annually, but they **are auditing** (Present Continuous) this week due to regulatory changes.\"*"
                    ),
                    new(
                        "micro_check",
                        "Quick Check",
                        "Select the correct verb form for the sentence.",
                        QuestionPrompt: "Please quiet down; Ms. Vance _______ a confidential conference call with overseas investors.",
                        OptionA: "conducts",
                        OptionB: "is conducting",
                        OptionC: "conducted",
                        OptionD: "has conducted",
                        CorrectOption: "B",
                        Explanation: "The prompt 'Please quiet down' indicates an action taking place at the present moment, requiring the present continuous tense ('is conducting').",
                        Rationale: "Option A is simple present (habitual, not in-progress). Options C and D are past and perfect tenses, which do not fit the urgent immediate context."
                    )
                }
            )
        });
    }

    private async Task ValidateUpsertAsync(AdminLessonUpsertDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new AdminLessonException(ApiError.Codes.Validation, "Title is required.", 400);
        }

        if (request.Title.Length > 160)
        {
            throw new AdminLessonException(ApiError.Codes.Validation, "Title cannot exceed 160 characters.", 400);
        }

        if (request.EstimatedMinutes < 1)
        {
            throw new AdminLessonException(ApiError.Codes.Validation, "Estimated minutes must be at least 1.", 400);
        }

        if (!await db.Certificates.AnyAsync(c => c.CertId == request.CertId, cancellationToken))
        {
            throw new AdminLessonException(ApiError.Codes.CertificateUnavailable, $"Certificate {request.CertId} not found.", 404);
        }

        if (!await db.Skills.AnyAsync(s => s.SkillId == request.SkillId && s.CertId == request.CertId, cancellationToken))
        {
            throw new AdminLessonException(ApiError.Codes.Validation, "Skill does not belong to the selected certificate.", 400);
        }

        if (request.CategoryId.HasValue &&
            !await db.QuestionCategories.AnyAsync(c => c.CategoryId == request.CategoryId.Value && c.CertId == request.CertId, cancellationToken))
        {
            throw new AdminLessonException(ApiError.Codes.Validation, "Category does not belong to the selected certificate.", 400);
        }

        if (request.Cards is null || request.Cards.Count == 0)
        {
            throw new AdminLessonException(ApiError.Codes.Validation, "At least one card is required.", 400);
        }

        for (int i = 0; i < request.Cards.Count; i++)
        {
            var card = request.Cards[i];
            var cardNum = i + 1;

            if (string.IsNullOrWhiteSpace(card.CardType) || !AllowedCardTypes.Contains(card.CardType.Trim()))
            {
                throw new AdminLessonException(ApiError.Codes.Validation, $"Card #{cardNum}: CardType must be 'rule', 'pitfall', 'example', or 'micro_check'.", 400);
            }

            if (string.IsNullOrWhiteSpace(card.Title))
            {
                throw new AdminLessonException(ApiError.Codes.Validation, $"Card #{cardNum}: Title is required.", 400);
            }

            if (card.Title.Length > 160)
            {
                throw new AdminLessonException(ApiError.Codes.Validation, $"Card #{cardNum}: Title cannot exceed 160 characters.", 400);
            }

            if (string.IsNullOrWhiteSpace(card.ContentMarkdown))
            {
                throw new AdminLessonException(ApiError.Codes.Validation, $"Card #{cardNum}: ContentMarkdown is required.", 400);
            }

            if (string.Equals(card.CardType.Trim(), "micro_check", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(card.QuestionPrompt))
                {
                    throw new AdminLessonException(ApiError.Codes.Validation, $"Card #{cardNum}: QuestionPrompt is required for micro_check.", 400);
                }

                if (string.IsNullOrWhiteSpace(card.OptionA) || card.OptionA.Length > 250)
                {
                    throw new AdminLessonException(ApiError.Codes.Validation, $"Card #{cardNum}: OptionA is required and cannot exceed 250 characters.", 400);
                }

                if (string.IsNullOrWhiteSpace(card.OptionB) || card.OptionB.Length > 250)
                {
                    throw new AdminLessonException(ApiError.Codes.Validation, $"Card #{cardNum}: OptionB is required and cannot exceed 250 characters.", 400);
                }

                if (string.IsNullOrWhiteSpace(card.OptionC) || card.OptionC.Length > 250)
                {
                    throw new AdminLessonException(ApiError.Codes.Validation, $"Card #{cardNum}: OptionC is required and cannot exceed 250 characters.", 400);
                }

                if (string.IsNullOrWhiteSpace(card.OptionD) || card.OptionD.Length > 250)
                {
                    throw new AdminLessonException(ApiError.Codes.Validation, $"Card #{cardNum}: OptionD is required and cannot exceed 250 characters.", 400);
                }

                var correct = card.CorrectOption?.Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(correct) || correct is not ("A" or "B" or "C" or "D"))
                {
                    throw new AdminLessonException(ApiError.Codes.Validation, $"Card #{cardNum}: CorrectOption must be one of 'A', 'B', 'C', or 'D'.", 400);
                }

                if (string.IsNullOrWhiteSpace(card.Explanation))
                {
                    throw new AdminLessonException(ApiError.Codes.Validation, $"Card #{cardNum}: Explanation is required for micro_check.", 400);
                }
            }
        }
    }
}
