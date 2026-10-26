using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BusinessObjects;
using Contracts;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Services;

public interface IQuestionImportService
{
    /// <summary>Validates one upload (.json manifest or .zip package, ADR-0007) as a whole and, only if
    /// nothing is wrong, creates one draft exam from it. Every call that names a real certificate
    /// leaves an ImportBatches row and its ImportLog lines, whatever the outcome.</summary>
    Task<AdminImportResultDto> ImportAsync(int adminId, int certId, string sourceName, Stream content, CancellationToken cancellationToken = default);

    Task<AdminImportResultDto> ImportManifestAsync(int adminId, int certId, string manifestJson, CancellationToken cancellationToken = default);

    Task<AdminImportResultDto> ImportPackageZipAsync(int adminId, int certId, Stream zipStream, string fileName = "package.zip", CancellationToken cancellationToken = default);

    Task<AdminImportBatchesDto> GetBatchesAsync(int? certId, CancellationToken cancellationToken = default);

    Task<AdminImportBatchDetailDto> GetBatchAsync(int batchId, CancellationToken cancellationToken = default);

    Task<(Stream Stream, string ContentType, string FileName)> GenerateManifestTemplateAsync(int certId, CancellationToken cancellationToken = default);

    Task<(Stream Stream, string ContentType, string FileName)> ExportExamPackageAsync(int examId, string? format = "zip", CancellationToken cancellationToken = default);
}

