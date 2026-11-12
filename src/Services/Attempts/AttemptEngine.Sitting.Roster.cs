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
    /// #250 / #262 / #269: the paper's questions, from the one source a paper has - its roster.
    /// <para>
    /// A roster row carries every field the projection needs from the paper - section, position, unit,
    /// modality - and the pinned <c>ItemVersion</c> carries the scored content, so every downstream
    /// reader (navigation locks, clustering, the take view, the navigator, submission) is shape-blind.
    /// </para>
    /// <para>
    /// <b>#274: the attempt's own paper wins when it has one.</b> A sitting that was served a drawn
    /// paper (<c>AttemptStageBundles</c> non-empty) reads its own positions from
    /// <c>AttemptItemResponses</c>, in the order they were served; a sitting with no bundles reads the
    /// exam's shared roster exactly as before, so a paper sat before this change keeps working. Both
    /// branches return the same shape, so nothing downstream learns there are two.
    /// </para>
    /// <para>
    /// <b>Why the shared roster is not rewritten per learner.</b>
    /// <c>UQ_ExamItems_Exam_Section_Order</c> allows one position per (exam, section, position): a
    /// per-learner roster on <c>ExamItems</c> cannot be stored at all, and putting learner sittings in
    /// the admin catalogue is what the drawing ticket refused. The attempt-scoped list is this
    /// table's reason to exist, and the roster is never mutated.
    /// </para>
    /// </summary>
    internal async Task<List<SittingQuestion>> LoadSittingQuestionsAsync(
        int examId, CancellationToken cancellationToken) =>
        await LoadSittingQuestionsAsync(examId, attemptId: null, cancellationToken);

    /// <summary>
    /// #274: the same seam, resolved attempt-first. <paramref name="attemptId"/> names the sitting whose
    /// own paper is preferred; null is the exam-only read (an admin preview, or a caller that only has
    /// an exam id).
    /// </summary>
    internal async Task<List<SittingQuestion>> LoadSittingQuestionsAsync(
        int examId, int? attemptId, CancellationToken cancellationToken)
    {
        // #274: the served positions, ordered as the learner meets them. Empty for every attempt that
        // reads the shared roster - which is all of them, until an attempt-scoped paper is written.
        var servedPositionIds = attemptId is int id
            ? await LoadServedPositionIdsAsync(id, cancellationToken)
            : [];

        return await ProjectPaperAsync(examId, servedPositionIds, cancellationToken);
    }

    /// <summary>
    /// #274: the positions this sitting was served, in sitting order. Empty means "read the exam's
    /// shared roster", so a caller can tell the two branches apart without a second query.
    /// </summary>
    internal Task<List<int>> LoadServedPositionIdsAsync(int attemptId, CancellationToken cancellationToken) =>
        db.AttemptItemResponses
            .AsNoTracking()
            .Where(r => r.AttemptId == attemptId)
            .OrderBy(r => r.PositionNumber)
            .Select(r => r.ExamItemId)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// #274: the paper a list of positions renders. Shared by the sitting reads (which pass what the
    /// attempt was served) and by attempt creation (which passes the draw it is about to write), so one
    /// projection serves both and the second cannot drift from the first.
    /// </summary>
    internal async Task<List<SittingQuestion>> ProjectPaperAsync(
        int examId, IReadOnlyList<int> servedPositionIds, CancellationToken cancellationToken)
    {
        var rows = await RosterRowsAsync(examId, servedPositionIds, cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        // The shared stimulus, read once for the row set rather than per item.
        var stimulusIds = rows
            .Where(r => r.Item.StimulusId.HasValue)
            .Select(r => r.Item.StimulusId!.Value)
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
            var stimulus = row.Item.StimulusId is int stimulusId
                ? stimuli.GetValueOrDefault(stimulusId)
                : null;
            var version = row.Version;

            // Audio resolution matches the legacy path: a stimulus slice wins, and a section master
            // track is the fallback.
            var audio = ExamAudio.Resolve(
                stimulus?.AudioPath,
                stimulus?.AudioStartMs,
                stimulus?.AudioEndMs,
                sectionMasterAudio.GetValueOrDefault(row.Section.SectionId));

            list.Add(new SittingQuestion(
                Id: row.Binding.ExamItemId,
                ExamId: examId,
                ExamItemId: row.Binding.ExamItemId,
                ItemVersionId: version.ItemVersionId,
                ItemId: row.Binding.ItemId,
                SectionId: row.Section.SectionId,
                RenderFormat: row.Item.RenderFormat,
                Stem: version.Stem,
                Explanation: version.Explanation,
                Options: PoolOptions(version.ItemVersionId, version)
                    .Select(o => new SittingOption(
                        o.OptionId, o.Label, o.Text,
                        IsCorrect: IsKeyLabel(version, o.Label),
                        Rationale: OptionRationale(version, o.Label)))
                    .ToList(),
                MinWords: version.MinWords,
                MaxWords: version.MaxWords,
                PrepSeconds: version.PrepSeconds ?? stimulus?.PrepSeconds,
                ResponseSeconds: version.ResponseSeconds ?? stimulus?.ResponseSeconds,
                MaxSelections: version.MaxSelections,
                AllowNumber: version.AllowNumber,
                AcceptedAnswersJson: version.AcceptedAnswersJson,
                RequiredKeywords: version.RequiredKeywords,
                Hint: version.Hint,
                // #262: a read-aloud task's reference lives on the version, so producing practice can
                // build its pronunciation prompt without a question row.
                ReferenceText: version.ReferenceText,
                ReferenceKind: version.ReferenceKind,
                // A pool position's grouping identity is its stimulus: the navigation locks compare
                // "am I in the same bundle", and a stimulus IS the bundle.
                // #269: a solo position has no stimulus to share, so its block address is its own negated
                // position id - the same convention the Studio rail and the exporter use. A null GroupId
                // would make a locked-unit paper unreachable by TransitionGroupAsync.
                GroupId: row.Item.StimulusId ?? -row.Binding.ExamItemId,
                Section: new SittingSection(
                    SectionId: row.Section.SectionId,
                    Code: row.Section.Code,
                    Name: row.Section.Name,
                    SkillId: row.Section.SkillId,
                    Modality: row.Section.Modality,
                    LayoutType: row.Section.LayoutType,
                    RequirePassage: row.Section.RequirePassage,
                    NavigationMode: row.Section.NavigationMode,
                    ToolsConfigJson: row.Section.ToolsConfigJson,
                    HideOptionText: row.Section.HideOptionText,
                    OptionCount: row.Section.OptionCount > 0 ? row.Section.OptionCount : 4,
                    ClusterSize: row.Section.ClusterSize > 0 ? row.Section.ClusterSize : 1,
                    RequireImage: row.Section.RequireImage,
                    TimerPolicyJson: row.Section.TimerPolicyJson,
                    ScoreWeight: row.Section.ScoreWeight,
                    Skill: new SittingSkill(
                        SkillId: row.Skill.SkillId,
                        Code: row.Skill.Code,
                        Name: row.Skill.Name,
                        DisplayOrder: row.Skill.DisplayOrder,
                        DurationMinutes: row.Skill.DurationMinutes,
                        LockNavigationOneWay: row.Skill.LockNavigationOneWay)),
                Group: new SittingGroup(
                    UnitNumber: row.Binding.UnitNumber,
                    Passage: stimulus?.Passage,
                    Transcript: stimulus?.Transcript,
                    ImagePath: stimulus?.ImagePath,
                    ImageAlt: stimulus?.ImageAlt,
                    AudioPath: audio?.Path,
                    AudioStartMs: audio?.StartMs,
                    AudioEndMs: audio?.EndMs,
                    // #269: a locked unit's clock is the stimulus's own window. Without this the sub-timer
                    // fell through to the paper's overall duration and a 480-second unit showed 3600.
                    ResponseSeconds: stimulus?.ResponseSeconds)));
        }

        return list;
    }

    /// <summary>
    /// #274: one roster row joined to everything the projection reads from it. A named record rather
    /// than an anonymous type because two orderings produce it - the paper's own order and the shared
    /// roster's - and both must return the same shape.
    /// </summary>
    private sealed record RosterRow(ExamItem Binding, Section Section, Skill Skill, ItemVersion Version, Item Item);

    /// <summary>
    /// The rows a sitting is served: its own positions in served order when it has any, the exam's
    /// shared roster in skill/section/position order otherwise.
    /// <para>
    /// The served branch keeps the ORDER of <paramref name="servedPositionIds"/>: that list is the
    /// sitting order the learner was handed, and re-sorting it by the roster's own keys would renumber
    /// the navigator under them. The shared branch is sorted by skill, then section, then the binding's
    /// own position, because <c>DisplayOrder</c> is defined as "the paper's position, 1-based within its
    /// section" and the navigator's numbering must not move when a paper migrates.
    /// </para>
    /// </summary>
    private async Task<List<RosterRow>> RosterRowsAsync(
        int examId, IReadOnlyList<int> servedPositionIds, CancellationToken cancellationToken)
    {
        var query =
            from i in db.ExamItems.AsNoTracking()
            join s in db.Sections.AsNoTracking() on i.SectionId equals s.SectionId
            join k in db.Skills.AsNoTracking() on s.SkillId equals k.SkillId
            join v in db.ItemVersions.AsNoTracking() on i.ItemVersionId equals v.ItemVersionId
            join item in db.Items.AsNoTracking() on i.ItemId equals item.ItemId
            where i.ExamId == examId
            select new { Binding = i, Section = s, Skill = k, Version = v, Item = item };

        if (servedPositionIds.Count == 0)
        {
            // Sorted in SQL, on the anonymous shape: EF cannot translate an ORDER BY over a projected
            // record's members.
            var shared = await query
                .OrderBy(r => r.Skill.DisplayOrder)
                .ThenBy(r => r.Section.DisplayOrder)
                .ThenBy(r => r.Binding.DisplayOrder)
                .ToListAsync(cancellationToken);
            return shared
                .Select(r => new RosterRow(r.Binding, r.Section, r.Skill, r.Version, r.Item))
                .ToList();
        }

        var served = await query
            .Where(r => servedPositionIds.Contains(r.Binding.ExamItemId))
            .ToListAsync(cancellationToken);

        // Ordered in memory from the id list, not by a SQL ORDER BY: the order IS the list, and a
        // second ordering it could disagree with is the defect this whole seam exists to prevent.
        var byPositionId = served.ToDictionary(r => r.Binding.ExamItemId);
        return servedPositionIds
            .Select(id => byPositionId.GetValueOrDefault(id))
            .Where(row => row is not null)
            .Select(row => new RosterRow(row!.Binding, row.Section, row.Skill, row.Version, row.Item))
            .ToList();
    }

    /// <summary>
    /// #250: the paper position a request names, resolved through the same seam the projection uses.
    /// <para>
    /// The client sends back the id the projection gave it, an <c>ExamItemId</c>. Resolving it here
    /// means every writer - answer, draft, flag, navigation - agrees with the reader about what a
    /// position is.
    /// </para>
    /// </summary>
    private async Task<SittingQuestion> FindSittingQuestionAsync(
        ExamAttempt attempt, int questionId, CancellationToken cancellationToken)
    {
        var questions = await LoadSittingQuestionsAsync(attempt.ExamId, attempt.AttemptId, cancellationToken);
        return questions.FirstOrDefault(q => q.Id == questionId)
            ?? throw new AttemptEngineException(
                AttemptEngineException.QuestionNotFound,
                $"Question {questionId} not found in attempt.");
    }

    /// <summary>
    /// #269: remember where the learner is. <c>ExamAttempts.CurrentExamItemId</c> is a roster position,
    /// so the recorded value is the position's <c>ExamItemId</c> - the same id every answer-shape table
    /// carries.
    /// </summary>
    private static void RecordLastPosition(ExamAttempt attempt, SittingQuestion q)
    {
        attempt.CurrentExamItemId = q.ExamItemId;
    }

    /// <summary>
    /// #274: the one writer of the paper a sitting is served. Called from creation, inside the same
    /// transaction as the attempt row, and by nothing else - which is what the single-writer trigger
    /// enforces rather than trusts.
    /// <para>
    /// The served positions are the <b>projected</b> paper's ids, not the draw's list: a draw that named
    /// a position the projection dropped (a retired binding, a version that no longer exists) would
    /// otherwise leave the sitting holding a position it never rendered.
    /// </para>
    /// </summary>
    private void RecordServedPaper(
        ExamAttempt attempt, AttemptPaperDraw paper, IReadOnlyList<SittingQuestion> questions)
    {
        foreach (var bundle in paper.Bundles)
        {
            db.AttemptStageBundles.Add(new AttemptStageBundle
            {
                AttemptId = attempt.AttemptId,
                CertId = attempt.CertId,
                SkillId = bundle.SkillId,
                StageNumber = bundle.StageNumber,
                BandLower = bundle.BandLower,
                BandUpper = bundle.BandUpper,
                WrittenByService = DerivedFactWriters.AttemptPaper,
                ProjectionVersion = 1,
            });
        }

        for (var index = 0; index < questions.Count; index++)
        {
            db.AttemptItemResponses.Add(new AttemptItemResponse
            {
                AttemptId = attempt.AttemptId,
                ExamId = attempt.ExamId,
                PositionNumber = index + 1,
                ExamItemId = questions[index].ExamItemId,
            });
        }
    }
}
