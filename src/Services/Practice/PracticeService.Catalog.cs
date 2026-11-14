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
    /// <summary>
    /// #263: the pool items a section can drill, grouped by the section they sit in.
    /// <para>
    /// <b>Why the section is derived rather than stored.</b> A pool item is certificate-agnostic and
    /// section-free by design (#184), so there is no section column to filter on. What a section means in
    /// the pool is "the items its paper binds into it", which <c>ExamItems</c> already records - so the
    /// section's cell is read from the bindings, and a section with no binding has no stock.
    /// </para>
    /// </summary>
    private async Task<Dictionary<int, int>> CountDrillableBySectionAsync(
        int certId, CancellationToken cancellationToken)
    {
        var rows = await db.ExamItems
            .AsNoTracking()
            .Where(ei => ei.CertId == certId)
            .Join(db.Items.AsNoTracking(), ei => ei.ItemId, i => i.ItemId, (ei, i) => new { ei.SectionId, ei.ItemId, i.Modality, i.LifecycleStatus })
            .Where(x => x.Modality != SpeakingCode && x.LifecycleStatus == ItemSelector.ActiveLifecycle)
            .Select(x => new { x.SectionId, x.ItemId })
            .Distinct()
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => x.SectionId)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>
    /// #263: the pool items a section drills, in the order the planner expects.
    /// <para>
    /// A section drill draws from the items its paper binds into it, so the drill and the paper can never
    /// disagree about what a section contains. The cell each item belongs to is its own (topic, level),
    /// which is what mastery and cooldown are keyed by - not the section.
    /// </para>
    /// </summary>
    private async Task<List<PracticeCandidate>> LoadSectionCandidatesAsync(
        int userId, int certId, int sectionId, CancellationToken cancellationToken)
    {
        var rows = await db.ExamItems
            .AsNoTracking()
            .Where(ei => ei.CertId == certId && ei.SectionId == sectionId)
            .Join(db.Items.AsNoTracking(), ei => ei.ItemId, i => i.ItemId, (ei, i) => new { ei, i })
            .Where(x => x.i.Modality != SpeakingCode && x.i.LifecycleStatus == ItemSelector.ActiveLifecycle)
            .Select(x => new { x.i.ItemId, x.i.TopicId, x.i.CefrLevel })
            .Distinct()
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            // The caller decides what an unstocked section does; this method answers only "what stock
            // does the pool hold here", and an empty list is the honest answer.
            return [];
        }

        var itemIds = rows.Select(r => r.ItemId).ToList();
        var answers = await db.PracticeAnswers
            .AsNoTracking()
            .Where(pa => pa.ItemId.HasValue && itemIds.Contains(pa.ItemId.Value) && pa.IsCorrect != null)
            .Join(db.PracticeSessions.AsNoTracking(), pa => pa.SessionId, ps => ps.SessionId, (pa, ps) => new { pa, ps })
            .Where(x => x.ps.UserId == userId)
            .Select(x => new { x.pa.ItemId, x.pa.IsCorrect, x.pa.AnsweredAt })
            .ToListAsync(cancellationToken);

        var byItem = answers
            .GroupBy(a => a.ItemId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var now = _clock.GetUtcNow();

        // #263: the cooldown is derived, not stored per question. A wrong answer holds its item back for
        // the same window the retired per-question table used, so a drill cannot immediately re-ask what
        // the learner just missed.
        return rows.Select(r =>
        {
            var mine = byItem.GetValueOrDefault(r.ItemId) ?? [];
            var lastAnsweredAt = mine
                .Where(a => a.AnsweredAt.HasValue)
                .Select(a => (DateTimeOffset?)new DateTimeOffset(a.AnsweredAt!.Value, TimeSpan.Zero))
                .Max();
            var lastWasWrong = mine
                .OrderByDescending(a => a.AnsweredAt)
                .Select(a => a.IsCorrect == false)
                .FirstOrDefault();

            var cooldownUntil = lastWasWrong && lastAnsweredAt.HasValue
                ? lastAnsweredAt.Value + MasterySchedule.WrongAnswerCooldown
                : (DateTimeOffset?)null;

            return new PracticeCandidate(
                r.ItemId,
                r.TopicId,
                LastAnsweredAt: lastAnsweredAt,
                CooldownUntil: cooldownUntil is { } c && c > now ? c : null);
        }).ToList();
    }

    private static readonly string SpeakingCode = Modality.Speaking.ToCode();

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

        // #263: availability is pool stock, read from the items this certificate's papers bind into each
        // section - the same source the session builder draws from, so the count cannot promise a drill
        // the builder then refuses.
        var questionCounts = await CountDrillableBySectionAsync(activeCertId, cancellationToken);

        var accuracies = await db.vw_UserSectionAccuracyAlls
            .AsNoTracking()
            .Where(v => v.UserId == userId)
            .ToDictionaryAsync(v => v.SectionId, cancellationToken);

        var practiceAccuracies = await db.PracticeAnswers
            .AsNoTracking()
            .Where(pa => pa.Session.UserId == userId
                && pa.Session.CertId == activeCertId
                && pa.Session.SectionId != null
                && pa.IsCorrect != null)
            .GroupBy(pa => pa.Session.SectionId!.Value)
            .Select(g => new
            {
                SectionId = g.Key,
                Answered = g.Count(),
                Correct = g.Count(pa => pa.IsCorrect == true)
            })
            .ToDictionaryAsync(x => x.SectionId, cancellationToken);

        // #155 / #263: which cells are resting, per section. A cell is the pool's unit of mastery
        // (topic, level), and a section's cells are the ones its bound items belong to.
        var now = _clock.GetUtcNow();
        var sectionCells = await db.ExamItems
            .AsNoTracking()
            .Where(ei => ei.CertId == activeCertId)
            .Join(db.Items.AsNoTracking(), ei => ei.ItemId, i => i.ItemId,
                (ei, i) => new { ei.SectionId, i.TopicId, i.CefrLevel })
            .Distinct()
            .ToListAsync(cancellationToken);

        var mastery = await LoadMasteryAsync(userId, cancellationToken);
        var rests = sectionCells
            .GroupBy(c => c.SectionId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var due = g
                        .Select(c => mastery.TryGetValue(c.TopicId, out var m) ? m.NextReviewDueAt : null)
                        .OfType<DateTimeOffset>()
                        .Where(d => d > now)
                        .ToList();
                    return (Total: g.Count(), Resting: due.Count, Next: due.Count > 0 ? (DateTimeOffset?)due.Min() : null);
                });

        // #313: Free Practice unlocks strictly when the learner has a 'done' step covering the section/skill
        // in their active Learning Path.
        var doneSteps = await db.LearnerPathSteps
            .AsNoTracking()
            .Where(s => s.LearnerPath.UserId == userId
                && s.LearnerPath.CertId == activeCertId
                && s.LearnerPath.Status == LearnerPathStatus.Active.ToCode()
                && s.Status == LearnerPathStepStatus.Done.ToCode())
            .Select(s => new { s.SkillId, s.Modality })
            .ToListAsync(cancellationToken);

        var unlockedSkillIds = doneSteps.Select(s => s.SkillId).ToHashSet();

        var items = sections.Select(s =>
        {
            int answered = 0;
            int correct = 0;
            if (accuracies.TryGetValue(s.SectionId, out var acc) && acc.Answered is { } total && total > 0)
            {
                answered += total;
                correct += acc.Correct ?? 0;
            }
            if (practiceAccuracies.TryGetValue(s.SectionId, out var pAcc) && pAcc.Answered > 0)
            {
                answered += pAcc.Answered;
                correct += pAcc.Correct;
            }
            int? accuracy = answered > 0 ? (int)Math.Round((double)correct / answered * 100) : null;

            questionCounts.TryGetValue(s.SectionId, out var available);
            rests.TryGetValue(s.SectionId, out var rest);
            var isUnlocked = unlockedSkillIds.Contains(s.SkillId);
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
                rest.Next,
                isUnlocked);
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

        // #313: Free Practice is stateless — no in_progress or abandoned session resume tracking.
        // Resume remains strictly on Mock & Placement ExamAttempts.
        return new PracticeSectionsView(cert, listening, reading, other, weakest,
            UnfinishedSession: null, speaking, speakingPracticeExamId);
    }
}
