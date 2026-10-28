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
}
