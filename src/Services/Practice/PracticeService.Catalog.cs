using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class PracticeService
{

    public async Task<PracticeSectionsView> GetPracticeSectionsAsync(int userId, CancellationToken cancellationToken = default)
    {
        var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var cert = await db.Certificates
            .AsNoTracking()
            .Where(c => c.CertId == activeCertId)
            .Select(c => new CertificateOption(c.Code, c.Name))
            .SingleAsync(cancellationToken);

        var sections = await db.Sections
            .AsNoTracking()
            .Where(s => s.CertId == activeCertId)
            .Include(s => s.Skill)
            .OrderBy(s => s.Skill.DisplayOrder)
            .ThenBy(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);

        var questionCounts = await db.Questions
            .AsNoTracking()
            .Where(q => q.Section.CertId == activeCertId && q.QuestionType != QuestionType.FreeResponse.ToCode() && q.QuestionType != "writing" && q.QuestionType != "speaking")
            .GroupBy(q => q.SectionId)
            .Select(g => new { SectionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.SectionId, g => g.Count, cancellationToken);

        var accuracies = await db.vw_UserSectionAccuracyAlls
            .AsNoTracking()
            .Where(v => v.UserId == userId)
            .ToDictionaryAsync(v => v.SectionId, cancellationToken);

        var items = sections.Select(s =>
        {
            int answered = 0;
            int? accuracy = null;
            if (accuracies.TryGetValue(s.SectionId, out var acc) && acc.Answered is { } total && total > 0)
            {
                answered = total;
                accuracy = (int)Math.Round((double)(acc.Correct ?? 0) / total * 100);
            }

            questionCounts.TryGetValue(s.SectionId, out var available);
            return new PracticeSectionItem(
                s.SectionId,
                s.Skill.Code,
                s.Skill.Name,
                EnumCodes.Parse<Modality>(s.Modality),
                s.Code,
                s.Name,
                s.OptionCount,
                s.ExpectedQuestionCount ?? 0,
                available,
                accuracy,
                answered,
                s.DisplayOrder);
        }).ToList();

        var listening = items.Where(i => i.Modality == Modality.Listening).ToList();
        var reading = items.Where(i => i.Modality == Modality.Reading).ToList();
        var other = items.Where(i => i.Modality is not (Modality.Listening or Modality.Reading)).ToList();

        // Weakest part: listening/reading section with lowest accuracy among those with at least 1 answer
        var weakest = items
            .Where(i => i.Modality is Modality.Listening or Modality.Reading && i.AccuracyPercentage.HasValue && i.AnsweredCount > 0)
            .OrderBy(i => i.AccuracyPercentage!.Value)
            .ThenByDescending(i => i.AnsweredCount)
            .FirstOrDefault();

        var unfinishedSession = await db.PracticeSessions
            .AsNoTracking()
            .Include(ps => ps.Section)
            .Where(ps => ps.UserId == userId
                && ps.Section.CertId == activeCertId
                && ps.Status == PracticeStatus.InProgress.ToCode()
                && ps.LearnerPathStepId == null)
            .OrderByDescending(ps => ps.StartedAt)
            .Select(ps => new PracticeSessionInfo(
                ps.SessionId,
                ps.SectionId,
                ps.Section.Code,
                ps.Section.Name,
                EnumCodes.Parse<Modality>(ps.Section.Modality),
                ps.QuestionCount,
                EnumCodes.Parse<PracticeStatus>(ps.Status),
                ps.CorrectCount,
                EnumCodes.Parse<QuestionSource>(ps.QuestionSource),
                EnumCodes.Parse<FeedbackMode>(ps.FeedbackMode),
                ps.StartedAt,
                ps.FinishedAt))
            .FirstOrDefaultAsync(cancellationToken);

        return new PracticeSectionsView(cert, listening, reading, other, weakest, unfinishedSession);
    }
}
