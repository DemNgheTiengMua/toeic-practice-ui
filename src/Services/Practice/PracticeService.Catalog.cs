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
    /// <para>
    /// shortgap: a section with NO pool bindings falls back to the legacy question count, because a
    /// certificate authored before the pool still has to drill. #269 drops that table and this branch with
    /// it; a section that HAS pool stock never consults the legacy count, so the two can never disagree
    /// about the same section.
    /// </para>
    /// </summary>
    private async Task<Dictionary<int, int>> CountDrillableBySectionAsync(
        int certId, CancellationToken cancellationToken)
    {
        var rows = await db.ExamItems
            .AsNoTracking()
            .Where(ei => ei.CertId == certId)
            .Join(db.Items.AsNoTracking(), ei => ei.ItemId, i => i.ItemId, (ei, i) => new { ei.SectionId, i.Modality, i.LifecycleStatus })
            .Where(x => x.Modality != SpeakingCode && x.LifecycleStatus == ItemSelector.ActiveLifecycle)
            .Select(x => x.SectionId)
            .ToListAsync(cancellationToken);

        var counts = rows
            .GroupBy(sectionId => sectionId)
            .ToDictionary(g => g.Key, g => g.Count());

        // shortgap: the legacy branch #269 deletes.
        var legacy = await DrillableLegacyQuestions(certId)
            .GroupBy(q => q.SectionId)
            .Select(g => new { SectionId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        foreach (var row in legacy)
        {
            counts.TryAdd(row.SectionId, row.Count);
        }

        return counts;
    }

    /// <summary>
    /// shortgap: the legacy drillability rule, kept only for a section the pool has not stocked. The
    /// speaking exclusion and the "answerable without a grader" rule are the same two facts the pool
    /// branch expresses; #269 deletes this method with the tables it reads.
    /// </summary>
    private IQueryable<Question> DrillableLegacyQuestions(int certId) =>
        db.Questions.AsNoTracking().Where(q => q.Section.CertId == certId
            && q.Section.Modality != SpeakingCode
            && (q.QuestionType == FreeResponseCode
                || (q.QuestionType != FreeResponseCode
                    && (q.AcceptedAnswersJson != null || db.QuestionOptions.Any(o => o.QuestionId == q.QuestionId)))));

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
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            // shortgap: a section the pool has not stocked still drills its legacy questions. #269
            // deletes this branch with the tables; a stocked section never reaches it.
            return await LoadLegacySectionCandidatesAsync(userId, certId, sectionId, cancellationToken);
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

    /// <summary>
    /// shortgap: the legacy half of <see cref="LoadSectionCandidatesAsync"/>, for a section the pool has
    /// not stocked. It reads the per-question history table this ticket stops WRITING but does not yet
    /// delete; #269 removes both branches together. The planner is given the legacy question id in the
    /// item slot, which is what the session builder then writes back as a legacy answer.
    /// </summary>
    private async Task<List<PracticeCandidate>> LoadLegacySectionCandidatesAsync(
        int userId, int certId, int sectionId, CancellationToken cancellationToken)
    {
        var candidates = await DrillableLegacyQuestions(certId)
            .Where(q => q.SectionId == sectionId)
            .Select(q => new
            {
                q.QuestionId,
                q.CategoryId,
                History = db.UserQuestionHistories
                    .Where(h => h.UserId == userId && h.QuestionId == q.QuestionId)
                    .Select(h => new { h.LastAnsweredAt, h.CooldownUntil })
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return candidates
            .Select(c => new PracticeCandidate(
                c.QuestionId,
                c.CategoryId,
                c.History?.LastAnsweredAt,
                c.History?.CooldownUntil))
            .ToList();
    }

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

        // #263: availability is pool stock, read from the items this certificate's papers bind into each
        // section - the same source the session builder draws from, so the count cannot promise a drill
        // the builder then refuses.
        var questionCounts = await CountDrillableBySectionAsync(activeCertId, cancellationToken);

        var accuracies = await db.vw_UserSectionAccuracyAlls
            .AsNoTracking()
            .Where(v => v.UserId == userId)
            .ToDictionaryAsync(v => v.SectionId, cancellationToken);

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

        // shortgap: a section the pool has not stocked reports its rest from the legacy category axis,
        // which is what the drill itself still draws for such a section. #269 deletes both this branch
        // and the legacy loader behind it. A stocked section never reaches it.
        var legacySectionCategories = await db.Questions
            .AsNoTracking()
            .Where(q => q.Section.CertId == activeCertId && q.CategoryId != null)
            .Select(q => new { q.SectionId, CategoryId = q.CategoryId!.Value })
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var group in legacySectionCategories.GroupBy(c => c.SectionId))
        {
            if (rests.ContainsKey(group.Key))
            {
                continue;
            }

            var due = group
                .Select(c => mastery.TryGetValue(c.CategoryId, out var m) ? m.NextReviewDueAt : null)
                .OfType<DateTimeOffset>()
                .Where(d => d > now)
                .ToList();
            rests[group.Key] = (group.Count(), due.Count, due.Count > 0 ? due.Min() : (DateTimeOffset?)null);
        }

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
                // #240: a lesson sitting resumes inside the reader, not from the practice catalog.
                && ps.LearnerPathStepId == null
                && ps.LessonId == null)
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
