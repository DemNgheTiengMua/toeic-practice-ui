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
    /// #250: the paper's questions, read from the POOL when the paper was authored there, and from the
    /// legacy rows otherwise.
    /// <para>
    /// <b>Why this is the whole ticket in one method.</b> <see cref="LoadExamQuestionsAsync"/> reads
    /// <c>db.Questions</c>, and every downstream reader - navigation locks, clustering, the take view,
    /// the navigator, submission - was written against a <c>Question</c> row. That made the legacy table
    /// impossible to drop: even a fully migrated paper still had to read it, because the projection had
    /// no other source for <i>where a question sits</i>.
    /// </para>
    /// <para>
    /// This method supplies that source. A roster row already carries every field the projection needs
    /// from the paper - section, position, unit, modality - and the pinned <c>ItemVersion</c> carries
    /// the scored content. So a paper built from <c>ExamItems</c> and one built from <c>Questions</c>
    /// arrive at the same <see cref="SittingQuestion"/> shape, and every reader after this point is
    /// shape-blind.
    /// </para>
    /// <para>
    /// <b>Which branch, and why it is decided this way.</b> A paper is pool-authored only when it has NO
    /// legacy <c>Questions</c> rows. That is what criterion 1 states - "builds a paper from ExamItems
    /// ALONE" - and it is the only rule that is safe during the transition, because a paper that has
    /// BOTH (the seed's assembled paper does: bindings beside question rows) has answers recorded
    /// against <c>QuestionId</c>, and reading it through the roster would look for an <c>ExamItemId</c>
    /// those answers do not carry. Measured: the first draft preferred the roster whenever bindings
    /// existed, and
    /// <c>Saving_an_answer_records_the_pinned_label_and_scores_against_the_pinned_key</c> came back with
    /// a null selected option, because the answer's legacy key never met the roster's position key.
    /// </para>
    /// <para>
    /// The count query is the transition's cost, and criterion 5 pays it off: when the three legacy
    /// tables are dropped, this becomes "read the roster" and nothing else.
    /// </para>
    /// </summary>
    private async Task<List<SittingQuestion>> LoadSittingQuestionsAsync(
        int examId, CancellationToken cancellationToken)
    {
        var legacyCount = await db.Questions
            .AsNoTracking()
            .CountAsync(q => q.ExamId == examId, cancellationToken);

        return legacyCount > 0
            ? await LoadLegacyQuestionsAsync(examId, cancellationToken)
            : await LoadRosterAsync(examId, cancellationToken);
    }

    /// <summary>
    /// The paper as the pool describes it: one row per <c>ExamItems</c> binding, in sitting order.
    /// Reads no <c>Questions</c> row at all.
    /// <para>
    /// Ordering is by skill, then section, then the binding's own position - the same order the legacy
    /// branch produced, because the roster's <c>DisplayOrder</c> is defined as "the paper's position,
    /// 1-based within its section" and the navigator's numbering must not move when a paper migrates.
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

        // The shared stimulus, read once for the row set rather than per item.
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

            // Audio resolution matches the legacy path: a stimulus slice wins, and a section master
            // track is the fallback.
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
                RenderFormat: row.item.RenderFormat,
                Stem: version.Stem,
                LegacyStem: null,
                Explanation: version.Explanation,
                LegacyExplanation: null,
                Options: PoolOptions(version.ItemVersionId, version)
                    .Select(o => new SittingOption(
                        o.OptionId, o.Label, o.Text,
                        IsCorrect: IsKeyLabel(version, o.Label),
                        Rationale: OptionRationale(version, o.Label)))
                    .ToList(),
                LegacyOptions: null,
                MinWords: version.MinWords,
                MaxWords: version.MaxWords,
                PrepSeconds: version.PrepSeconds ?? stimulus?.PrepSeconds,
                ResponseSeconds: version.ResponseSeconds ?? stimulus?.ResponseSeconds,
                MaxSelections: version.MaxSelections,
                AllowNumber: version.AllowNumber,
                AcceptedAnswersJson: version.AcceptedAnswersJson,
                RequiredKeywords: version.RequiredKeywords,
                Hint: version.Hint,
                // A pool position's grouping identity is its stimulus: the navigation locks compare
                // "am I in the same bundle", and a stimulus IS the bundle.
                GroupId: row.item.StimulusId,
                Section: new SittingSection(
                    SectionId: row.s.SectionId,
                    Code: row.s.Code,
                    Name: row.s.Name,
                    SkillId: row.s.SkillId,
                    Modality: row.s.Modality,
                    LayoutType: row.s.LayoutType,
                    RequirePassage: row.s.RequirePassage,
                    NavigationMode: row.s.NavigationMode,
                    ToolsConfigJson: row.s.ToolsConfigJson,
                    HideOptionText: row.s.HideOptionText,
                    OptionCount: row.s.OptionCount > 0 ? row.s.OptionCount : 4,
                    ClusterSize: row.s.ClusterSize > 0 ? row.s.ClusterSize : 1,
                    RequireImage: row.s.RequireImage,
                    TimerPolicyJson: row.s.TimerPolicyJson,
                    ScoreWeight: row.s.ScoreWeight,
                    Skill: new SittingSkill(
                        SkillId: row.k.SkillId,
                        Code: row.k.Code,
                        Name: row.k.Name,
                        DisplayOrder: row.k.DisplayOrder,
                        DurationMinutes: row.k.DurationMinutes,
                        LockNavigationOneWay: row.k.LockNavigationOneWay)),
                Group: new SittingGroup(
                    UnitNumber: row.i.UnitNumber,
                    Passage: stimulus?.Passage,
                    Transcript: stimulus?.Transcript,
                    ImagePath: stimulus?.ImagePath,
                    ImageAlt: stimulus?.ImageAlt,
                    AudioPath: audio?.Path,
                    AudioStartMs: audio?.StartMs,
                    AudioEndMs: audio?.EndMs)));
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

        // The legacy audio chain, resolved here rather than in the projection: a group's own slice
        // wins, and the section's uploaded master track is the fallback. Both are properties of the
        // paper, so they belong to the loader.
        var sectionMasterAudio = await LoadSectionMasterAudioAsync(examId, cancellationToken);

        return questions
            .Select(q => FromLegacyQuestion(
                q,
                boundByQuestion.GetValueOrDefault(q.QuestionId),
                ResolveGroupAudio(q.Group, sectionMasterAudio)))
            .ToList();
    }

    /// <summary>One legacy <c>Question</c> row as a <see cref="SittingQuestion"/>.</summary>
    private SittingQuestion FromLegacyQuestion(
        Question q, BoundPaperPosition? bound, ResolvedGroupAudio? audio) => new(
        Id: q.QuestionId,
        ExamId: q.ExamId,
        ExamItemId: null,
        ItemVersionId: bound?.ItemVersionId,
        ItemId: bound?.ItemId ?? 0,
        SectionId: q.SectionId,
        // #186: the pinned version's stem and options are the scored content. Everything else - type,
        // word limits, timers, the section's chrome - is the paper's, so it still comes from the row.
        RenderFormat: q.QuestionType,
        Stem: bound?.Version.Stem ?? q.Stem,
        LegacyStem: q.Stem,
        Explanation: bound?.Version.Explanation ?? q.Explanation,
        LegacyExplanation: q.Explanation,
        Options: bound is null
            ? q.QuestionOptionQuestionNavigations
                .OrderBy(o => o.Label)
                .Select(o => new SittingOption(o.OptionId, o.Label, o.Text, o.IsCorrect, o.Rationale))
                .ToList()
            : PoolOptions(bound.ItemVersionId, bound.Version)
                .Select(o => new SittingOption(
                    o.OptionId, o.Label, o.Text,
                    IsCorrect: IsKeyLabel(bound.Version, o.Label),
                    Rationale: OptionRationale(bound.Version, o.Label)))
                .ToList(),
        // The row's own options, kept beside the scored ones so review can render what the learner
        // answered rather than what the paper binds today.
        LegacyOptions: q.QuestionOptionQuestionNavigations
            .OrderBy(o => o.Label)
            .Select(o => new SittingOption(o.OptionId, o.Label, o.Text, o.IsCorrect, o.Rationale))
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
        GroupId: q.GroupId,
        Section: new SittingSection(
            SectionId: q.SectionId,
            Code: q.Section?.Code ?? "",
            Name: q.Section?.Name ?? "",
            SkillId: q.Section?.SkillId ?? 0,
            Modality: q.Section?.Modality ?? "reading",
            LayoutType: q.Section?.LayoutType,
            RequirePassage: q.Section?.RequirePassage ?? false,
            NavigationMode: q.Section?.NavigationMode,
            ToolsConfigJson: q.Section?.ToolsConfigJson,
            HideOptionText: q.Section?.HideOptionText ?? false,
            OptionCount: q.Section is { OptionCount: > 0 } s ? s.OptionCount : 4,
            ClusterSize: q.Section is { ClusterSize: > 0 } c ? c.ClusterSize : 1,
            RequireImage: q.Section?.RequireImage ?? false,
            TimerPolicyJson: q.Section?.TimerPolicyJson,
            ScoreWeight: q.Section?.ScoreWeight ?? 1.0m,
            Skill: new SittingSkill(
                SkillId: q.Section?.SkillId ?? 0,
                Code: q.Section?.Skill?.Code ?? "",
                Name: q.Section?.Skill?.Name ?? "",
                DisplayOrder: q.Section?.Skill?.DisplayOrder ?? 0,
                DurationMinutes: q.Section?.Skill?.DurationMinutes,
                LockNavigationOneWay: q.Section?.Skill?.LockNavigationOneWay ?? false)),
        Group: new SittingGroup(
            UnitNumber: q.Group?.UnitNumber ?? 1,
            Passage: q.Group?.Passage,
            Transcript: q.Group?.Transcript,
            ImagePath: q.Group?.ImagePath,
            ImageAlt: q.Group?.ImageAlt,
            AudioPath: audio?.Path,
            AudioStartMs: audio?.StartMs,
            AudioEndMs: audio?.EndMs,
            ResponseSeconds: q.Group?.ResponseSeconds),
        // The legacy scoring path needs the row itself; nothing else may branch on it.
        LegacyQuestion: q);

    /// <summary>
    /// #250: the paper position a request names, resolved through the same seam the projection uses.
    /// <para>
    /// The client sends back the id the projection gave it, and that id is a <c>QuestionId</c> on a
    /// legacy paper and an <c>ExamItemId</c> on a pool paper. Resolving it here means every writer -
    /// answer, draft, flag, navigation - agrees with the reader about what a position is, instead of
    /// each querying <c>Questions</c> on its own.
    /// </para>
    /// </summary>
    private async Task<SittingQuestion> FindSittingQuestionAsync(
        ExamAttempt attempt, int questionId, CancellationToken cancellationToken)
    {
        var questions = await LoadSittingQuestionsAsync(attempt.ExamId, cancellationToken);
        return questions.FirstOrDefault(q => q.Id == questionId)
            ?? throw new AttemptEngineException(
                AttemptEngineException.QuestionNotFound,
                $"Question {questionId} not found in attempt.");
    }

    /// <summary>
    /// #250: remember where the learner is, for the one column that can hold it.
    /// <para>
    /// <b>The limit, stated rather than hidden.</b> <c>ExamAttempts.LastQuestionId</c> carries a foreign
    /// key to <c>Questions(QuestionId, ExamId)</c>. A pool position's id is an <c>ExamItemId</c>, which
    /// is not a question id, so writing it here would either fail the foreign key or - worse, on a
    /// database whose id spaces overlap - silently point at an unrelated question. So this records the
    /// position only for a legacy paper, and a pool paper resumes at its first unlocked position.
    /// </para>
    /// <para>
    /// That is a real limitation and it is why criterion 5 has to repoint the column before the legacy
    /// tables go: a pool-only paper cannot yet remember a mid-paper position across a reload.
    /// </para>
    /// </summary>
    private static void RecordLastPosition(ExamAttempt attempt, SittingQuestion q)
    {
        if (q.LegacyQuestion is not null)
        {
            attempt.LastQuestionId = q.Id;
        }
    }
}
