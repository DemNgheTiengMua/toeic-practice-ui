using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #269: which skill a written answer counts for.
/// <para>
/// The legacy path read <c>AttemptAnswer.Question.Section.SkillId</c>, so every per-skill tally in the
/// codebase leaned on the question row. A pool answer has no question row; the same fact lives on the
/// position it was written against (<c>AttemptAnswers.ExamItemId</c> → <c>ExamItems.SectionId</c> →
/// <c>Sections.SkillId</c>). This is that lookup, in one place, so two readers cannot disagree about
/// which skill an answer counts for.
/// </para>
/// <para>
/// <b>Why the legacy branch is still here.</b> An answer written before a paper was migrated pins a
/// <c>QuestionId</c> and nothing else, and it must keep scoring against the skill it was actually
/// answered in. The two shapes are resolved into ONE map keyed by answer id, so a caller never branches
/// on which store an answer came from. The <c>Questions</c> half dies with the table (#269 criterion 5);
/// the <c>ExamItems</c> half is what remains.
/// </para>
/// <para>
/// Only the answers the attempt actually holds are resolved: the map is never a full paper read, so a
/// caller cannot accidentally count a position the learner has not reached.
/// </para>
/// </summary>
internal static class RosterSkills
{
    /// <summary>Answer id → skill id, for every answer on the attempt that can name a skill.</summary>
    public static async Task<Dictionary<int, int>> ByAnswerAsync(
        CompanionDbContext db, int attemptId, CancellationToken cancellationToken = default)
    {
        var answers = await db.AttemptAnswers
            .AsNoTracking()
            .Where(a => a.AttemptId == attemptId)
            .Select(a => new { a.AnswerId, a.ExamItemId, a.QuestionId })
            .ToListAsync(cancellationToken);

        if (answers.Count == 0)
        {
            return [];
        }

        var positionIds = answers
            .Where(a => a.ExamItemId.HasValue)
            .Select(a => a.ExamItemId!.Value)
            .Distinct()
            .ToList();

        var questionIds = answers
            .Where(a => !a.ExamItemId.HasValue && a.QuestionId.HasValue)
            .Select(a => a.QuestionId!.Value)
            .Distinct()
            .ToList();

        var skillByPosition = positionIds.Count == 0
            ? []
            : await db.ExamItems
                .AsNoTracking()
                .Where(i => positionIds.Contains(i.ExamItemId))
                .Join(db.Sections.AsNoTracking(),
                    i => i.SectionId, s => s.SectionId,
                    (i, s) => new { i.ExamItemId, s.SkillId })
                .ToDictionaryAsync(x => x.ExamItemId, x => x.SkillId, cancellationToken);

        var skillByQuestion = questionIds.Count == 0
            ? []
            : await db.Questions
                .AsNoTracking()
                .Where(q => questionIds.Contains(q.QuestionId))
                .Join(db.Sections.AsNoTracking(),
                    q => q.SectionId, s => s.SectionId,
                    (q, s) => new { q.QuestionId, s.SkillId })
                .ToDictionaryAsync(x => x.QuestionId, x => x.SkillId, cancellationToken);

        var map = new Dictionary<int, int>(answers.Count);
        foreach (var answer in answers)
        {
            var skillId = answer.ExamItemId is int positionId
                ? skillByPosition.GetValueOrDefault(positionId)
                : answer.QuestionId is int questionId
                    ? skillByQuestion.GetValueOrDefault(questionId)
                    : 0;

            if (skillId != 0)
            {
                map[answer.AnswerId] = skillId;
            }
        }

        return map;
    }
}
