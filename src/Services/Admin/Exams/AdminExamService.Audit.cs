using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class AdminExamService
{

    public async Task<ExamAuditResultDto> AuditExamPublishReadinessAsync(int examId, CancellationToken cancellationToken = default)
    {
        var exam = await db.Exams.AsNoTracking()
            .Include(e => e.Cert)
            .FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken);

        if (exam == null)
        {
            throw new ExamNotFoundException(examId);
        }

        var sections = await db.Sections.AsNoTracking()
            .Where(s => s.CertId == exam.CertId)
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);

        var sectionAudios = await db.ExamSectionAudios.AsNoTracking()
            .Where(a => a.ExamId == examId)
            .ToListAsync(cancellationToken);

        var groups = await db.QuestionGroups.AsNoTracking()
            .Include(g => g.Section)
            .Include(g => g.QuestionGroups)
                .ThenInclude(q => q.QuestionOptionQuestionNavigations)
            .Where(g => g.ExamId == examId)
            .OrderBy(g => g.DisplayOrder)
            // Two collection navigations in one query is a Cartesian product: warn 20504, and the
            // row count multiplies with every option added. Split keeps each level linear (F5).
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var sectionIds = sections.Select(s => s.SectionId).ToHashSet();
        var sectionCriteria = await db.SectionCriteria.AsNoTracking()
            .Where(sc => sectionIds.Contains(sc.SectionId))
            .ToListAsync(cancellationToken);

        return AuditExamPublishReadinessInternal(exam, sections, sectionAudios, groups, sectionCriteria);
    }

    private static ExamAuditResultDto AuditExamPublishReadinessInternal(
        Exam exam,
        List<Section> sections,
        List<ExamSectionAudio> sectionAudios,
        List<QuestionGroup> groups,
        List<SectionCriterion> sectionCriteria)
    {
        var issues = new List<ExamAuditIssueDto>();
        var missingItems = new List<string>();

        var relevantSections = exam.Purpose switch
        {
            "speaking_practice" => sections.Where(s => s.Modality == "speaking").ToList(),
            "writing_practice" => sections.Where(s => s.Modality == "writing").ToList(),
            _ => sections
        };

        var relevantSectionIds = relevantSections.Select(s => s.SectionId).ToHashSet();
        var relevantGroups = groups.Where(g => relevantSectionIds.Contains(g.SectionId)).ToList();
        var totalQuestions = relevantGroups.Sum(g => g.QuestionGroups.Count);

        var sectionExpectedSum = relevantSections.Where(s => s.ExpectedQuestionCount.HasValue).Sum(s => s.ExpectedQuestionCount!.Value);
        var expectedTotal = sectionExpectedSum > 0 ? sectionExpectedSum : (exam.QuestionCount ?? 0);

        if (groups.Count == 0 || totalQuestions == 0)
        {
            var msg = "Exam must have at least one question group and question.";
            issues.Add(new ExamAuditIssueDto(
                Code: "EXAM_EMPTY",
                Severity: "error",
                Message: msg));
            missingItems.Add(msg);
        }

        if (exam.QuestionCount.HasValue && totalQuestions < exam.QuestionCount.Value)
        {
            var diff = exam.QuestionCount.Value - totalQuestions;
            var msg = $"Exam requires at least {exam.QuestionCount.Value} questions, but only has {totalQuestions} ({diff} missing).";
            issues.Add(new ExamAuditIssueDto(
                Code: "EXAM_QUESTION_COUNT_LOW",
                Severity: "error",
                Message: msg));
            missingItems.Add(msg);
        }

        foreach (var sec in relevantSections)
        {
            var secGroups = groups.Where(g => g.SectionId == sec.SectionId).ToList();
            var secQuestions = secGroups.SelectMany(g => g.QuestionGroups).ToList();

            if (sec.ExpectedQuestionCount.HasValue && sec.ExpectedQuestionCount.Value > 0)
            {
                if (secQuestions.Count < sec.ExpectedQuestionCount.Value)
                {
                    var diff = sec.ExpectedQuestionCount.Value - secQuestions.Count;
                    var msg = $"Section [{sec.Code}] '{sec.Name}' requires {sec.ExpectedQuestionCount.Value} questions, but only has {secQuestions.Count} ({diff} missing).";
                    issues.Add(new ExamAuditIssueDto(
                        Code: "SEC_QUESTION_COUNT_LOW",
                        Severity: "error",
                        Message: msg,
                        SectionId: sec.SectionId,
                        SectionCode: sec.Code,
                        ElementTarget: $"section-link-{sec.SectionId}"));
                    missingItems.Add(msg);
                }
            }

            if (sec.ClusterSize > 1 && secGroups.Count > 0)
            {
                foreach (var g in secGroups)
                {
                    if (g.QuestionGroups.Count != sec.ClusterSize)
                    {
                        var msg = $"Group #{g.DisplayOrder} in [{sec.Code}] requires {sec.ClusterSize} questions per cluster, but has {g.QuestionGroups.Count}.";
                        issues.Add(new ExamAuditIssueDto(
                            Code: "GRP_CLUSTER_SIZE_MISMATCH",
                            Severity: "error",
                            Message: msg,
                            SectionId: sec.SectionId,
                            SectionCode: sec.Code,
                            GroupId: g.GroupId,
                            ElementTarget: $"group-card-{g.GroupId}"));
                        missingItems.Add(msg);
                    }
                }
            }

            if (sec.RequirePassage && secGroups.Count > 0)
            {
                foreach (var g in secGroups)
                {
                    if (string.IsNullOrWhiteSpace(g.Passage))
                    {
                        var msg = $"Group #{g.DisplayOrder} in [{sec.Code}] requires a stimulus passage (RequirePassage is enabled).";
                        issues.Add(new ExamAuditIssueDto(
                            Code: "GRP_PASSAGE_MISSING",
                            Severity: "error",
                            Message: msg,
                            SectionId: sec.SectionId,
                            SectionCode: sec.Code,
                            GroupId: g.GroupId,
                            ElementTarget: $"group-card-{g.GroupId}"));
                        missingItems.Add(msg);
                    }
                }
            }

            if (sec.RequireImage && secGroups.Count > 0)
            {
                foreach (var g in secGroups)
                {
                    if (string.IsNullOrWhiteSpace(g.ImagePath))
                    {
                        var msg = $"Group #{g.DisplayOrder} in [{sec.Code}] requires an image stimulus (RequireImage is enabled).";
                        issues.Add(new ExamAuditIssueDto(
                            Code: "GRP_IMAGE_MISSING",
                            Severity: "error",
                            Message: msg,
                            SectionId: sec.SectionId,
                            SectionCode: sec.Code,
                            GroupId: g.GroupId,
                            ElementTarget: $"group-card-{g.GroupId}"));
                        missingItems.Add(msg);
                    }
                }
            }

            if (sec.RequireAudio || (sec.Modality == "listening" && (secGroups.Count > 0 || (sec.ExpectedQuestionCount.HasValue && sec.ExpectedQuestionCount.Value > 0))))
            {
                var hasSecAudio = sectionAudios.Any(a => a.SectionId == sec.SectionId && !string.IsNullOrWhiteSpace(a.AudioPath));
                var allGroupsAudio = secGroups.Count > 0 && secGroups.All(g => !string.IsNullOrWhiteSpace(g.AudioPath));
                if (!hasSecAudio && !allGroupsAudio)
                {
                    var msg = $"Listening section [{sec.Code}] '{sec.Name}' is missing section audio or question group audio tracks.";
                    issues.Add(new ExamAuditIssueDto(
                        Code: "SEC_AUDIO_MISSING",
                        Severity: "error",
                        Message: msg,
                        SectionId: sec.SectionId,
                        SectionCode: sec.Code,
                        ElementTarget: $"section-link-{sec.SectionId}"));
                    missingItems.Add(msg);
                }
            }

            List<string>? allowedTypes = null;
            if (!string.IsNullOrWhiteSpace(sec.AllowedComponentTypesJson))
            {
                try
                {
                    allowedTypes = JsonSerializer.Deserialize<List<string>>(sec.AllowedComponentTypesJson);
                }
                catch { }
            }

            foreach (var g in secGroups)
            {
                if (!string.IsNullOrWhiteSpace(g.ImagePath) && string.IsNullOrWhiteSpace(g.ImageAlt))
                {
                    issues.Add(new ExamAuditIssueDto(
                        Code: "GRP_IMAGE_ALT_MISSING",
                        Severity: "error",
                        Message: $"Group #{g.DisplayOrder} in [{sec.Code}] has an image but is missing required accessibility alt text (CK_QGroups_ImageAlt).",
                        SectionId: sec.SectionId,
                        SectionCode: sec.Code,
                        GroupId: g.GroupId,
                        ElementTarget: $"group-card-{g.GroupId}"));
                }

                if (g.AudioStartMs.HasValue || g.AudioEndMs.HasValue)
                {
                    if (!g.AudioStartMs.HasValue || !g.AudioEndMs.HasValue || g.AudioStartMs.Value < 0 || g.AudioEndMs.Value <= g.AudioStartMs.Value)
                    {
                        issues.Add(new ExamAuditIssueDto(
                            Code: "GRP_AUDIO_SLICE_INVALID",
                            Severity: "error",
                            Message: $"Group #{g.DisplayOrder} in [{sec.Code}] has invalid audio timestamps: start must be >= 0 and end > start (CK_QGroups_AudioSlice).",
                            SectionId: sec.SectionId,
                            SectionCode: sec.Code,
                            GroupId: g.GroupId,
                            ElementTarget: $"group-card-{g.GroupId}"));
                    }
                }

                var master = sectionAudios.FirstOrDefault(a => a.SectionId == sec.SectionId)?.AudioPath;
                var hasMaster = sec.Modality == "listening" && !string.IsNullOrWhiteSpace(master);
                if (hasMaster && !string.IsNullOrWhiteSpace(g.AudioPath)
                    && MediaUrlHelper.NormalizeMediaUrl(g.AudioPath) != MediaUrlHelper.NormalizeMediaUrl(master))
                {
                    issues.Add(new ExamAuditIssueDto(
                        Code: "GRP_AUDIO_OVERRIDDEN",
                        Severity: "warning",
                        Message: $"Group #{g.DisplayOrder} in [{sec.Code}] has its own audio file, but the section's master audio is played instead (any slice is applied to the master). Clear the group's audio path or remove the master audio.",
                        SectionId: sec.SectionId,
                        SectionCode: sec.Code,
                        GroupId: g.GroupId,
                        ElementTarget: $"group-card-{g.GroupId}"));
                }

                if (hasMaster && secGroups.Count > 1
                    && ExamAudio.Resolve(g.AudioPath, g.AudioStartMs, g.AudioEndMs, master)?.StartMs is null)
                {
                    issues.Add(new ExamAuditIssueDto(
                        Code: "GRP_AUDIO_UNSLICED",
                        Severity: "error",
                        Message: $"Group #{g.DisplayOrder} in [{sec.Code}] has no slice of the section's master audio, so learners would hear the whole track. Set its start/end in the audio slicer.",
                        SectionId: sec.SectionId,
                        SectionCode: sec.Code,
                        GroupId: g.GroupId,
                        ElementTarget: $"group-card-{g.GroupId}"));
                }

                foreach (var q in g.QuestionGroups)
                {
                    if (string.IsNullOrWhiteSpace(q.Stem))
                    {
                        issues.Add(new ExamAuditIssueDto(
                            Code: "Q_STEM_EMPTY",
                            Severity: "error",
                            Message: $"Question #{q.DisplayOrder} in [{sec.Code}] has an empty stem.",
                            SectionId: sec.SectionId,
                            SectionCode: sec.Code,
                            GroupId: g.GroupId,
                            QuestionId: q.QuestionId,
                            ElementTarget: $"question-card-{q.QuestionId}"));
                    }

                    if (allowedTypes != null && allowedTypes.Count > 0 && !allowedTypes.Contains(q.QuestionType, StringComparer.OrdinalIgnoreCase))
                    {
                        var msg = $"Question #{q.DisplayOrder} in [{sec.Code}] has type '{q.QuestionType}', which is not allowed by the section blueprint (allowed: {sec.AllowedComponentTypesJson}).";
                        issues.Add(new ExamAuditIssueDto(
                            Code: "Q_TYPE_NOT_ALLOWED",
                            Severity: "error",
                            Message: msg,
                            SectionId: sec.SectionId,
                            SectionCode: sec.Code,
                            GroupId: g.GroupId,
                            QuestionId: q.QuestionId,
                            ElementTarget: $"question-card-{q.QuestionId}"));
                        missingItems.Add(msg);
                    }

                    if (sec.RequiredKeywordCount > 0 && string.Equals(q.QuestionType, "free_response", StringComparison.OrdinalIgnoreCase))
                    {
                        var kwList = string.IsNullOrWhiteSpace(q.RequiredKeywords) ? new List<string>() : AttemptEngine.ParseKeywords(q.RequiredKeywords);
                        if (kwList.Count < sec.RequiredKeywordCount)
                        {
                            var msg = $"Question #{q.DisplayOrder} in [{sec.Code}] requires at least {sec.RequiredKeywordCount} keywords (found {kwList.Count}).";
                            issues.Add(new ExamAuditIssueDto(
                                Code: "Q_KEYWORDS_MISSING",
                                Severity: "error",
                                Message: msg,
                                SectionId: sec.SectionId,
                                SectionCode: sec.Code,
                                GroupId: g.GroupId,
                                QuestionId: q.QuestionId,
                                ElementTarget: $"question-card-{q.QuestionId}"));
                            missingItems.Add(msg);
                        }
                    }

                    if (string.Equals(q.QuestionType, "mcq", StringComparison.OrdinalIgnoreCase) || string.Equals(q.QuestionType, "mcq_3", StringComparison.OrdinalIgnoreCase))
                    {
                        var optCount = q.QuestionOptionQuestionNavigations.Count;
                        if (optCount < 2)
                        {
                            issues.Add(new ExamAuditIssueDto(
                                Code: "Q_OPTIONS_FEW",
                                Severity: "error",
                                Message: $"{q.QuestionType.ToUpperInvariant()} Question #{q.DisplayOrder} in [{sec.Code}] must have at least 2 options (found {optCount}).",
                                SectionId: sec.SectionId,
                                SectionCode: sec.Code,
                                GroupId: g.GroupId,
                                QuestionId: q.QuestionId,
                                ElementTarget: $"question-card-{q.QuestionId}"));
                        }

                        if (sec.OptionCount > 0 && optCount != sec.OptionCount)
                        {
                            var msg = $"Question #{q.DisplayOrder} in [{sec.Code}] has {optCount} options (blueprint expects {sec.OptionCount}).";
                            issues.Add(new ExamAuditIssueDto(
                                Code: "Q_OPTIONS_COUNT_MISMATCH",
                                Severity: "warning",
                                Message: msg,
                                SectionId: sec.SectionId,
                                SectionCode: sec.Code,
                                GroupId: g.GroupId,
                                QuestionId: q.QuestionId,
                                ElementTarget: $"question-card-{q.QuestionId}"));
                        }

                        var correctCount = q.QuestionOptionQuestionNavigations.Count(o => o.IsCorrect);
                        if (correctCount != 1)
                        {
                            issues.Add(new ExamAuditIssueDto(
                                Code: "Q_OPTIONS_ONE_CORRECT",
                                Severity: "error",
                                Message: $"{q.QuestionType.ToUpperInvariant()} Question #{q.DisplayOrder} in [{sec.Code}] must have exactly one correct option (found {correctCount}) (UX_Options_OneCorrect).",
                                SectionId: sec.SectionId,
                                SectionCode: sec.Code,
                                GroupId: g.GroupId,
                                QuestionId: q.QuestionId,
                                ElementTarget: $"question-card-{q.QuestionId}"));
                        }
                    }
                    else if (string.Equals(q.QuestionType, "multi_select", StringComparison.OrdinalIgnoreCase))
                    {
                        var optCount = q.QuestionOptionQuestionNavigations.Count;
                        if (optCount < 2)
                        {
                            issues.Add(new ExamAuditIssueDto(
                                Code: "Q_OPTIONS_FEW",
                                Severity: "error",
                                Message: $"Multi-select Question #{q.DisplayOrder} in [{sec.Code}] must have at least 2 options (found {optCount}).",
                                SectionId: sec.SectionId,
                                SectionCode: sec.Code,
                                GroupId: g.GroupId,
                                QuestionId: q.QuestionId,
                                ElementTarget: $"question-card-{q.QuestionId}"));
                        }

                        var correctCount = q.QuestionOptionQuestionNavigations.Count(o => o.IsCorrect);
                        if (correctCount < 1)
                        {
                            issues.Add(new ExamAuditIssueDto(
                                Code: "Q_OPTIONS_NONE_CORRECT",
                                Severity: "error",
                                Message: $"Multi-select Question #{q.DisplayOrder} in [{sec.Code}] must have at least one correct option.",
                                SectionId: sec.SectionId,
                                SectionCode: sec.Code,
                                GroupId: g.GroupId,
                                QuestionId: q.QuestionId,
                                ElementTarget: $"question-card-{q.QuestionId}"));
                        }
                    }
                    else if (string.Equals(q.QuestionType, "inline_completion", StringComparison.OrdinalIgnoreCase))
                    {
                        var hasAccepted = !string.IsNullOrWhiteSpace(q.AcceptedAnswersJson) || q.QuestionOptionQuestionNavigations.Any(o => o.IsCorrect);
                        if (!hasAccepted)
                        {
                            issues.Add(new ExamAuditIssueDto(
                                Code: "Q_ACCEPTED_ANSWERS_MISSING",
                                Severity: "error",
                                Message: $"Inline completion Question #{q.DisplayOrder} in [{sec.Code}] must have at least one accepted answer.",
                                SectionId: sec.SectionId,
                                SectionCode: sec.Code,
                                GroupId: g.GroupId,
                                QuestionId: q.QuestionId,
                                ElementTarget: $"question-card-{q.QuestionId}"));
                        }
                    }
                    else if (string.Equals(q.QuestionType, "dropdown", StringComparison.OrdinalIgnoreCase) || string.Equals(q.QuestionType, "drag_drop", StringComparison.OrdinalIgnoreCase))
                    {
                        var hasAnswers = !string.IsNullOrWhiteSpace(q.AcceptedAnswersJson) || q.QuestionOptionQuestionNavigations.Any(o => o.IsCorrect);
                        if (!hasAnswers)
                        {
                            issues.Add(new ExamAuditIssueDto(
                                Code: "Q_OPTIONS_NONE_CORRECT",
                                Severity: "error",
                                Message: $"{q.QuestionType} Question #{q.DisplayOrder} in [{sec.Code}] must have at least one correct option or accepted answer.",
                                SectionId: sec.SectionId,
                                SectionCode: sec.Code,
                                GroupId: g.GroupId,
                                QuestionId: q.QuestionId,
                                ElementTarget: $"question-card-{q.QuestionId}"));
                        }
                    }
                    else if (string.Equals(q.QuestionType, "free_response", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!sectionCriteria.Any(sc => sc.SectionId == sec.SectionId && sc.SkillId == q.SkillId))
                        {
                            issues.Add(new ExamAuditIssueDto(
                                Code: "Q_FR_CRITERIA_MISSING",
                                Severity: "error",
                                Message: $"Free response Question #{q.DisplayOrder} in [{sec.Code}] must have at least one rubric criterion assigned to its section.",
                                SectionId: sec.SectionId,
                                SectionCode: sec.Code,
                                GroupId: g.GroupId,
                                QuestionId: q.QuestionId,
                                ElementTarget: $"question-card-{q.QuestionId}"));
                        }

                        if (q.ResponseSeconds is null or <= 0)
                        {
                            issues.Add(new ExamAuditIssueDto(
                                Code: "Q_FR_RESPONSE_SEC",
                                Severity: "error",
                                Message: $"Free response Question #{q.DisplayOrder} in [{sec.Code}] must have response seconds > 0.",
                                SectionId: sec.SectionId,
                                SectionCode: sec.Code,
                                GroupId: g.GroupId,
                                QuestionId: q.QuestionId,
                                ElementTarget: $"question-card-{q.QuestionId}"));
                        }

                        if (sec.Modality == "writing"
                            && (q.MinWords is null or <= 0 || q.MaxWords is null or <= 0 || q.MaxWords < q.MinWords))
                        {
                            issues.Add(new ExamAuditIssueDto(
                                Code: "Q_FR_WORD_BOUNDS",
                                Severity: "error",
                                Message: $"Free response Writing question #{q.DisplayOrder} in [{sec.Code}] must have positive minimum and maximum word counts, with max >= min.",
                                SectionId: sec.SectionId,
                                SectionCode: sec.Code,
                                GroupId: g.GroupId,
                                QuestionId: q.QuestionId,
                                ElementTarget: $"question-card-{q.QuestionId}"));
                        }

                        if (sec.Modality == "speaking" && q.PrepSeconds is null or <= 0)
                        {
                            issues.Add(new ExamAuditIssueDto(
                                Code: "Q_FR_PREP_SEC",
                                Severity: "error",
                                Message: $"Free response Speaking question #{q.DisplayOrder} in [{sec.Code}] must have preparation seconds > 0.",
                                SectionId: sec.SectionId,
                                SectionCode: sec.Code,
                                GroupId: g.GroupId,
                                QuestionId: q.QuestionId,
                                ElementTarget: $"question-card-{q.QuestionId}"));
                        }
                    }
                }
            }
        }

        var blockingCount = issues.Count(i => i.Severity == "error");
        var warningCount = issues.Count(i => i.Severity == "warning");
        var canPublish = blockingCount == 0 && totalQuestions > 0;

        return new ExamAuditResultDto(
            CanPublish: canPublish,
            TotalQuestions: totalQuestions,
            ExpectedQuestions: expectedTotal > 0 ? expectedTotal : totalQuestions,
            BlockingErrorCount: blockingCount,
            WarningCount: warningCount,
            Issues: issues,
            MissingItems: missingItems);
    }
}
