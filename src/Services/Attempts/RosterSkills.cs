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
/// The fact lives on the position the answer was written against
/// (<c>AttemptAnswers.ExamItemId</c> → <c>ExamItems.SectionId</c> → <c>Sections.SkillId</c>). This is
/// that lookup, in one place, so two readers cannot disagree about which skill an answer counts for.
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
            .Select(a => new { a.AnswerId, a.ExamItemId })
            .ToListAsync(cancellationToken);

        if (answers.Count == 0)
        {
            return [];
        }

        // #269: ExamItemId is required on AttemptAnswer, so every answer names a position.
        var positionIds = answers
            .Select(a => a.ExamItemId)
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

        var map = new Dictionary<int, int>(answers.Count);
        foreach (var answer in answers)
        {
            var skillId = skillByPosition.GetValueOrDefault(answer.ExamItemId);

            if (skillId != 0)
            {
                map[answer.AnswerId] = skillId;
            }
        }

        return map;
    }

    /// <summary>
    /// #269: free-response id → section id, resolved from the roster position a response names. The
    /// sibling of <see cref="ByAnswerAsync"/> for the productive half, so a rubric override is looked up
    /// against the section the task was actually sat in.
    /// </summary>
    public static async Task<Dictionary<int, int>> ByResponseSectionAsync(
        CompanionDbContext db, int attemptId, CancellationToken cancellationToken = default)
    {
        var responses = await db.FreeResponses
            .AsNoTracking()
            .Where(fr => fr.AttemptId == attemptId)
            .Select(fr => new { fr.FreeResponseId, fr.ExamItemId })
            .ToListAsync(cancellationToken);

        if (responses.Count == 0)
        {
            return [];
        }

        // #269: ExamItemId is required on FreeResponse, so every response names a position.
        var positionIds = responses
            .Select(r => r.ExamItemId)
            .Distinct()
            .ToList();

        var sectionByPosition = positionIds.Count == 0
            ? []
            : await db.ExamItems
                .AsNoTracking()
                .Where(i => positionIds.Contains(i.ExamItemId))
                .Select(i => new { i.ExamItemId, i.SectionId })
                .ToDictionaryAsync(x => x.ExamItemId, x => x.SectionId, cancellationToken);

        var map = new Dictionary<int, int>(responses.Count);
        foreach (var response in responses)
        {
            var sectionId = sectionByPosition.GetValueOrDefault(response.ExamItemId);

            if (sectionId != 0)
            {
                map[response.FreeResponseId] = sectionId;
            }
        }

        return map;
    }
}
