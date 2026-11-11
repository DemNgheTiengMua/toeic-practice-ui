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
    /// #185: the answer key is read through <c>ExamItems</c> to the pinned <c>ItemVersion</c>. That is
    /// what makes the binding the source of a key: a re-versioned item changes what a <em>future</em>
    /// paper binds, and this read follows the version this paper actually pinned.
    /// </para>
    /// <para>
    /// #269: the roster is the paper's only shape now, so the legacy <c>Questions</c> fallback is gone -
    /// a rendered position always has a binding, and therefore always has a pinned version to read its
    /// key from.
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
    /// #185: one answer key per rendered question, from the version the binding pins. Keyed by the
    /// position the sitting projection renders, so the caller cannot tell where it came from.
    /// </summary>
    private async Task<List<ExamPreviewAnswer>> ResolvePreviewAnswersAsync(
        int examId,
        AttemptTakeView take,
        CancellationToken cancellationToken)
    {
        // A binding names an item version, not a question row, so the join to the rendered paper is the
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

        var positionInSection = new Dictionary<int, int>();
        var answers = new List<ExamPreviewAnswer>(take.Questions!.Count);
        foreach (var item in take.Questions!)
        {
            var position = positionInSection[item.SectionId] = positionInSection.GetValueOrDefault(item.SectionId) + 1;

            // #269: every rendered position has a binding - the roster IS the paper - so the key is the
            // pinned version's, always. A position with no row here would be a paper the loader could not
            // have produced.
            if (!keyByPosition.TryGetValue((item.SectionCode, position), out var version))
            {
                answers.Add(new ExamPreviewAnswer(item.QuestionId, [], null));
                continue;
            }

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
        }

        return answers;
    }
}
