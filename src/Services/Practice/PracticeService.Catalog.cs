using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class PracticeService
{
    /// <summary>#150: the single rule for "can this section be answered in the drill?", used by both the
    /// catalog count and the session builder so the page can never advertise a drill the service refuses.
    /// Speaking is excluded because a spoken answer cannot be typed and its recorded, AI-graded attempt is
    /// a different flow (#151); every other modality drills either its free-response or its keyed questions.</summary>
    private IQueryable<Question> DrillableQuestions(int certId) =>
        db.Questions.AsNoTracking().Where(q => q.Section.CertId == certId
            && q.Section.Modality != SpeakingCode
            && (q.QuestionType == FreeResponseCode
                || (q.QuestionType != FreeResponseCode
                    && (q.AcceptedAnswersJson != null || db.QuestionOptions.Any(o => o.QuestionId == q.QuestionId)))));

    private static readonly string SpeakingCode = Modality.Speaking.ToCode();

    private static readonly string FreeResponseCode = QuestionType.FreeResponse.ToCode();

    public async Task<PracticeSectionsView> GetPracticeSectionsAsync(int userId, CancellationToken cancellationToken = default)
    {
        var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var cert = await db.Certificates
            .AsNoTracking()
            .Where(c => c.CertId == activeCertId)
            .Select(c => new CertificateOption(c.Code, c.Name, c.SectionNoun))
            .SingleAsync(cancellationToken);

        var sections = await db.Sections
            .AsNoTracking()
            .Where(s => s.CertId == activeCertId)
            .Include(s => s.Skill)
            .OrderBy(s => s.Skill.DisplayOrder)
            .ThenBy(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);

        var questionCounts = await DrillableQuestions(activeCertId)
            .GroupBy(q => q.SectionId)
            .Select(g => new { SectionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.SectionId, g => g.Count, cancellationToken);

        var accuracies = await db.vw_UserSectionAccuracyAlls
            .AsNoTracking()
            .Where(v => v.UserId == userId)
            .ToDictionaryAsync(v => v.SectionId, cancellationToken);

        // #155: which categories are resting, per section. A section with every category in cooldown is
        // what "you may come back in 6 hours" has to be measured against, so the count and the soonest
        // due date are computed here rather than guessed by the page.
        var now = _clock.GetUtcNow();
        var sectionCategories = await db.Questions
            .AsNoTracking()
            .Where(q => q.Section.CertId == activeCertId && q.CategoryId != null)
            .Select(q => new { q.SectionId, CategoryId = q.CategoryId!.Value })
            .Distinct()
            .ToListAsync(cancellationToken);

        var mastery = await LoadMasteryAsync(userId, cancellationToken);
        var rests = sectionCategories
            .GroupBy(c => c.SectionId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var due = g
                        .Select(c => mastery.TryGetValue(c.CategoryId, out var m) ? m.NextReviewDueAt : null)
                        .OfType<DateTimeOffset>()
                        .Where(d => d > now)
                        .ToList();
                    return (Total: g.Count(), Resting: due.Count, Next: due.Count > 0 ? (DateTimeOffset?)due.Min() : null);
                });

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
            rests.TryGetValue(s.SectionId, out var rest);
            return new PracticeSectionItem(
                s.SectionId,
                s.Skill.Code,
                s.Skill.Name,
                EnumCodes.Parse<Modality>(s.Modality),
                s.Code,
                s.Name,
                s.OptionCount,
                s.ExpectedQuestionCount,
                available,
                accuracy,
                answered,
                s.DisplayOrder,
                rest.Total,
                rest.Resting,
                rest.Next);
        }).ToList();

        var listening = items.Where(i => i.Modality == Modality.Listening).ToList();
        var reading = items.Where(i => i.Modality == Modality.Reading).ToList();
        // #150: writing sections drill; speaking does not (a spoken answer cannot be typed), so the page
        // links speaking to its recorded, AI-graded practice instead of offering a drill that refuses.
        var other = items.Where(i => i.Modality == Modality.Writing).ToList();
        var speaking = items.Where(i => i.Modality == Modality.Speaking).ToList();

        // Weakest part: listening/reading section with lowest accuracy among those with at least 1 answer
        var weakest = items
            .Where(i => i.Modality is Modality.Listening or Modality.Reading && i.AccuracyPercentage.HasValue && i.AnsweredCount > 0)
            .OrderBy(i => i.AccuracyPercentage!.Value)
            .ThenByDescending(i => i.AnsweredCount)
            .FirstOrDefault();

        var unfinishedSession = await db.PracticeSessions
            .AsNoTracking()
            .Include(ps => ps.Section)
            .Include(ps => ps.Topic)
            .Where(ps => ps.UserId == userId
                && ps.CertId == activeCertId
                && ps.Status == PracticeStatus.InProgress.ToCode()
                && ps.LearnerPathStepId == null)
            .OrderByDescending(ps => ps.StartedAt)
            .ToListAsync(cancellationToken);

        // #150: speaking has no typed drill. Point at its recorded, AI-graded practice when the
        // certificate has one published, so the card is a real link rather than a refusal.
        var speakingPracticeExamId = await db.Exams
            .AsNoTracking()
            .Where(e => e.CertId == activeCertId
                && e.Purpose == ExamPurpose.SpeakingPractice.ToCode()
                && e.Status == ExamStatus.Published.ToCode()
                && !e.IsDeleted)
            .OrderBy(e => e.ExamId)
            .Select(e => (int?)e.ExamId)
            .FirstOrDefaultAsync(cancellationToken);

        return new PracticeSectionsView(cert, listening, reading, other, weakest,
            unfinishedSession.Count == 0 ? null : ToSessionInfo(unfinishedSession[0]), speaking, speakingPracticeExamId);
    }
}
