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

        var questions = await db.Questions.AsNoTracking()
            .Where(q => q.ExamId == examId)
            .Select(q => new
            {
                q.QuestionId,
                q.Explanation,
                q.SectionId,
                q.GroupId,
                Correct = q.QuestionOptionQuestionNavigations.Where(o => o.IsCorrect).Select(o => o.OptionId).ToList(),
            })
            .ToListAsync(cancellationToken);
        var byId = questions.ToDictionary(q => q.QuestionId);

        var answers = take.Questions!
            .Select(item => byId.TryGetValue(item.QuestionId, out var row)
                ? new ExamPreviewAnswer(item.QuestionId, row.Correct, row.Explanation)
                : new ExamPreviewAnswer(item.QuestionId, [], null))
            .ToList();

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
}
