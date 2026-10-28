using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class PracticeService
{

    public async Task<PracticeSummary> GetSummaryAsync(int userId, int sessionId, CancellationToken cancellationToken = default)
    {
        var session = await db.PracticeSessions
            .Include(s => s.Section)
            .Where(s => s.SessionId == sessionId && s.UserId == userId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new PracticeException(PracticeException.SessionNotFound, $"Practice session {sessionId} was not found.");

        if (session.Status == PracticeStatus.InProgress.ToCode())
        {
            session.Status = PracticeStatus.Finished.ToCode();
            session.FinishedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await PruneOldSessionsAsync(userId, session.Section.CertId, cancellationToken);
        }

        var answers = await db.PracticeAnswers
            .AsNoTracking()
            .Include(pa => pa.Question)
                .ThenInclude(q => q.Category)
            .Include(pa => pa.Question)
                .ThenInclude(q => q.Group)
            .Where(pa => pa.SessionId == sessionId)
            .OrderBy(pa => pa.DisplayOrder)
            .ToListAsync(cancellationToken);

        var questionIds = answers.Select(a => a.QuestionId).ToList();
        var allOptions = await db.QuestionOptions
            .AsNoTracking()
            .Where(o => questionIds.Contains(o.QuestionId))
            .ToListAsync(cancellationToken);

        var outcomes = answers.Select(a =>
        {
            var q = a.Question;
            var qOptions = allOptions.Where(o => o.QuestionId == q.QuestionId).ToList();
            var selectedOpt = a.SelectedOptionId.HasValue
                ? qOptions.FirstOrDefault(o => o.OptionId == a.SelectedOptionId.Value)
                : null;
            var correctOpt = qOptions.FirstOrDefault(o => o.IsCorrect);

            return new QuestionOutcome(
                a.DisplayOrder,
                q.QuestionId,
                q.Category?.Name,
                q.Stem,
                selectedOpt?.Label,
                selectedOpt?.Text,
                correctOpt?.Label,
                correctOpt?.Text,
                a.IsCorrect,
                a.HintUsed,
                q.Explanation,
                q.Group.AudioPath,
                q.Group.AudioStartMs,
                q.Group.AudioEndMs,
                q.Group.Transcript,
                q.QuestionType ?? "mcq",
                a.ResponseText,
                a.SelectedOptionIdsJson,
                q.AcceptedAnswersJson);
        }).ToList();

        var total = answers.Count;
        var answered = answers.Count(a => a.SelectedOptionId.HasValue || a.IsCorrect.HasValue);
        var correct = answers.Count(a => a.IsCorrect == true);
        var incorrect = answered - correct;
        var skipped = total - answered;
        var accuracy = answered > 0 ? (int)Math.Round((double)correct / answered * 100) : 0;
        var hintUsedTotal = answers.Count(a => a.HintUsed);
        var correctWithHint = answers.Count(a => a.HintUsed && a.IsCorrect == true);
        var incorrectWithHint = answers.Count(a => a.HintUsed && a.IsCorrect == false);

        return new PracticeSummary(
            session.SessionId,
            session.Section.Code,
            session.Section.Name,
            EnumCodes.Parse<Modality>(session.Modality),
            session.StartedAt,
            session.FinishedAt,
            total,
            answered,
            correct,
            incorrect,
            skipped,
            accuracy,
            hintUsedTotal,
            correctWithHint,
            incorrectWithHint,
            outcomes);
    }
}
