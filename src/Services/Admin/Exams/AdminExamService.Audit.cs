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

        var sectionIds = sections.Select(s => s.SectionId).ToHashSet();
        var sectionCriteria = await db.SectionCriteria.AsNoTracking()
            .Where(sc => sectionIds.Contains(sc.SectionId))
            .ToListAsync(cancellationToken);

        return await AuditExamPublishReadinessInternal(exam, sections, sectionAudios, sectionCriteria, cancellationToken);
    }

    /// <summary>
    /// #269: the paper's positions, shaped as the group tree the publish checks were written against.
    /// <para>
    /// <b>Why a projection rather than a rewrite.</b> The ~30 checks below read a group and its questions
    /// by name - passage, image alt, audio slice, stem, type, required keywords, option count, key count.
    /// Every one of those facts still exists; only the table changed. So the roster is projected into the
    /// same member names and not one check moves. Rewriting 30 checks around a new shape would be 30
    /// chances to weaken one.
    /// </para>
    /// <para>
    /// <b>Grouping is the stimulus, exactly as the Studio addresses it.</b> A position's block is the
    /// stimulus its items share; a solo position has none, so it is its own block and its address is the
    /// negated roster id - the same convention <c>BuildPoolSectionDto</c> and the sitting loader use, which
    /// is why an issue's <c>group-card-{id}</c> target lands on the card the admin sees. Grouping is per
    /// SECTION as well as per stimulus: one stimulus is pool stock that two sections may both bind.
    /// </para>
    /// <para>
    /// <b>Options come from the version's four columns.</b> A pool version stores labels A-D and one key
    /// set rather than option rows, so the projection supplies one entry per NON-NULL column and marks the
    /// ones the key names. The count a check sees is therefore the number of options the sitting would
    /// render - an mcq with three filled columns is three, and the section blueprint's 4 is still a
    /// mismatch. A productive version contributes none, which is what its authoring shape says.
    /// </para>
    /// </summary>
    private async Task<List<AuditGroup>> LoadAuditGroupsAsync(int examId, CancellationToken cancellationToken)
    {
        var rows = await db.ExamItems.AsNoTracking()
            .Where(i => i.ExamId == examId)
            .Join(db.ItemVersions.AsNoTracking(), i => i.ItemVersionId, v => v.ItemVersionId, (i, v) => new { i, v })
            .Join(db.Items.AsNoTracking(), x => x.i.ItemId, item => item.ItemId, (x, item) => new { x.i, x.v, item })
            .Join(db.Sections.AsNoTracking(), x => x.i.SectionId, s => s.SectionId, (x, s) => new { x.i, x.v, x.item, s })
            .OrderBy(x => x.i.SectionId).ThenBy(x => x.i.DisplayOrder)
            .ToListAsync(cancellationToken);

        var stimulusIds = rows
            .Where(r => r.item.StimulusId.HasValue)
            .Select(r => r.item.StimulusId!.Value)
            .Distinct()
            .ToList();

        var stimuli = stimulusIds.Count == 0
            ? new Dictionary<int, Stimulus>()
            : (await db.Stimuli.AsNoTracking()
                .Where(s => stimulusIds.Contains(s.StimulusId))
                .ToListAsync(cancellationToken))
                .ToDictionary(s => s.StimulusId);

        return rows
            .GroupBy(r => r.i.SectionId)
            .SelectMany(section => section
                .GroupBy(r => r.item.StimulusId ?? -r.i.ExamItemId)
                .OrderBy(block => block.Min(r => r.i.DisplayOrder))
                .Select((block, index) =>
                {
                    var first = block.First();
                    var stimulus = first.item.StimulusId is int stimulusId
                        ? stimuli.GetValueOrDefault(stimulusId)
                        : null;

                    return new AuditGroup(
                        GroupId: block.Key,
                        SectionId: section.Key,
                        // Numbered inside its section, which is how the Studio rail numbers a block and
                        // what a "Group #N" message has to mean.
                        DisplayOrder: index + 1,
                        Passage: stimulus?.Passage,
                        ImagePath: stimulus?.ImagePath,
                        ImageAlt: stimulus?.ImageAlt,
                        AudioPath: stimulus?.AudioPath,
                        AudioStartMs: stimulus?.AudioStartMs,
                        AudioEndMs: stimulus?.AudioEndMs,
                        QuestionGroups: block
                            .OrderBy(r => r.i.DisplayOrder)
                            .Select(r => new AuditQuestion(
                                QuestionId: r.i.ExamItemId,
                                SectionId: r.i.SectionId,
                                SkillId: r.s.SkillId,
                                DisplayOrder: r.i.DisplayOrder,
                                Stem: r.v.Stem,
                                QuestionType: r.item.RenderFormat,
                                RequiredKeywords: r.v.RequiredKeywords,
                                AcceptedAnswersJson: r.v.AcceptedAnswersJson,
                                // A productive task's windows may be declared on the stimulus instead of
                                // the version; the sitting resolves them the same way, so the audit judges
                                // the number a learner is actually held to.
                                PrepSeconds: r.v.PrepSeconds ?? stimulus?.PrepSeconds,
                                ResponseSeconds: r.v.ResponseSeconds ?? stimulus?.ResponseSeconds,
                                MinWords: r.v.MinWords,
                                MaxWords: r.v.MaxWords,
                                QuestionOptionQuestionNavigations: AuditOptions(r.v)))
                            .ToList());
                }))
            .ToList();
    }

    /// <summary>The version's options as rows: one per filled column, marked when the key names it.</summary>
    private static List<AuditOption> AuditOptions(ItemVersion version)
    {
        var key = AttemptEngine.KeyLabels(version.KeyLabel);
        var texts = AttemptEngine.OptionTexts(version);

        var options = new List<AuditOption>(texts.Count);
        for (var index = 0; index < texts.Count; index++)
        {
            if (texts[index] is null)
            {
                continue;
            }

            options.Add(new AuditOption(key.Contains(((char)('A' + index)).ToString())));
        }

        return options;
    }

    /// <summary>A publish-audit block: the stimulus a run of positions shares, or one solo position.</summary>
    private sealed record AuditGroup(
        int GroupId,
        int SectionId,
        int DisplayOrder,
        string? Passage,
        string? ImagePath,
        string? ImageAlt,
        string? AudioPath,
        int? AudioStartMs,
        int? AudioEndMs,
        IReadOnlyList<AuditQuestion> QuestionGroups);

    /// <summary>One authored position, under the names the checks already read.</summary>
    private sealed record AuditQuestion(
        int QuestionId,
        int SectionId,
        int SkillId,
        int DisplayOrder,
        string Stem,
        string QuestionType,
        string? RequiredKeywords,
        string? AcceptedAnswersJson,
        int? PrepSeconds,
        int? ResponseSeconds,
        int? MinWords,
        int? MaxWords,
        IReadOnlyList<AuditOption> QuestionOptionQuestionNavigations);

    /// <summary>One rendered option. The version has no option id or label to carry; the count is what matters.</summary>
    private sealed record AuditOption(bool IsCorrect);

    private async Task<ExamAuditResultDto> AuditExamPublishReadinessInternal(
        Exam exam,
        List<Section> sections,
        List<ExamSectionAudio> sectionAudios,
        List<SectionCriterion> sectionCriteria,
        CancellationToken cancellationToken)
    {
        // #269: the paper's positions, grouped the way the Studio addresses them. Read here rather than
        // passed in, so neither caller can hand the audit a shape the sitting would not render.
        var groups = await LoadAuditGroupsAsync(exam.ExamId, cancellationToken);
        var issues = new List<ExamAuditIssueDto>();
        var missingItems = new List<string>();

        // #269: a position is a ROSTER row. Every count below used only `Questions`, so a
        // pool-authored paper reported zero everywhere and could never pass the publish door -
        // measured: "Exam must have at least one question group and question", "requires at least 2
        // questions, but only has 0", on a paper whose roster was complete.
        //
        // Read for the whole exam first: which sections a PLACEMENT paper serves is exactly the set it
        // binds, so that decision needs this.
        var rosterBySection = await db.ExamItems.AsNoTracking()
            .Where(i => i.ExamId == exam.ExamId)
            .GroupBy(i => i.SectionId)
            .Select(g => new { SectionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SectionId, x => x.Count, cancellationToken);

        // Which sections this paper is judged on.
        //
        // A mock or a practice paper covers a whole skill, so every section of that skill is in scope and
        // a gap means a hole on the paper. A PLACEMENT paper is a SLICE across skills: the stock is drawn
        // per (skill, band), so it binds one section per skill and nothing else. Auditing it against all
        // twenty TOEIC sections asked for 200 items on a seven-item paper and refused publication with
        // fifteen "section declares N but holds 0" errors - measured on the imported Reading B2 form.
        var relevantSections = exam.Purpose switch
        {
            "speaking_practice" => sections.Where(s => s.Modality == "speaking").ToList(),
            "writing_practice" => sections.Where(s => s.Modality == "writing").ToList(),
            "placement" => sections.Where(s => rosterBySection.ContainsKey(s.SectionId)).ToList(),
            _ => sections
        };

        var relevantSectionIds = relevantSections.Select(s => s.SectionId).ToHashSet();
        var relevantGroups = groups.Where(g => relevantSectionIds.Contains(g.SectionId)).ToList();

        int RenderedIn(int sectionId) => Math.Max(
            relevantGroups.Where(g => g.SectionId == sectionId).Sum(g => g.QuestionGroups.Count),
            rosterBySection.GetValueOrDefault(sectionId));

        var totalQuestions = relevantSections
            .Where(s => relevantSectionIds.Contains(s.SectionId))
            .Sum(s => RenderedIn(s.SectionId));

        // #157: ExpectedQuestionCount is NOT NULL, so every section now contributes. A section whose
        // count is missing can no longer make this sum silently low.
        var sectionExpectedSum = relevantSections.Sum(s => s.ExpectedQuestionCount);
        var expectedTotal = sectionExpectedSum > 0 ? sectionExpectedSum : (exam.QuestionCount ?? 0);

        if (groups.Count == 0 && totalQuestions == 0)
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
            var secRendered = RenderedIn(sec.SectionId);

            // #157: ExpectedQuestionCount is NOT NULL, so this check can no longer be skipped by
            // leaving the count blank. It is the check that stopped an empty part from publishing.
            //
            // #269: except for a PLACEMENT paper, whose size is its draw rather than the section's
            // blueprint - ExpectedQuestionCount is a certificate-level number shared by every paper on
            // that section, so a 7-item slice can never satisfy the 16 a full form declares. The same
            // rule INV-1 applies; the two are deliberately kept in step.
            if (exam.Purpose != "placement" && secRendered < sec.ExpectedQuestionCount)
            {
                var diff = sec.ExpectedQuestionCount - secRendered;
                var msg = $"Section [{sec.Code}] '{sec.Name}' requires {sec.ExpectedQuestionCount} questions, but only has {secRendered} ({diff} missing).";
                issues.Add(new ExamAuditIssueDto(
                    Code: "SEC_QUESTION_COUNT_LOW",
                    Severity: "error",
                    Message: msg,
                    SectionId: sec.SectionId,
                    SectionCode: sec.Code,
                    ElementTarget: $"section-link-{sec.SectionId}"));
                missingItems.Add(msg);
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

            if (sec.RequireAudio || (sec.Modality == "listening" && (secGroups.Count > 0 || sec.ExpectedQuestionCount > 0)))
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

        // #226 / ADM-12: the six publish invariants, as six rows. They judge a different question from
        // the ~26 checks above - whether a paper assembled from the warehouse may go live - so they are
        // added here rather than folded into that list, and each one is counted and named separately
        // because ADM-13 wants the unmet invariant named, not "cannot publish".
        var (invariantIssues, invariants) = await EvaluateInvariantsAsync(exam, sections, cancellationToken);
        issues.AddRange(invariantIssues);

        blockingCount = issues.Count(i => i.Severity == "error");
        warningCount = issues.Count(i => i.Severity == "warning");

        var canPublish = blockingCount == 0 && totalQuestions > 0;

        return new ExamAuditResultDto(
            CanPublish: canPublish,
            TotalQuestions: totalQuestions,
            ExpectedQuestions: expectedTotal > 0 ? expectedTotal : totalQuestions,
            BlockingErrorCount: blockingCount,
            WarningCount: warningCount,
            Issues: issues,
            MissingItems: missingItems,
            Invariants: invariants);
    }
}
