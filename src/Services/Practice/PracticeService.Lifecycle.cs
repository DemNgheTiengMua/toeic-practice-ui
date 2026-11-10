using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class PracticeService
{
    public async Task<PracticeSessionInfo> StartSessionAsync(int userId, StartPracticeSessionInput input, CancellationToken cancellationToken = default)
    {
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);

        var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);

        // #235 / PLC-62..64: these are the free drill's locks. A *path* step does not come through
        // here — LearningPathService starts its own session — so a step is never blocked by the plan
        // that scheduled it (PLC-60).
        await EnforceFreeDrillLocksAsync(userId, input, cancellationToken);

        return input.TopicId is { } topicId
            ? await StartPoolSessionAsync(userId, activeCertId, topicId, input.CefrLevel!.Value, input.ContentTier, cancellationToken)
            : await StartSectionSessionAsync(userId, activeCertId, input.SectionId!.Value, cancellationToken);
    }

    /// <summary>
    /// #235 / PLC-62 and PLC-64: the three things that stop a free drill, each with its own sentence and
    /// each naming the way on. Throwing here means no session row is written, so a refused drill does
    /// not count against the learner's day.
    /// </summary>
    private async Task EnforceFreeDrillLocksAsync(
        int userId, StartPracticeSessionInput input, CancellationToken cancellationToken)
    {
        var state = await GetFreeDrillStateAsync(userId, input.TopicId, input.CefrLevel, cancellationToken);
        if (!state.IsUnlocked)
        {
            throw new PracticeException(PracticeException.FreeDrillLocked, state.LockNotice!);
        }
    }

    /// <summary>
    /// #187: a drill against a cell. There is no exam and no section behind it: the items come from
    /// the pool at (topic, level) and the session records which version of each item was answered.
    /// </summary>
    private async Task<PracticeSessionInfo> StartPoolSessionAsync(
        int userId, int certId, int topicId, CefrLevel level, string contentTier, CancellationToken cancellationToken)
    {
        var topic = await db.Topics
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.TopicId == topicId, cancellationToken)
            ?? throw new PracticeException(PracticeException.SectionUnavailable, $"Topic {topicId} does not exist.");

        // #150's rule reaches the pool too: speaking is answered by voice, and its recorded, AI-graded
        // practice is a different attempt. Say where it lives rather than serving a textarea.
        if (string.Equals(topic.Modality, Modality.Speaking.ToCode(), StringComparison.Ordinal))
        {
            throw new PracticeException(
                PracticeException.ModalityNotAllowed,
                "Speaking is answered by voice, so it has no typed drill: open the recorded speaking practice, which is graded by AI.");
        }

        // #235 / PLC-62..64: a free drill is a reward for following the plan, and it stops at the day's
        // ceiling. Path steps do not pass through here, so a step is never blocked by any of this.
        var selected = (await PlanPoolItemsAsync(userId, certId, topic.Modality, topicId, level, contentTier, cancellationToken)).Items;

        if (selected.Count == 0)
        {
            // #190: name the tier. "No active items at R-COH B2" is now an incomplete sentence: the
            // cell may be full of the other tier, and a message that omitted it would send an author
            // looking for a problem in the wrong one.
            throw new PracticeException(
                PracticeException.NoQuestionsAvailable,
                $"No {contentTier} items are active at {topic.Code} {level.Code}.");
        }

        await AbandonInProgressAsync(userId, certId, cancellationToken);

        var session = new PracticeSession
        {
            UserId = userId,
            CertId = certId,
            SectionId = null,
            TopicId = topicId,
            CefrLevel = level,
            Modality = topic.Modality,
            QuestionCount = selected.Count,
            Status = PracticeStatus.InProgress.ToCode(),
            CorrectCount = 0,
            QuestionSource = QuestionSource.All.ToCode(),
            StartedAt = _clock.GetUtcNow(),
        };

        db.PracticeSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);

        for (var i = 0; i < selected.Count; i++)
        {
            db.PracticeAnswers.Add(new PracticeAnswer
            {
                SessionId = session.SessionId,
                QuestionId = null,
                SectionId = null,
                ItemId = selected[i].ItemId,
                ItemVersionId = selected[i].ItemVersionId,
                SelectedLabel = null,
                SelectedOptionId = null,
                IsCorrect = null,
                HintUsed = false,
                DisplayOrder = i + 1,
            });
        }
        await db.SaveChangesAsync(cancellationToken);

        await PruneOldSessionsAsync(userId, certId, cancellationToken);

        return ToSessionInfo(session, topic.Code, topic.Name);
    }

    private async Task<PracticeSessionInfo> StartSectionSessionAsync(
        int userId, int certId, int sectionId, CancellationToken cancellationToken)
    {
        var section = await db.Sections
            .AsNoTracking()
            .Where(s => s.SectionId == sectionId && s.CertId == certId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new PracticeException(PracticeException.SectionUnavailable, $"Section {sectionId} is not available for active certificate.");

        // #150: a spoken answer cannot be typed, and its recorded, AI-graded practice is a different
        // attempt (#151), so speaking is the one modality the free drill does not serve. Say where the
        // recorded practice lives rather than serving a textarea, and keep the refusal here and in
        // DrillableQuestions so the catalog never advertises a drill this method will not start.
        if (string.Equals(section.Modality, Modality.Speaking.ToCode(), StringComparison.Ordinal))
        {
            throw new PracticeException(
                PracticeException.ModalityNotAllowed,
                "Speaking is answered by voice, so it has no typed drill: open the recorded speaking practice, which is graded by AI.");
        }

        // #263: the section draws from ONE shape, and the planner says which. Deciding per id by
        // "does a pool item happen to have this id" is wrong: item ids and question ids are two
        // counters that collide, so a legacy question id can name an unrelated item and the answer
        // would be written as a pool row for content nobody drew.
        var plan = await PlanSmartPracticeAsync(userId, certId, section.SectionId, cancellationToken);
        var selectedIds = plan.Ids;
        var isPool = plan.IsPool;

        if (selectedIds.Count == 0)
        {
            throw new PracticeException(PracticeException.NoQuestionsAvailable, "No questions match the selected criteria for this section.");
        }

        var versionByItem = new Dictionary<int, int>();
        if (isPool)
        {
            var drawnItems = await db.Items
                .AsNoTracking()
                .Where(i => selectedIds.Contains(i.ItemId))
                .Select(i => new
                {
                    i.ItemId,
                    VersionId = i.ItemVersions
                        .OrderByDescending(v => v.VersionNumber)
                        .Select(v => (int?)v.ItemVersionId)
                        .FirstOrDefault(),
                })
                .ToListAsync(cancellationToken);
            versionByItem = drawnItems.ToDictionary(d => d.ItemId, d => d.VersionId!.Value);
        }

        await AbandonInProgressAsync(userId, certId, cancellationToken);

        var session = new PracticeSession
        {
            UserId = userId,
            CertId = certId,
            SectionId = section.SectionId,
            Modality = section.Modality,
            QuestionCount = selectedIds.Count,
            Status = PracticeStatus.InProgress.ToCode(),
            CorrectCount = 0,
            QuestionSource = QuestionSource.All.ToCode(),
            StartedAt = _clock.GetUtcNow(),
        };

        db.PracticeSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);

        for (var i = 0; i < selectedIds.Count; i++)
        {
            db.PracticeAnswers.Add(new PracticeAnswer
            {
                SessionId = session.SessionId,
                // #263: a pool item names itself and the version it pinned; a legacy question names
                // itself and the section. CK_PracticeAnswers_Shape forbids the third mixture.
                QuestionId = isPool ? null : selectedIds[i],
                SectionId = isPool ? null : section.SectionId,
                ItemId = isPool ? selectedIds[i] : null,
                ItemVersionId = isPool ? versionByItem[selectedIds[i]] : null,
                SelectedLabel = null,
                SelectedOptionId = null,
                IsCorrect = null,
                HintUsed = false,
                DisplayOrder = i + 1,
            });
        }
        await db.SaveChangesAsync(cancellationToken);

        await PruneOldSessionsAsync(userId, certId, cancellationToken);

        return ToSessionInfo(session, section.Code, section.Name);
    }

    /// <summary>
    /// Clean up or abandon any existing standalone in-progress sessions for this user and certificate.
    /// Shared by both shapes: starting a second drill is one event whether the first was a section or
    /// a cell, so the two must not implement it differently.
    /// </summary>
    private async Task AbandonInProgressAsync(int userId, int certId, CancellationToken cancellationToken)
    {
        var existingInProgress = await db.PracticeSessions
            .Where(s => s.UserId == userId && s.CertId == certId && s.Status == PracticeStatus.InProgress.ToCode() && s.LearnerPathStepId == null
                // #240: a lesson sitting is its own open item, with its own resume position. Starting a
                // drill must not quietly abandon the lesson the learner is halfway through.
                && s.LessonId == null)
            .ToListAsync(cancellationToken);

        foreach (var inProg in existingInProgress)
        {
            // #253: "has content", not "has a grade" - a pool productive answer is ungraded forever, so
            // this check used to delete a session the learner had already written into.
            var answers = await db.PracticeAnswers
                .AsNoTracking()
                .Where(pa => pa.SessionId == inProg.SessionId)
                .ToListAsync(cancellationToken);
            var hasAnswers = answers.Any(IsAnswerRecorded);

            if (!hasAnswers)
            {
                var emptyAnswers = await db.PracticeAnswers
                    .Where(pa => pa.SessionId == inProg.SessionId)
                    .ToListAsync(cancellationToken);
                db.PracticeAnswers.RemoveRange(emptyAnswers);
                db.PracticeSessions.Remove(inProg);
            }
            else
            {
                inProg.Status = PracticeStatus.Abandoned.ToCode();
                inProg.FinishedAt = DateTimeOffset.UtcNow;
            }
        }
    }

    /// <summary>
    /// #155: the questions a session contains, decided by mastery state and the cooldown - never by a
    /// learner-picked source. The mix itself is <see cref="SmartPracticePlanner"/>, a pure function; this
    /// only loads what it needs.
    /// </summary>
    private async Task<(IReadOnlyList<int> Ids, bool IsPool)> PlanSmartPracticeAsync(
        int userId, int certId, int sectionId, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();

        // #263: a section's candidates come from the items its paper binds into it. The cooldown is
        // derived from this learner's own answers to those items, because #237 made the CELL the unit of
        // mastery and the selector already refuses the per-question history table as a source.
        var poolCandidates = await LoadSectionCandidatesAsync(userId, certId, sectionId, cancellationToken);
        var isPool = poolCandidates.Count > 0;
        var candidates = isPool
            ? poolCandidates
            // shortcut: a section the pool has not stocked still drills its legacy questions. #269
            // deletes this branch; a stocked section never reaches it.
            : await LoadLegacySectionCandidatesAsync(userId, certId, sectionId, cancellationToken);

        var plan = candidates
            .Select(c => new PracticeCandidate(
                c.QuestionId,
                c.CategoryId,
                c.LastAnsweredAt,
                c.CooldownUntil))
            .ToList();

        var mastery = await LoadMasteryAsync(userId, cancellationToken);
        return (SmartPracticePlanner.Plan(plan, mastery, now), isPool);
    }

    public async Task<PracticeSessionInfo> GetSessionInfoAsync(int userId, int sessionId, CancellationToken cancellationToken = default)
    {
        var session = await db.PracticeSessions
            .AsNoTracking()
            .Include(s => s.Section)
            .Include(s => s.Topic)
            .Where(s => s.SessionId == sessionId && s.UserId == userId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new PracticeException(PracticeException.SessionNotFound, $"Practice session {sessionId} was not found.");

        return ToSessionInfo(session);
    }

    private async Task PruneOldSessionsAsync(int userId, int certId, CancellationToken cancellationToken)
    {
        var sessions = await db.PracticeSessions
            .Where(s => s.UserId == userId && s.CertId == certId
                // #240: a lesson sitting is not one of the standalone drills this retention bound
                // governs. Pruning it would delete the evidence a micro-check wrote.
                && s.LearnerPathStepId == null
                && s.LessonId == null)
            .OrderByDescending(s => s.StartedAt)
            .ThenByDescending(s => s.SessionId)
            .Select(s => s.SessionId)
            .ToListAsync(cancellationToken);

        if (sessions.Count <= MaxSavedSessionsPerCertificate)
        {
            return;
        }

        var excessSessionIds = sessions.Skip(MaxSavedSessionsPerCertificate).ToList();
        if (excessSessionIds.Count == 0)
        {
            return;
        }

        var insightIds = await db.AiInsights
            .Where(ai => ai.PracticeSessionId.HasValue && excessSessionIds.Contains(ai.PracticeSessionId.Value))
            .Select(ai => ai.InsightId)
            .ToListAsync(cancellationToken);

        var recos = await db.Recommendations
            .Where(r => (r.PracticeSessionId.HasValue && excessSessionIds.Contains(r.PracticeSessionId.Value))
                     || (r.InsightId.HasValue && insightIds.Contains(r.InsightId.Value)))
            .ToListAsync(cancellationToken);
        if (recos.Count > 0)
        {
            db.Recommendations.RemoveRange(recos);
        }

        var insights = await db.AiInsights
            .Where(ai => ai.PracticeSessionId.HasValue && excessSessionIds.Contains(ai.PracticeSessionId.Value))
            .ToListAsync(cancellationToken);
        if (insights.Count > 0)
        {
            db.AiInsights.RemoveRange(insights);
        }

        var answers = await db.PracticeAnswers
            .Where(pa => excessSessionIds.Contains(pa.SessionId))
            .ToListAsync(cancellationToken);
        if (answers.Count > 0)
        {
            db.PracticeAnswers.RemoveRange(answers);
        }

        var sessionsToRemove = await db.PracticeSessions
            .Where(s => excessSessionIds.Contains(s.SessionId))
            .ToListAsync(cancellationToken);
        if (sessionsToRemove.Count > 0)
        {
            db.PracticeSessions.RemoveRange(sessionsToRemove);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