public sealed class QuestionImportService(
    CompanionDbContext db,
    IFileStore fileStore,
    TimeProvider clock,
    ILogger<QuestionImportService>? log = null) : IQuestionImportService
{
    public const long MaxUploadBytes = 100L * 1024 * 1024;
    private const long MaxManifestBytes = 5L * 1024 * 1024;
    private const long MaxMediaFileBytes = 60L * 1024 * 1024;
    private const long MaxUnpackedBytes = 200L * 1024 * 1024;
    private const int MaxPackageEntries = 500;

    /// <summary>Imported media is stored under imports/ and referenced from the question rows by the
    /// URL the MVC site serves it at (it forwards /media/imports/... to the API).</summary>
    public const string MediaUrlPrefix = "/media/";

    private static readonly string[] AudioExtensions = [".mp3", ".wav"];
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png"];

    public async Task<AdminImportResultDto> ImportAsync(int adminId, int certId, string sourceName, Stream content, CancellationToken cancellationToken = default)
    {
        if (!await db.Certificates.AnyAsync(c => c.CertId == certId, cancellationToken))
        {
            throw new AdminExamException(ApiError.Codes.CertificateUnavailable, $"Certificate with id {certId} not found.", 404);
        }

        var fileName = Path.GetFileName(sourceName?.Trim() ?? string.Empty);
        if (fileName.Length == 0)
        {
            fileName = "upload";
        }

        var batch = new ImportBatch
        {
            CertId = certId,
            SourceName = fileName.Length > 260 ? fileName[..260] : fileName,
            Status = ImportBatchStatus.Pending.ToCode(),
            UploadedBy = adminId,
            CreatedAt = clock.GetUtcNow(),
        };
        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync(cancellationToken);

        var report = new ImportReport();
        var package = await ReadPackageAsync(fileName, content, report, cancellationToken);
        if (package is null)
        {
            return await FailAsync(batch, report, cancellationToken);
        }

        var plan = await ValidateAsync(certId, package, report, cancellationToken);
        if (plan is null || report.ErrorCount > 0)
        {
            return await FailAsync(batch, report, cancellationToken);
        }

        return await CommitAsync(batch, plan, package, report, cancellationToken);
    }

    public Task<AdminImportResultDto> ImportManifestAsync(int adminId, int certId, string manifestJson, CancellationToken cancellationToken = default) =>
        ImportAsync(adminId, certId, "manifest.json", new MemoryStream(Encoding.UTF8.GetBytes(manifestJson)), cancellationToken);

    public Task<AdminImportResultDto> ImportPackageZipAsync(int adminId, int certId, Stream zipStream, string fileName = "package.zip", CancellationToken cancellationToken = default) =>
        ImportAsync(adminId, certId, fileName, zipStream, cancellationToken);

    // ---- Reading the upload ----

    /// <summary>The manifest plus, for a .zip, its media entries keyed by their normalised path.</summary>
    private sealed record ImportPackage(ImportManifest Manifest, IReadOnlyDictionary<string, byte[]>? Media);

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

    // ---- Validating the manifest ----

    private sealed record PlannedExam(
        string Name,
        ExamPurpose Purpose,
        string DeliveryMode,
        int DurationMinutes,
        string? SourceName,
        string? Description,
        int? QuestionCount);

    private sealed record PlannedGroup(
        Section Section,
        ImportManifestGroup Source,
        int DisplayOrder,
        IReadOnlyList<PlannedQuestion> Questions);

    private sealed record PlannedQuestion(
        ImportManifestQuestion Source,
        QuestionType Type,
        int? CategoryId,
        int DisplayOrder,
        string? AcceptedAnswersJson,
        string? RequiredKeywords);

    private sealed record ImportPlan(PlannedExam Exam, IReadOnlyList<PlannedGroup> Groups, IReadOnlyList<string> MediaPaths)
    {
        public int QuestionCount => Groups.Sum(g => g.Questions.Count);
    }

    /// <summary>Checks the whole manifest against ADR-0007 and the catalog of <paramref name="certId"/>,
    /// recording every problem rather than stopping at the first. Each rule mirrors a schema CHECK,
    /// unique index or trigger, so a valid plan never reaches the database as a constraint error.</summary>
    private async Task<ImportPlan?> ValidateAsync(int certId, ImportPackage package, ImportReport report, CancellationToken cancellationToken)
    {
        var manifest = package.Manifest;
        if (manifest.Version is not null && manifest.Version != "1.0")
        {
            report.Error(null, $"Unsupported manifest version '{manifest.Version}'; this importer reads version 1.0.");
        }

        var exam = await ValidateExamAsync(certId, manifest.Exam, report, cancellationToken);

        var sections = await db.Sections.AsNoTracking().Where(s => s.CertId == certId).ToListAsync(cancellationToken);
        var categories = await db.QuestionCategories.AsNoTracking().Where(c => c.CertId == certId)
            .ToDictionaryAsync(c => c.Code, c => c.CategoryId, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var criteria = (await db.GradingCriteria.AsNoTracking().Where(c => c.CertId == certId)
                .Select(c => new { c.SkillId, c.Code }).ToListAsync(cancellationToken))
            .ToLookup(c => c.SkillId, c => c.Code);

        if (manifest.Sections is not { Count: > 0 })
        {
            report.Error(null, "The manifest has no sections.");
            return null;
        }

        var groups = new List<PlannedGroup>();
        var mediaPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var row = 0;
        var groupOrder = 0;
        var modalityErrors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (sectionSource, sectionIndex) in manifest.Sections.Select((s, i) => (s, i + 1)))
        {
            var code = sectionSource.SectionCode?.Trim();
            var where = $"Section {(string.IsNullOrEmpty(code) ? $"#{sectionIndex}" : code)}";
            var section = ResolveSection(sections, code, where, report);

            if (section is not null && exam is not null && !modalityErrors.Contains(section.Code))
            {
                var required = exam.Purpose switch
                {
                    ExamPurpose.SpeakingPractice => Modality.Speaking,
                    ExamPurpose.WritingPractice => Modality.Writing,
                    _ => (Modality?)null,
                };
                if (required is { } modality && section.Modality != modality.ToCode())
                {
                    modalityErrors.Add(section.Code);
                    report.Error(null, $"A {exam.Purpose.ToCode()} exam can only hold {modality.ToCode()} sections; {section.Code} is {section.Modality}.");
                }
            }

            if (sectionSource.Groups is not { Count: > 0 })
            {
                report.Error(null, $"{where}: the section has no groups.");
                continue;
            }

            // Blueprint cluster size validation
            if (section is not null && section.ClusterSize > 1)
            {
                foreach (var (groupSource, groupIndex) in sectionSource.Groups.Select((g, i) => (g, i + 1)))
                {
                    var qCount = groupSource.Questions?.Count ?? 0;
                    if (qCount != section.ClusterSize)
                    {
                        report.Error(null, $"{where}, group {groupIndex}: Section '{section.Code}' requires {section.ClusterSize} questions per group cluster, but found {qCount}.");
                    }
                }
            }

            foreach (var (groupSource, groupIndex) in sectionSource.Groups.Select((g, i) => (g, i + 1)))
            {
                groupOrder++;
                var groupWhere = $"{where}, group {groupIndex}";
                ValidateGroup(groupSource, section, groupWhere, package.Media, mediaPaths, report);

                if (groupSource.Questions is not { Count: > 0 })
                {
                    report.Error(null, $"{groupWhere}: the group has no questions.");
                    continue;
                }

                var questions = new List<PlannedQuestion>();
                foreach (var (questionSource, questionIndex) in groupSource.Questions.Select((q, i) => (q, i + 1)))
                {
                    row++;
                    var planned = ValidateQuestion(questionSource, $"{groupWhere}, question {questionIndex}", row, questionIndex, section, categories, criteria, report);
                    if (planned is not null)
                    {
                        questions.Add(planned);
                    }
                }

                if (section is not null)
                {
                    groups.Add(new PlannedGroup(section, groupSource, groupSource.DisplayOrder ?? groupOrder, questions));
                }
            }
        }

        return exam is null ? null : new ImportPlan(exam, groups, mediaPaths.ToList());
    }

    private async Task<PlannedExam?> ValidateExamAsync(int certId, ImportManifestExam? source, ImportReport report, CancellationToken cancellationToken)
    {
        if (source is null)
        {
            report.Error(null, "The manifest has no exam header.");
            return null;
        }

        var errorsBefore = report.ErrorCount;
        var title = source.Title?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            report.Error(null, "Exam: title is required.");
        }
        else if (title.Length > 160)
        {
            report.Error(null, "Exam: title cannot exceed 160 characters.");
        }
        else if (await db.Exams.AnyAsync(e => e.CertId == certId && e.Name == title && !e.IsDeleted, cancellationToken))
        {
            report.Error(null, $"Exam: this certificate already has an exam named '{title}'. Rename it in the manifest or delete the existing exam.");
        }

        var purpose = ExamPurpose.Mock;
        if (source.Purpose is not null && !EnumCodes.TryParse(source.Purpose.Trim(), out purpose))
        {
            report.Error(null, $"Exam: Unknown exam purpose '{source.Purpose}'; use {string.Join(", ", Enum.GetValues<ExamPurpose>().Select(p => p.ToCode()))}.");
        }

        var deliveryMode = "strict_exam";
        if (!string.IsNullOrWhiteSpace(source.DeliveryMode))
        {
            var dm = source.DeliveryMode.Trim().ToLowerInvariant();
            if (dm is not ("strict_exam" or "practice"))
            {
                report.Error(null, $"Exam: Unknown deliveryMode '{source.DeliveryMode}'; use strict_exam or practice.");
            }
            else
            {
                deliveryMode = dm;
            }
        }

        if (source.DurationMinutes is not > 0)
        {
            report.Error(null, "Exam: durationMinutes must be greater than 0.");
        }

        if (source.QuestionCount is <= 0)
        {
            report.Error(null, "Exam: questionCount, when given, must be greater than 0.");
        }

        var sourceName = Normalise(source.SourceName);
        if (sourceName?.Length > 120)
        {
            report.Error(null, "Exam: sourceName cannot exceed 120 characters.");
        }

        var description = Normalise(source.Description);
        if (description?.Length > 500)
        {
            report.Error(null, "Exam: description cannot exceed 500 characters.");
        }

        return report.ErrorCount > errorsBefore
            ? null
            : new PlannedExam(title!, purpose, deliveryMode, source.DurationMinutes!.Value, sourceName, description, source.QuestionCount);
    }

    private static Section? ResolveSection(List<Section> sections, string? code, string where, ImportReport report)
    {
        if (string.IsNullOrEmpty(code))
        {
            report.Error(null, $"{where}: sectionCode is required.");
            return null;
        }

        var matches = sections.Where(s => string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase)).ToList();
        switch (matches.Count)
        {
            case 1:
                return matches[0];
            case 0:
                var known = string.Join(", ", sections.OrderBy(s => s.Modality).ThenBy(s => s.DisplayOrder).Select(s => s.Code).Distinct());
                report.Error(null, $"Unknown section '{code}' for this certificate. Known sections: {known}.");
                return null;
            default:
                report.Error(null, $"Section code '{code}' is used by more than one skill in this certificate, so it cannot be imported by code.");
                return null;
        }
    }

    private static void ValidateGroup(
        ImportManifestGroup group,
        Section? section,
        string where,
        IReadOnlyDictionary<string, byte[]>? media,
        HashSet<string> mediaPaths,
        ImportReport report)
    {
        if (group.UnitNumber is { } unit && unit <= 0)
        {
            report.Error(null, $"{where}: unitNumber must be greater than 0.");
        }

        ValidateMedia(group.AudioFile, "audioFile", AudioExtensions, where, media, mediaPaths, report);
        ValidateMedia(group.ImageFile, "imageFile", ImageExtensions, where, media, mediaPaths, report);

        // Section media blueprint requirements
        if (section is not null)
        {
            if (section.RequirePassage && string.IsNullOrWhiteSpace(group.Passage))
            {
                report.Error(null, $"{where}: Section '{section.Code}' requires a reading passage.");
            }

            if (section.RequireAudio && string.IsNullOrWhiteSpace(group.AudioFile))
            {
                report.Error(null, $"{where}: Section '{section.Code}' requires an audio file.");
            }

            if (section.RequireImage && string.IsNullOrWhiteSpace(group.ImageFile))
            {
                report.Error(null, $"{where}: Section '{section.Code}' requires an image file.");
            }
        }

        // CK_QGroups_ImageAlt
        if (group.ImageFile is not null && string.IsNullOrWhiteSpace(group.ImageAlt))
        {
            report.Error(null, $"{where}: imageAlt is required when imageFile is given.");
        }

        if (group.ImageAlt?.Length > 400)
        {
            report.Error(null, $"{where}: imageAlt cannot exceed 400 characters.");
        }

        // CK_QGroups_AudioSlice
        if (group.AudioStartMs.HasValue != group.AudioEndMs.HasValue)
        {
            report.Error(null, $"{where}: give both audioStartMs and audioEndMs, or neither.");
        }
        else if (group.AudioStartMs is { } start && group.AudioEndMs is { } end && (start < 0 || end <= start))
        {
            report.Error(null, $"{where}: audioStartMs must be 0 or more and audioEndMs must be after it.");
        }

        // CK_QGroups_Prep / CK_QGroups_Response
        if (group.PrepSeconds is < 0)
        {
            report.Error(null, $"{where}: prepSeconds cannot be negative.");
        }

        if (group.ResponseSeconds is <= 0)
        {
            report.Error(null, $"{where}: responseSeconds must be greater than 0.");
        }
    }

    private static void ValidateMedia(string? file, string field, string[] allowed, string where, IReadOnlyDictionary<string, byte[]>? media, HashSet<string> mediaPaths, ImportReport report)
    {
        if (file is null)
        {
            return;
        }

        var path = NormaliseEntryPath(file);
        if (path is null || !path.StartsWith("media/", StringComparison.OrdinalIgnoreCase) || path.Length == "media/".Length)
        {
            report.Error(null, $"{where}: {field} '{file}' must be a file inside media/.");
            return;
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (!allowed.Contains(extension))
        {
            report.Error(null, $"{where}: {field} '{file}' must be one of {string.Join(", ", allowed)}.");
            return;
        }

        if (media is null)
        {
            report.Error(null, $"{where}: Media files need a .zip package; '{file}' cannot be read from a plain .json upload.");
            return;
        }

        if (!media.ContainsKey(path))
        {
            report.Error(null, $"{where}: {field} '{file}' is not in the package.");
            return;
        }

        mediaPaths.Add(path);
    }

    private static PlannedQuestion? ValidateQuestion(
        ImportManifestQuestion question,
        string where,
        int row,
        int questionIndex,
        Section? section,
        Dictionary<string, int> categories,
        ILookup<int, string> criteria,
        ImportReport report)
    {
        var errorsBefore = report.ErrorCount;
        void Fail(string message) => report.Error(row, $"{where}: {message}");

        if (string.IsNullOrWhiteSpace(question.Stem))
        {
            Fail("stem is required.");
        }

        if (question.DifficultyLevel is not (>= 1 and <= 5))
        {
            Fail("difficultyLevel must be between 1 and 5.");
        }

        if (question.Hint?.Length > 1000)
        {
            Fail("hint cannot exceed 1000 characters.");
        }

        int? categoryId = null;
        if (!string.IsNullOrWhiteSpace(question.CategoryCode))
        {
            if (categories.TryGetValue(question.CategoryCode.Trim(), out var id))
            {
                categoryId = id;
            }
            else
            {
                Fail($"Unknown category '{question.CategoryCode}' for this certificate.");
            }
        }

        if (!EnumCodes.TryParse<QuestionType>(question.QuestionType?.Trim(), out var type))
        {
            Fail($"questionType must be mcq, mcq_3, multi_select, inline_completion, dropdown, drag_drop, or free_response, not '{question.QuestionType}'.");
            return null;
        }

        // Section blueprint AllowedComponentTypesJson validation
        if (section is not null && !string.IsNullOrWhiteSpace(section.AllowedComponentTypesJson))
        {
            List<string>? allowed = null;
            try
            {
                allowed = JsonSerializer.Deserialize<List<string>>(section.AllowedComponentTypesJson);
            }
            catch
            {
            }

            if (allowed != null && allowed.Count > 0 && !allowed.Contains(type.ToCode(), StringComparer.OrdinalIgnoreCase))
            {
                Fail($"Question type '{type.ToCode()}' is not allowed in section '{section.Code}' (allowed: {section.AllowedComponentTypesJson}).");
            }
        }

        // Modality checks
        if (type == QuestionType.FreeResponse)
        {
            if (section is not null && section.Modality is not ("speaking" or "writing"))
            {
                Fail($"section {section.Code} is {section.Modality}, which takes objective questions only.");
            }
        }
        else
        {
            if (section is not null && section.Modality is not ("listening" or "reading"))
            {
                Fail($"section {section.Code} is {section.Modality}, which takes free_response questions only.");
            }
        }

        // Question-type specific validation
        switch (type)
        {
            case QuestionType.Mcq:
                ValidateMcq(question, section, Fail);
                break;
            case QuestionType.Mcq_3:
                ValidateMcq3(question, section, Fail);
                break;
            case QuestionType.MultiSelect:
                ValidateMultiSelect(question, section, Fail);
                break;
            case QuestionType.InlineCompletion:
                ValidateInlineCompletion(question, section, Fail);
                break;
            case QuestionType.Dropdown:
            case QuestionType.DragDrop:
                ValidateDropdownOrDragDrop(question, section, type, Fail);
                break;
            case QuestionType.FreeResponse:
                ValidateFreeResponse(question, section, criteria, Fail);
                break;
        }

        string? acceptedAnswersJson = null;
        if (question.AcceptedAnswers != null && question.AcceptedAnswers.Count > 0)
        {
            acceptedAnswersJson = JsonSerializer.Serialize(question.AcceptedAnswers);
        }
        else if (!string.IsNullOrWhiteSpace(question.AcceptedAnswersJson))
        {
            acceptedAnswersJson = question.AcceptedAnswersJson.Trim();
        }

        string? requiredKeywords = null;
        if (question.RequiredKeywords != null && question.RequiredKeywords.Count > 0)
        {
            requiredKeywords = string.Join(", ", question.RequiredKeywords);
        }

        return report.ErrorCount > errorsBefore
            ? null
            : new PlannedQuestion(question, type, categoryId, question.DisplayOrder ?? questionIndex, acceptedAnswersJson, requiredKeywords);
    }

    private static void ValidateMcq(ImportManifestQuestion question, Section? section, Action<string> fail)
    {
        // CK_Questions_Timing / CK_Questions_Words
        if (question.PrepSeconds is not null || question.ResponseSeconds is not null || question.MinWords is not null || question.MaxWords is not null)
        {
            fail("mcq questions cannot set prepSeconds, responseSeconds, minWords or maxWords.");
        }

        if (question.Criteria is { Count: > 0 })
        {
            fail("mcq questions cannot have criteria.");
        }

        var options = question.Options ?? [];
        if (options.Count < 2)
        {
            fail("mcq questions need at least 2 options.");
            return;
        }

        if (section != null && section.OptionCount > 0 && options.Count != section.OptionCount)
        {
            fail($"Section '{section.Code}' expects {section.OptionCount} options for mcq questions, but found {options.Count}.");
        }

        // UX_Options_OneCorrect
        if (options.Count(o => o.IsCorrect) != 1)
        {
            fail("mcq questions need exactly one correct option.");
        }

        ValidateOptionEntries(options, fail);
    }

    private static void ValidateMcq3(ImportManifestQuestion question, Section? section, Action<string> fail)
    {
        if (question.PrepSeconds is not null || question.ResponseSeconds is not null || question.MinWords is not null || question.MaxWords is not null)
        {
            fail("mcq_3 questions cannot set prepSeconds, responseSeconds, minWords or maxWords.");
        }

        if (question.Criteria is { Count: > 0 })
        {
            fail("mcq_3 questions cannot have criteria.");
        }

        var options = question.Options ?? [];
        if (options.Count != 3)
        {
            fail("mcq_3 questions must have exactly 3 options.");
            return;
        }

        if (options.Count(o => o.IsCorrect) != 1)
        {
            fail("mcq_3 questions need exactly one correct option.");
        }

        ValidateOptionEntries(options, fail);
    }

    private static void ValidateMultiSelect(ImportManifestQuestion question, Section? section, Action<string> fail)
    {
        if (question.PrepSeconds is not null || question.ResponseSeconds is not null || question.MinWords is not null || question.MaxWords is not null)
        {
            fail("multi_select questions cannot set prepSeconds, responseSeconds, minWords or maxWords.");
        }

        if (question.Criteria is { Count: > 0 })
        {
            fail("multi_select questions cannot have criteria.");
        }

        var options = question.Options ?? [];
        if (options.Count < 2)
        {
            fail("multi_select questions need at least 2 options.");
            return;
        }

        var correctCount = options.Count(o => o.IsCorrect);
        if (correctCount < 1)
        {
            fail("multi_select questions must have at least one correct option.");
        }

        if (!question.MaxSelections.HasValue || question.MaxSelections.Value < 2)
        {
            fail("multi_select questions must specify maxSelections of at least 2.");
        }
        else if (question.MaxSelections.Value > options.Count)
        {
            fail("maxSelections cannot exceed the number of available options.");
        }
        else if (correctCount > question.MaxSelections.Value)
        {
            fail("multi_select questions cannot have more correct options than maxSelections.");
        }

        ValidateOptionEntries(options, fail);
    }

    private static void ValidateInlineCompletion(ImportManifestQuestion question, Section? section, Action<string> fail)
    {
        if (question.PrepSeconds is not null || question.ResponseSeconds is not null)
        {
            fail("inline_completion questions cannot set prepSeconds or responseSeconds.");
        }

        if (question.Criteria is { Count: > 0 })
        {
            fail("inline_completion questions cannot have criteria.");
        }

        var accepted = question.AcceptedAnswers ?? (string.IsNullOrWhiteSpace(question.AcceptedAnswersJson) ? [] : AttemptEngine.ParseAcceptedAnswers(question.AcceptedAnswersJson));
        if (accepted.Count == 0 && (question.Options == null || !question.Options.Any(o => o.IsCorrect)))
        {
            fail("inline_completion questions must specify acceptedAnswers.");
        }

        if (question.MinWords is <= 0)
        {
            fail("minWords must be greater than 0.");
        }

        if (question.MaxWords is <= 0)
        {
            fail("maxWords must be greater than 0.");
        }

        if (question.MaxWords is { } max && question.MinWords is { } min && max < min)
        {
            fail("maxWords must be at least minWords.");
        }

        if (question.Options is { Count: > 0 })
        {
            ValidateOptionEntries(question.Options, fail);
        }
    }

    private static void ValidateDropdownOrDragDrop(ImportManifestQuestion question, Section? section, QuestionType type, Action<string> fail)
    {
        if (question.PrepSeconds is not null || question.ResponseSeconds is not null || question.MinWords is not null || question.MaxWords is not null)
        {
            fail($"{type.ToCode()} questions cannot set prepSeconds, responseSeconds, minWords or maxWords.");
        }

        if (question.Criteria is { Count: > 0 })
        {
            fail($"{type.ToCode()} questions cannot have criteria.");
        }

        var options = question.Options ?? [];
        if (options.Count < 2)
        {
            fail($"{type.ToCode()} questions need at least 2 options.");
            return;
        }

        if (options.Count(o => o.IsCorrect) < 1)
        {
            fail($"{type.ToCode()} questions must have at least one correct option.");
        }

        ValidateOptionEntries(options, fail);
    }

    private static void ValidateFreeResponse(ImportManifestQuestion question, Section? section, ILookup<int, string> criteria, Action<string> fail)
    {
        if (question.Options is { Count: > 0 })
        {
            fail("free_response questions cannot have options.");
        }

        // CK_Questions_Timing
        if (question.PrepSeconds is < 0)
        {
            fail("prepSeconds cannot be negative.");
        }

        if (question.ResponseSeconds is <= 0)
        {
            fail("responseSeconds must be greater than 0.");
        }

        // CK_Questions_Words
        if (question.MinWords is <= 0)
        {
            fail("minWords must be greater than 0.");
        }

        if (question.MaxWords is { } max && max < (question.MinWords ?? 1))
        {
            fail("maxWords must be at least minWords.");
        }

        // Required keywords validation against certificate blueprint
        if (section is not null && section.RequiredKeywordCount > 0)
        {
            var keywords = question.RequiredKeywords ?? (string.IsNullOrWhiteSpace(question.AcceptedAnswersJson) ? [] : AttemptEngine.ParseKeywords(question.AcceptedAnswersJson));
            if (keywords.Count < section.RequiredKeywordCount)
            {
                fail($"Section '{section.Code}' requires at least {section.RequiredKeywordCount} required keywords for free_response questions, but found {keywords.Count}.");
            }
        }

        // The rubric is the skill's GradingCriteria (ADR-0004); a manifest may only name existing ones.
        if (section is not null)
        {
            var known = criteria[section.SkillId].ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var code in (question.Criteria ?? []).Select(c => c.Code?.Trim()))
            {
                if (string.IsNullOrEmpty(code))
                {
                    fail("every criterion needs a code.");
                }
                else if (!known.Contains(code))
                {
                    fail($"Unknown grading criterion '{code}' for {section.Code}. Known criteria: {string.Join(", ", known.Order())}.");
                }
            }
        }
    }

    private static void ValidateOptionEntries(IReadOnlyList<ImportManifestOption> options, Action<string> fail)
    {
        var labels = options.Select(o => o.Label?.Trim().ToUpperInvariant()).ToList();
        if (labels.Any(string.IsNullOrEmpty))
        {
            fail("every option needs a label.");
        }

        foreach (var duplicate in labels.Where(l => !string.IsNullOrEmpty(l)).GroupBy(l => l).Where(g => g.Count() > 1))
        {
            fail($"Option label '{duplicate.Key}' is used twice.");
        }

        if (labels.Any(l => l?.Length > 4))
        {
            fail("option labels cannot be longer than 4 characters.");
        }

        if (options.Any(o => string.IsNullOrWhiteSpace(o.Text)))
        {
            fail("every option needs text.");
        }
    }

    // ---- Writing the exam ----

    /// <summary>Stores the media, then writes the exam and all its rows in one transaction. If either
    /// step fails, the rows roll back and the files already written are deleted, so a failed batch
    /// leaves only its ImportBatches row and log.</summary>
    private async Task<AdminImportResultDto> CommitAsync(ImportBatch batch, ImportPlan plan, ImportPackage package, ImportReport report, CancellationToken cancellationToken)
    {
        var stored = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var path in plan.MediaPaths)
            {
                using var stream = new MemoryStream(package.Media![path]);
                stored[path] = await fileStore.SaveImportMediaAsync(batch.BatchId, stream, Path.GetExtension(path), cancellationToken);
            }
        }
        catch (IOException ex)
        {
            log?.LogError(ex, "Import batch {BatchId}: storing media failed", batch.BatchId);
            await DeleteStoredAsync(stored.Values);
            report.Error(null, "The media files could not be stored. Nothing was imported; try the upload again.");
            return await FailAsync(batch, report, cancellationToken);
        }

        Exam exam;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                exam = await WriteExamAsync(batch.CertId, plan, stored, cancellationToken);
                db.ImportLogs.Add(NewLog(batch.BatchId, null, ImportLogLevel.Info,
                    $"Created draft exam '{exam.Name}' with {plan.QuestionCount} questions in {plan.Groups.Count} groups."));
                batch.Status = ImportBatchStatus.Done.ToCode();
                batch.CompletedAt = clock.GetUtcNow();
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                // Validation mirrors every constraint, so reaching here is a bug (ADR-0001): log it,
                // undo everything, and tell the admin plainly.
                log?.LogError(ex, "Import batch {BatchId}: database rejected a validated plan", batch.BatchId);
                await transaction.RollbackAsync(cancellationToken);
                await DeleteStoredAsync(stored.Values);
                report.Error(null, "The database refused the import. Nothing was imported; the error has been logged.");
                return await FailAsync(batch, report, cancellationToken, resetTracker: true);
            }
        }

        return new AdminImportResultDto(batch.BatchId, ImportBatchStatusDto.Done, exam.ExamId, exam.Name, plan.QuestionCount, 0, report.WarningCount);
    }

    /// <summary>Saves in stages (exam, groups, questions, options) with explicit keys: the composite
    /// foreign keys that pin a question to its group's exam and section need the generated ids.</summary>
    private async Task<Exam> WriteExamAsync(int certId, ImportPlan plan, IReadOnlyDictionary<string, string> stored, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        string? Url(string? file) => file is null ? null : MediaUrlPrefix + stored[NormaliseEntryPath(file)!];

        var exam = new Exam
        {
            CertId = certId,
            Name = plan.Exam.Name,
            Status = ExamStatus.Draft.ToCode(),
            Purpose = plan.Exam.Purpose.ToCode(),
            DeliveryMode = plan.Exam.DeliveryMode,
            DurationMinutes = plan.Exam.DurationMinutes,
            SourceName = plan.Exam.SourceName,
            Description = plan.Exam.Description,
            QuestionCount = plan.Exam.QuestionCount,
            CreatedAt = now,
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync(cancellationToken);

        var groups = plan.Groups.Select(planned => (planned, row: new QuestionGroup
        {
            ExamId = exam.ExamId,
            CertId = certId,
            SectionId = planned.Section.SectionId,
            UnitNumber = planned.Source.UnitNumber ?? 1,
            Passage = Normalise(planned.Source.Passage),
            AudioPath = Url(planned.Source.AudioFile),
            AudioStartMs = planned.Source.AudioStartMs,
            AudioEndMs = planned.Source.AudioEndMs,
            ImagePath = Url(planned.Source.ImageFile),
            ImageAlt = planned.Source.ImageFile is null ? null : Normalise(planned.Source.ImageAlt),
            Transcript = Normalise(planned.Source.Transcript),
            PrepSeconds = planned.Source.PrepSeconds,
            ResponseSeconds = planned.Source.ResponseSeconds,
            DisplayOrder = planned.DisplayOrder,
        })).ToList();
        db.QuestionGroups.AddRange(groups.Select(g => g.row));
        await db.SaveChangesAsync(cancellationToken);

        var questions = groups.SelectMany(g => g.planned.Questions.Select(question => (question, row: new Question
        {
            GroupId = g.row.GroupId,
            ExamId = exam.ExamId,
            SectionId = g.planned.Section.SectionId,
            SkillId = g.planned.Section.SkillId,
            Stem = question.Source.Stem!.Trim(),
            DifficultyLevel = question.Source.DifficultyLevel!.Value,
            QuestionType = question.Type.ToCode(),
            Explanation = Normalise(question.Source.Explanation),
            CategoryId = question.CategoryId,
            PrepSeconds = question.Source.PrepSeconds,
            ResponseSeconds = question.Source.ResponseSeconds,
            MinWords = question.Source.MinWords,
            MaxWords = question.Source.MaxWords,
            MaxSelections = question.Source.MaxSelections,
            AllowNumber = question.Source.AllowNumber ?? true,
            AcceptedAnswersJson = question.AcceptedAnswersJson,
            RequiredKeywords = question.RequiredKeywords,
            Hint = Normalise(question.Source.Hint),
            DisplayOrder = question.DisplayOrder,
            UpdatedAt = now,
        }))).ToList();
        db.Questions.AddRange(questions.Select(q => q.row));
        await db.SaveChangesAsync(cancellationToken);

        // Options for all non-free-response questions
        db.QuestionOptions.AddRange(questions
            .Where(q => q.question.Type != QuestionType.FreeResponse && q.question.Source.Options != null)
            .SelectMany(q => q.question.Source.Options!.Select(o => new QuestionOption
            {
                QuestionId = q.row.QuestionId,
                QuestionType = q.row.QuestionType,
                Label = o.Label!.Trim().ToUpperInvariant(),
                Text = o.Text!.Trim(),
                IsCorrect = o.IsCorrect,
                Rationale = Normalise(o.Rationale),
            })));
        await db.SaveChangesAsync(cancellationToken);

        return exam;
    }

    private async Task<AdminImportResultDto> FailAsync(ImportBatch batch, ImportReport report, CancellationToken cancellationToken, bool resetTracker = false)
    {
        if (resetTracker)
        {
            // The rolled-back rows are still tracked as Added; drop them and re-attach the batch.
            db.ChangeTracker.Clear();
            db.ImportBatches.Attach(batch);
        }

        db.ImportLogs.AddRange(report.Entries.Select(e => NewLog(batch.BatchId, e.RowNo, e.Level, e.Message)));
        batch.Status = ImportBatchStatus.Failed.ToCode();
        batch.CompletedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return new AdminImportResultDto(batch.BatchId, ImportBatchStatusDto.Failed, null, null, 0, report.ErrorCount, report.WarningCount);
    }

    private async Task DeleteStoredAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                await fileStore.DeleteMediaAsync(path);
            }
            catch (IOException ex)
            {
                log?.LogWarning(ex, "Could not remove orphaned import media {Path}", path);
            }
        }
    }

    private static ImportLog NewLog(int batchId, int? rowNo, ImportLogLevel level, string message) => new()
    {
        BatchId = batchId,
        RowNo = rowNo,
        Level = level.ToCode(),
        Message = message.Length > 1000 ? message[..997] + "..." : message,
    };

    private static string? Normalise(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // ---- Reading batches back ----

    public async Task<AdminImportBatchesDto> GetBatchesAsync(int? certId, CancellationToken cancellationToken = default)
    {
        var query = db.ImportBatches.AsNoTracking();
        if (certId is { } id)
        {
            query = query.Where(b => b.CertId == id);
        }

        var batches = await ProjectBatches(query.OrderByDescending(b => b.BatchId).Take(50)).ToListAsync(cancellationToken);
        var certificates = await db.Certificates.AsNoTracking().OrderBy(c => c.CertId)
            .Select(c => new AdminCertificateOptionDto(c.CertId, c.Code, c.Name)).ToListAsync(cancellationToken);
        return new AdminImportBatchesDto(batches.Select(ToDto).ToList(), certificates);
    }

    public async Task<AdminImportBatchDetailDto> GetBatchAsync(int batchId, CancellationToken cancellationToken = default)
    {
        var batch = await ProjectBatches(db.ImportBatches.AsNoTracking().Where(b => b.BatchId == batchId)).SingleOrDefaultAsync(cancellationToken)
            ?? throw new AdminExamException(ApiError.Codes.ImportBatchNotFound, $"Import batch {batchId} not found.", 404);

        var logs = await db.ImportLogs.AsNoTracking().Where(l => l.BatchId == batchId).OrderBy(l => l.LogId).ToListAsync(cancellationToken);
        return new AdminImportBatchDetailDto(
            ToDto(batch),
            logs.Select(l => new AdminImportLogDto(l.LogId, l.RowNo, EnumCodes.Parse<ImportLogLevel>(l.Level).MapTo<ImportLogLevelDto>(), l.Message, l.LoggedAt)).ToList());
    }

    private sealed record BatchRow(ImportBatch Batch, string CertCode, string Email, int Errors, int Warnings);

    private static IQueryable<BatchRow> ProjectBatches(IQueryable<ImportBatch> query)
    {
        var error = ImportLogLevel.Error.ToCode();
        var warn = ImportLogLevel.Warn.ToCode();
        return query.Select(b => new BatchRow(
            b,
            b.Cert.Code,
            b.UploadedByNavigation.Email,
            b.ImportLogs.Count(l => l.Level == error),
            b.ImportLogs.Count(l => l.Level == warn)));
    }

    private static AdminImportBatchListItemDto ToDto(BatchRow row) => new(
        row.Batch.BatchId,
        row.Batch.CertId,
        row.CertCode,
        row.Batch.SourceName,
        EnumCodes.Parse<ImportBatchStatus>(row.Batch.Status).MapTo<ImportBatchStatusDto>(),
        row.Email,
        row.Batch.CreatedAt,
        row.Batch.CompletedAt,
        row.Errors,
        row.Warnings);

    // ---- Dynamic Manifest Template Generation ----

    public async Task<(Stream Stream, string ContentType, string FileName)> GenerateManifestTemplateAsync(int certId, CancellationToken cancellationToken = default)
    {
        var cert = await db.Certificates.AsNoTracking().FirstOrDefaultAsync(c => c.CertId == certId, cancellationToken)
            ?? throw new AdminExamException(ApiError.Codes.CertificateUnavailable, $"Certificate with id {certId} not found.", 404);

        var skills = await db.Skills.AsNoTracking().Where(s => s.CertId == certId).OrderBy(s => s.DisplayOrder).ToListAsync(cancellationToken);
        var sections = await db.Sections.AsNoTracking().Where(s => s.CertId == certId).OrderBy(s => s.DisplayOrder).ToListAsync(cancellationToken);

        var manifestSections = new List<ImportManifestSection>();
        var totalQuestionCount = 0;

        foreach (var skill in skills)
        {
            var skillSections = sections.Where(s => s.SkillId == skill.SkillId).OrderBy(s => s.DisplayOrder).ToList();
            foreach (var sec in skillSections)
            {
                var allowed = ParseAllowedComponents(sec);
                var clusterSize = sec.ClusterSize > 0 ? sec.ClusterSize : 1;
                var placeholderQuestions = new List<ImportManifestQuestion>();

                for (var qIndex = 1; qIndex <= clusterSize; qIndex++)
                {
                    totalQuestionCount++;
                    var qType = allowed.Count > 0 ? allowed[(qIndex - 1) % allowed.Count] : (sec.Modality is "speaking" or "writing" ? "free_response" : "mcq");
                    placeholderQuestions.Add(CreatePlaceholderQuestion(sec, qType, qIndex));
                }

                var group = new ImportManifestGroup
                {
                    UnitNumber = 1,
                    Passage = sec.RequirePassage ? $"[Passage for {sec.Name}] Detailed text or context here." : null,
                    AudioFile = sec.RequireAudio ? "media/sample_audio.mp3" : null,
                    ImageFile = sec.RequireImage ? "media/sample_image.jpg" : null,
                    ImageAlt = sec.RequireImage ? $"Sample image description for {sec.Name}" : null,
                    PrepSeconds = sec.NavigationMode == "SYSTEM_PACED_AUTO" ? 15 : null,
                    ResponseSeconds = sec.NavigationMode == "SYSTEM_PACED_AUTO" ? 45 : null,
                    DisplayOrder = 1,
                    Questions = placeholderQuestions,
                };

                manifestSections.Add(new ImportManifestSection
                {
                    SectionCode = sec.Code,
                    Groups = [group],
                });
            }
        }

        var durationMinutes = skills.Sum(s => s.DurationMinutes ?? 60);
        if (durationMinutes <= 0) durationMinutes = 120;

        var manifest = new ImportManifest
        {
            Schema = "https://raw.githubusercontent.com/extra-efficiency/schemas/main/manifest.schema.json",
            Version = "1.0",
            Exam = new ImportManifestExam
            {
                Title = $"{cert.Code} Practice Exam Template",
                Purpose = "mock",
                DeliveryMode = "strict_exam",
                DurationMinutes = durationMinutes,
                SourceName = $"{cert.Code} Official Template",
                Description = $"Pre-structured skeleton generated dynamically from {cert.Name} section blueprints.",
                QuestionCount = totalQuestionCount,
            },
            Sections = manifestSections,
        };

        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, ImportManifest.Json);
        var stream = new MemoryStream(jsonBytes);
        var fileName = $"{cert.Code.ToLowerInvariant()}_manifest_template.json";
        return (stream, "application/json", fileName);
    }

    private static List<string> ParseAllowedComponents(Section sec)
    {
        if (!string.IsNullOrWhiteSpace(sec.AllowedComponentTypesJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(sec.AllowedComponentTypesJson);
                if (parsed != null && parsed.Count > 0) return parsed;
            }
            catch
            {
            }
        }

        if (sec.Modality is "speaking" or "writing")
        {
            return ["free_response"];
        }

        return sec.OptionCount == 3 ? ["mcq_3"] : ["mcq"];
    }

    private static ImportManifestQuestion CreatePlaceholderQuestion(Section sec, string qType, int displayOrder)
    {
        return qType.ToLowerInvariant() switch
        {
            "free_response" => new ImportManifestQuestion
            {
                Stem = $"[Prompt for {sec.Name}] Respond to the question or task description.",
                DifficultyLevel = 3,
                QuestionType = "free_response",
                PrepSeconds = sec.NavigationMode == "SYSTEM_PACED_AUTO" ? 15 : null,
                ResponseSeconds = sec.Modality == "speaking" ? 45 : 1200,
                MinWords = sec.Modality == "writing" ? 150 : null,
                MaxWords = sec.Modality == "writing" ? 250 : null,
                RequiredKeywords = sec.RequiredKeywordCount > 0
                    ? Enumerable.Range(1, sec.RequiredKeywordCount).Select(k => $"keyword_{k}").ToList()
                    : null,
                DisplayOrder = displayOrder,
                Explanation = "Detailed scoring rationale and rubric expectations.",
                Hint = "Organize thoughts clearly before responding.",
            },

            "multi_select" => new ImportManifestQuestion
            {
                Stem = $"[Question #{displayOrder}] Which TWO of the following statements are supported by the passage?",
                DifficultyLevel = 3,
                QuestionType = "multi_select",
                MaxSelections = 2,
                DisplayOrder = displayOrder,
                Options =
                [
                    new() { Label = "A", Text = "First supported factual claim", IsCorrect = true, Rationale = "Directly verified in paragraph 2." },
                    new() { Label = "B", Text = "Plausible distractor claim", IsCorrect = false, Rationale = "Contradicts paragraph 3." },
                    new() { Label = "C", Text = "Second supported factual claim", IsCorrect = true, Rationale = "Confirmed in final paragraph." },
                    new() { Label = "D", Text = "Unrelated distractor claim", IsCorrect = false, Rationale = "Not mentioned in the text." },
                ],
                Explanation = "Claims A and C are directly substantiated by the stimulus.",
                Hint = "Check each statement against the passage numbers.",
            },

            "inline_completion" => new ImportManifestQuestion
            {
                Stem = $"[Question #{displayOrder}] The new distribution center reduced regional transit times by _____ percent.",
                DifficultyLevel = 3,
                QuestionType = "inline_completion",
                AllowNumber = true,
                MaxWords = 2,
                AcceptedAnswers = ["35", "thirty-five"],
                DisplayOrder = displayOrder,
                Explanation = "The second paragraph explicitly states a 35% reduction.",
                Hint = "Look for numerical percentage metrics.",
            },

            "dropdown" or "drag_drop" => new ImportManifestQuestion
            {
                Stem = $"[Question #{displayOrder}] Match the category label to this item.",
                DifficultyLevel = 3,
                QuestionType = qType.ToLowerInvariant(),
                DisplayOrder = displayOrder,
                Options =
                [
                    new() { Label = "A", Text = "Correct Category Option", IsCorrect = true, Rationale = "Matches standard taxonomy." },
                    new() { Label = "B", Text = "Alternate Category Option", IsCorrect = false, Rationale = "Incorrect classification." },
                    new() { Label = "C", Text = "Tertiary Category Option", IsCorrect = false, Rationale = "Incorrect classification." },
                ],
                Explanation = "Option A correctly classifies the given attribute.",
                Hint = "Review category classifications in stimulus.",
            },

            "mcq_3" => new ImportManifestQuestion
            {
                Stem = $"[Question #{displayOrder}] What was the primary topic discussed by the speakers?",
                DifficultyLevel = 3,
                QuestionType = "mcq_3",
                DisplayOrder = displayOrder,
                Options =
                [
                    new() { Label = "A", Text = "Correct primary topic", IsCorrect = true, Rationale = "Directly stated in initial sentence." },
                    new() { Label = "B", Text = "Secondary mention", IsCorrect = false, Rationale = "Only briefly mentioned." },
                    new() { Label = "C", Text = "Unrelated topic", IsCorrect = false, Rationale = "Not discussed." },
                ],
                Explanation = "The speakers primarily discussed option A.",
                Hint = "Listen for the opening statement.",
            },

            _ => new ImportManifestQuestion
            {
                Stem = $"[Question #{displayOrder}] Which option best completes the sentence?",
                DifficultyLevel = 3,
                QuestionType = "mcq",
                DisplayOrder = displayOrder,
                Options = Enumerable.Range(0, sec.OptionCount is >= 2 and <= 6 ? sec.OptionCount : 4)
                    .Select(i => new ImportManifestOption
                    {
                        Label = ((char)('A' + i)).ToString(),
                        Text = i == 0 ? "Correct answer statement" : $"Distractor statement {(char)('A' + i)}",
                        IsCorrect = i == 0,
                        Rationale = i == 0 ? "Correctly follows grammatical rule." : "Incorrect syntax or distractor.",
                    }).ToList(),
                Explanation = "Option A is the correct answer based on context.",
                Hint = "Consider the grammatical role required in the blank.",
            },
        };
    }

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

    /// <summary>Collects every problem found in one upload; written to ImportLog when the batch ends.</summary>
    private sealed class ImportReport
    {
        public List<(int? RowNo, ImportLogLevel Level, string Message)> Entries { get; } = [];

        public int ErrorCount => Entries.Count(e => e.Level == ImportLogLevel.Error);

        public int WarningCount => Entries.Count(e => e.Level == ImportLogLevel.Warn);

        public void Error(int? rowNo, string message) => Entries.Add((rowNo, ImportLogLevel.Error, message));
    }
}
