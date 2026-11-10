using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class AttemptEngine
{
    /// <summary>
    /// #250: the paper's questions, read from the POOL when the paper has a roster.
    /// <para>
    /// <b>Why this is the whole ticket in one method.</b> <see cref="LoadExamQuestionsAsync"/> reads
    /// <c>db.Questions</c>, and every downstream reader — navigation locks, clustering, the take view,
    /// the navigator, submission — was written against a <c>Question</c> row. That made the legacy table
    /// impossible to drop: even a fully migrated paper still had to read it, because the projection had
    /// no other source for <i>where a question sits</i>.
    /// </para>
    /// <para>
    /// This method supplies that source. A roster row already carries every field the projection needs
    /// from the paper — section, position, unit, modality — and the pinned <c>ItemVersion</c> carries
    /// the scored content. So a paper built from <c>ExamItems</c> and one built from <c>Questions</c>
    /// arrive at the same <see cref="SittingQuestion"/> shape, and every reader after this point is
    /// shape-blind.
    /// </para>
    /// <para>
    /// <b>The legacy branch is explicit, not a silent fallback.</b> A paper with no roster rows reads
    /// its <c>Questions</c> exactly as before, and that branch is named
    /// <see cref="LoadLegacyQuestionsAsync"/> so a reader can see which path a paper took. When criterion
    /// 5 of #250 drops the three tables, that branch goes with them; until then, 17 of 18 seeded papers
    /// still depend on it and a migration nobody has run yet is not a reason to break them.
    /// </para>
    /// </summary>
    private async Task<List<SittingQuestion>> LoadSittingQuestionsAsync(
        int examId, CancellationToken cancellationToken)
    {
        var roster = await LoadRosterAsync(examId, cancellationToken);
        return roster.Count > 0
            ? roster
            : await LoadLegacyQuestionsAsync(examId, cancellationToken);
    }

    /// <summary>
    /// The paper as the pool describes it: one row per <c>ExamItems</c> binding, in sitting order.
    /// Reads no <c>Questions</c> row at all.
    /// <para>
    /// Ordering is by skill, then section, then the binding's own position — the same order
    /// <see cref="LoadLegacyQuestionsAsync"/> produced, because the roster's
    /// <c>DisplayOrder</c> is defined as "the paper's position, 1-based within its section" and the
    /// navigator's numbering must not move when a paper migrates.
    /// </para>
    /// </summary>
    private async Task<List<SittingQuestion>> LoadRosterAsync(
        int examId, CancellationToken cancellationToken)
    {
        var rows = await db.ExamItems
            .AsNoTracking()
            .Where(i => i.ExamId == examId)
            .Join(db.Sections.AsNoTracking(),
                i => i.SectionId, s => s.SectionId,
                (i, s) => new { i, s })
            .Join(db.Skills.AsNoTracking(),
                x => x.s.SkillId, k => k.SkillId,
                (x, k) => new { x.i, x.s, k })
            .Join(db.ItemVersions.AsNoTracking(),
                x => x.i.ItemVersionId, v => v.ItemVersionId,
                (x, v) => new { x.i, x.s, x.k, v })
            .Join(db.Items.AsNoTracking(),
                x => x.i.ItemId, item => item.ItemId,
                (x, item) => new { x.i, x.s, x.k, x.v, item })
            .OrderBy(x => x.k.DisplayOrder)
            .ThenBy(x => x.s.DisplayOrder)
            .ThenBy(x => x.i.DisplayOrder)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        // The shared stimulus, when the item has one. Read once for the row set rather than per item.
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

        var sectionMasterAudio = await LoadSectionMasterAudioAsync(examId, cancellationToken);

        var list = new List<SittingQuestion>(rows.Count);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var stimulus = row.item.StimulusId is int stimulusId
                ? stimuli.GetValueOrDefault(stimulusId)
                : null;
            var version = row.v;

            var options = PoolOptions(version.ItemVersionId, version)
                .Select(o => new SittingOption(o.OptionId, o.Label, o.Text))
                .ToList();

            // Audio resolution matches the legacy path: a stimulus slice wins, and a section master
            // track is the fallback. Resolved here rather than in the projection so both paths hand
            // the view the same resolved values.
            var audio = ExamAudio.Resolve(
                stimulus?.AudioPath,
                stimulus?.AudioStartMs,
                stimulus?.AudioEndMs,
                sectionMasterAudio.GetValueOrDefault(row.s.SectionId));

            list.Add(new SittingQuestion(
                Id: row.i.ExamItemId,
                ExamId: examId,
                ExamItemId: row.i.ExamItemId,
                ItemVersionId: version.ItemVersionId,
                ItemId: row.i.ItemId,
                SectionId: row.s.SectionId,
                SectionCode: row.s.Code,
                SectionName: row.s.Name,
                SkillId: row.k.SkillId,
                SkillCode: row.k.Code,
                SkillName: row.k.Name,
                SkillDisplayOrder: row.k.DisplayOrder,
                SkillDurationMinutes: row.k.DurationMinutes,
                Modality: row.s.Modality,
                RenderFormat: row.item.RenderFormat,
                Stem: version.Stem,
                ImagePath: stimulus?.ImagePath,
                ImageAlt: stimulus?.ImageAlt,
                AudioPath: audio?.Path,
                AudioStartMs: audio?.StartMs,
                AudioEndMs: audio?.EndMs,
                Transcript: stimulus?.Transcript,
                Passage: stimulus?.Passage,
                Options: options,
                MinWords: version.MinWords,
                MaxWords: version.MaxWords,
                PrepSeconds: version.PrepSeconds ?? stimulus?.PrepSeconds,
                ResponseSeconds: version.ResponseSeconds ?? stimulus?.ResponseSeconds,
                MaxSelections: version.MaxSelections,
                AllowNumber: version.AllowNumber,
                AcceptedAnswersJson: version.AcceptedAnswersJson,
                RequiredKeywords: version.RequiredKeywords,
                Hint: version.Hint,
                LayoutType: row.s.LayoutType,
                RequirePassage: row.s.RequirePassage,
                NavigationMode: row.s.NavigationMode,
                ToolsConfigJson: row.s.ToolsConfigJson,
                HideOptionText: row.s.HideOptionText,
                OptionCount: row.s.OptionCount > 0 ? row.s.OptionCount : 4,
                ClusterSize: row.s.ClusterSize > 0 ? row.s.ClusterSize : 1,
                RequireImage: row.s.RequireImage,
                UnitNumber: row.i.UnitNumber,
                // A pool position's grouping identity is its stimulus: the navigation locks compare
                // "am I in the same bundle", and a stimulus IS the bundle.
                GroupId: row.item.StimulusId,
                TimerPolicyJson: row.s.TimerPolicyJson));
        }

        return list;
    }

    /// <summary>
    /// The paper as the legacy rows describe it. Unchanged behaviour, but mapped into the same
    /// <see cref="SittingQuestion"/> shape so no downstream reader branches on which source it came
    /// from. This is the branch criterion 5 deletes along with the tables.
    /// <para>
    /// <b>The pinned version is overlaid here, once.</b> A legacy paper may still carry
    /// <c>ExamItems</c> bindings (the seed's assembled paper does), and #186 says the bound version's
    /// stem and options are what the sitting renders. Doing that overlay at load time is what lets the
    /// projection stay free of bindings entirely: it reads a <see cref="SittingQuestion"/> and cannot
    /// tell whether the content came from a question row or a pinned version.
    /// </para>
    /// </summary>
    private async Task<List<SittingQuestion>> LoadLegacyQuestionsAsync(
        int examId, CancellationToken cancellationToken)
    {
        var questions = await LoadExamQuestionsAsync(examId, cancellationToken);

        var bindings = await LoadPaperBindingsAsync(examId, cancellationToken);
        EnsureBindingsRender(questions, bindings);
        var boundByQuestion = PlaceBindings(questions, bindings);

        return questions
            .Select(q => FromLegacyQuestion(q, boundByQuestion.GetValueOrDefault(q.QuestionId)))
            .ToList();
    }

    /// <summary>One legacy <c>Question</c> row as a <see cref="SittingQuestion"/>.</summary>
    private SittingQuestion FromLegacyQuestion(Question q, BoundPaperPosition? bound) => new(
        Id: q.QuestionId,
        ExamId: q.ExamId,
        ExamItemId: null,
        ItemVersionId: bound?.ItemVersionId,
        ItemId: bound?.ItemId ?? 0,
        SectionId: q.SectionId,
        SectionCode: q.Section?.Code ?? "",
        SectionName: q.Section?.Name ?? "",
        SkillId: q.Section?.SkillId ?? 0,
        SkillCode: q.Section?.Skill?.Code ?? "",
        SkillName: q.Section?.Skill?.Name ?? "",
        SkillDisplayOrder: q.Section?.Skill?.DisplayOrder ?? 0,
        SkillDurationMinutes: q.Section?.Skill?.DurationMinutes,
        Modality: q.Section?.Modality ?? "reading",
        // #186: the pinned version's stem and options are the scored content. Everything else - type,
        // word limits, timers, the section's chrome - is the paper's, so it still comes from the row.
        RenderFormat: q.QuestionType,
        Stem: bound?.Version.Stem ?? q.Stem,
        ImagePath: q.Group?.ImagePath,
        ImageAlt: q.Group?.ImageAlt,
        AudioPath: null,
        AudioStartMs: null,
        AudioEndMs: null,
        Transcript: q.Group?.Transcript,
        Passage: q.Group?.Passage,
        Options: bound is null
            ? q.QuestionOptionQuestionNavigations
                .OrderBy(o => o.Label)
                .Select(o => new SittingOption(o.OptionId, o.Label, o.Text))
                .ToList()
            : PoolOptions(bound.ItemVersionId, bound.Version)
                .Select(o => new SittingOption(o.OptionId, o.Label, o.Text))
                .ToList(),
        MinWords: q.MinWords,
        MaxWords: q.MaxWords,
        PrepSeconds: q.PrepSeconds,
        ResponseSeconds: q.ResponseSeconds,
        MaxSelections: q.MaxSelections,
        AllowNumber: q.AllowNumber,
        AcceptedAnswersJson: q.AcceptedAnswersJson,
        RequiredKeywords: q.RequiredKeywords,
        Hint: q.Hint,
        LayoutType: q.Section?.LayoutType,
        RequirePassage: q.Section?.RequirePassage ?? false,
        NavigationMode: q.Section?.NavigationMode,
        ToolsConfigJson: q.Section?.ToolsConfigJson,
        HideOptionText: q.Section?.HideOptionText ?? false,
        OptionCount: q.Section is { OptionCount: > 0 } s ? s.OptionCount : 4,
        ClusterSize: q.Section is { ClusterSize: > 0 } c ? c.ClusterSize : 1,
        RequireImage: q.Section?.RequireImage ?? false,
        UnitNumber: q.Group?.UnitNumber ?? 1,
        GroupId: q.GroupId,
        TimerPolicyJson: q.Section?.TimerPolicyJson,
        // The legacy scoring path needs the row itself; nothing else may branch on it.
        LegacyQuestion: q);
}
