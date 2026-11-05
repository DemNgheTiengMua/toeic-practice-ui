using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class AdminExamService
{
    /// <summary>
    /// #158: the exam as a learner would sit it — the real sitting projection plus the answer key —
    /// without publishing it and without touching <c>ExamAttempts</c>. Composed from the same
    /// projection the sitting uses, so an admin approves what the learner will actually get.
    /// <para>
    /// #185: where the paper is assembled from the pool, the answer key is read through
    /// <c>ExamItems</c> to the pinned <c>ItemVersion</c> rather than from the legacy
    /// <c>Questions</c>/<c>QuestionOptions</c> pair. That is what makes the binding the source of a
    /// key: a re-versioned item changes what a <em>future</em> paper binds, and this read follows the
    /// version this paper actually pinned.
    /// </para>
    /// <para>
    /// A position with no binding still reads the legacy row, because #231 deletes those and #231 has
    /// not run. Falling back per position rather than per paper is deliberate: a half-assembled paper
    /// must show the keys it has, not blank the ones it has not reached yet.
    /// </para>
    /// </summary>
    public async Task<ExamPreviewTakeView> GetExamPreviewTakeAsync(
        int examId,
        int? questionDisplayOrder,
        CancellationToken cancellationToken = default)
    {
        var exam = await db.Exams.AsNoTracking()
            .FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken)
            ?? throw new ExamNotFoundException(examId);

        var take = await _attemptEngine.GetExamPreviewTakeAsync(examId, questionDisplayOrder, cancellationToken);

        var answers = await ResolvePreviewAnswersAsync(examId, take, cancellationToken);

        var sections = take.Questions!
            .GroupBy(item => item.SectionId)
            .Select(group => new ExamPreviewSection(
                group.Key,
                group.First().SectionCode,
                group.First().SectionName,
                group.Min(item => item.DisplayOrder),
                group.Max(item => item.DisplayOrder),
                group.Count()))
            .OrderBy(section => section.FirstDisplayOrder)
            .ToList();

        return new ExamPreviewTakeView(take, answers, sections, exam.DurationMinutes, exam.Status);
    }

    /// <summary>
    /// #185: one answer key per rendered question. A bound position's key comes from the version the
    /// binding pins; an unbound position's comes from the legacy question row. Both are keyed by the
    /// question the sitting projection renders, so the caller cannot tell which path supplied it.
    /// </summary>
    private async Task<List<ExamPreviewAnswer>> ResolvePreviewAnswersAsync(
        int examId,
        AttemptTakeView take,
        CancellationToken cancellationToken)
    {
        // A binding names an item version, not a QuestionId, so the join to the rendered paper is the
        // position: ExamItems.DisplayOrder is 1-based within its section (UQ_ExamItems_Exam_Section_Order
        // makes that pair unique) and the projection renders the same order.
        var bound = await db.ExamItems.AsNoTracking()
            .Where(b => b.ExamId == examId)
            .Join(db.Sections.AsNoTracking(), b => b.SectionId, s => s.SectionId,
                (b, s) => new { Binding = b, s.Code })
            .Join(db.ItemVersions.AsNoTracking(), x => x.Binding.ItemVersionId, v => v.ItemVersionId,
                (x, v) => new { x.Code, x.Binding.DisplayOrder, Version = v })
            .ToListAsync(cancellationToken);

        var keyByPosition = bound.ToDictionary(
            b => (b.Code, b.DisplayOrder),
            b => b.Version);

        // The legacy key, read once. Only consulted for positions the pool has not assembled.
        var legacy = await db.Questions.AsNoTracking()
            .Where(q => q.ExamId == examId)
            .Select(q => new
            {
                q.QuestionId,
                q.Explanation,
                Correct = q.QuestionOptionQuestionNavigations.Where(o => o.IsCorrect).Select(o => o.OptionId).ToList(),
            })
            .ToListAsync(cancellationToken);
        var legacyById = legacy.ToDictionary(q => q.QuestionId);

        var positionInSection = new Dictionary<int, int>();
        var answers = new List<ExamPreviewAnswer>(take.Questions!.Count);
        foreach (var item in take.Questions!)
        {
            var position = positionInSection[item.SectionId] = positionInSection.GetValueOrDefault(item.SectionId) + 1;

            if (keyByPosition.TryGetValue((item.SectionCode, position), out var version))
            {
                // The pinned version's key label resolves against the options the projection renders:
                // A stays A. A productive version has no key, so it contributes no correct option and
                // keeps its explanation, which is what the grader is judged against, not an answer.
                var correct = version.KeyLabel is null
                    ? []
                    : item.Options
                        .Where(o => string.Equals(o.Label, version.KeyLabel, StringComparison.OrdinalIgnoreCase))
                        .Select(o => o.OptionId)
                        .ToList();
                answers.Add(new ExamPreviewAnswer(item.QuestionId, correct, version.Explanation));
                continue;
            }

            answers.Add(legacyById.TryGetValue(item.QuestionId, out var row)
                ? new ExamPreviewAnswer(item.QuestionId, row.Correct, row.Explanation)
                : new ExamPreviewAnswer(item.QuestionId, [], null));
        }

        return answers;
    }
}
