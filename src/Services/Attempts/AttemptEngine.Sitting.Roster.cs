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
    /// Ordering is by skill, then section, then the binding's own position, because the roster's
    /// <c>DisplayOrder</c> is defined as "the paper's position, 1-based within its section" and the
    /// navigator's numbering must not move when a paper migrates.
    /// </para>
    /// </summary>
    internal async Task<List<SittingQuestion>> LoadSittingQuestionsAsync(
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
                GroupId: row.item.StimulusId ?? -row.i.ExamItemId,
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
                    AudioEndMs: audio?.EndMs,
                    // #269: a locked unit's clock is the stimulus's own window. Without this the sub-timer
                    // fell through to the paper's overall duration and a 480-second unit showed 3600.
                    ResponseSeconds: stimulus?.ResponseSeconds)));
        }

        return list;
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
        var questions = await LoadSittingQuestionsAsync(attempt.ExamId, cancellationToken);
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
}
