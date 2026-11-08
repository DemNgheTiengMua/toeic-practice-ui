using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class PracticeService
{
    /// <summary>
    /// #235 / PLC-61: the <b>top three</b> coordinates the path currently prioritises, highlighted on the
    /// free-drill screen. Read from the learner's own open steps — the plan already says what today is
    /// for, so recomputing a priority here would be a second opinion that could disagree with it.
    /// <para>
    /// A learner with no active path has no priority list rather than an invented one: the screen says
    /// free drilling is unrestricted, which is true (PLC-61), instead of "no recommendations" implying a
    /// restriction.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<PrioritisedCoordinate>> GetPrioritisedCoordinatesAsync(
        int userId, CancellationToken cancellationToken = default)
    {
        var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);

        var pathId = await db.LearnerPaths
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.CertId == activeCertId && p.Status == "active")
            .Select(p => (int?)p.LearnerPathId)
            .FirstOrDefaultAsync(cancellationToken);

        if (pathId is null)
        {
            return [];
        }

        var open = await db.LearnerPathSteps
            .AsNoTracking()
            .Include(s => s.Skill)
            .Include(s => s.Topic)
            .Where(s => s.LearnerPathId == pathId
                && s.Status != LearnerPathStepStatus.Done.ToCode()
                && s.StepType != PathStepType.MockSection.ToCode())
            .OrderBy(s => s.WeekNumber ?? int.MaxValue)
            .ThenBy(s => s.WeekSequence)
            .ThenBy(s => s.StepId)
            .ToListAsync(cancellationToken);

        return open
            .Select(s => new
            {
                Step = s,
                Level = CefrLevel.TryParseAuthored(s.TargetCefr, out var level) ? level : CefrLevel.PreA1,
            })
            .GroupBy(x => (x.Step.SkillId, x.Step.TopicId, x.Level))
            .Select(g => g.First())
            .Take(3)
            .Select(x => new PrioritisedCoordinate(
                x.Step.SkillId,
                x.Step.Skill.Code,
                x.Step.Skill.Name,
                EnumCodes.Parse<Modality>(x.Step.Modality),
                x.Step.TopicId,
                x.Step.Topic.Code,
                x.Step.Topic.Name,
                x.Level,
                $"Open on your path in week {x.Step.WeekNumber ?? 0}"))
            .ToList();
    }

    /// <summary>
    /// #235 / PLC-61..64: whether a free drill may start, and if not, exactly why. Three independent
    /// locks, each with its own sentence:
    /// <list type="number">
    /// <item>the week's steps are not done — the lock <b>names</b> them and their remaining minutes
    /// (PLC-62: free drilling is a reward for following the plan, not a substitute for it);</item>
    /// <item>the day's ceiling is reached — 60 objective items, or 4 productive tasks, read from
    /// <c>vw_DailyPracticeVolume</c> rather than stored (D12);</item>
    /// <item>this cell already had its day — one session per cell per day (PLC-64).</item>
    /// </list>
    /// Every one of them offers a way on (review or lessons), because a lock with no exit is a dead end.
    /// <para>
    /// The three are computed <b>independently</b>: the ceiling still applies on a day whose steps are
    /// all done, and reporting only the first lock that fires would leave a learner to discover the
    /// second one after finishing it.
    /// </para>
    /// </summary>
    public async Task<FreeDrillState> GetFreeDrillStateAsync(
        int userId, int? topicId, CefrLevel? level, CancellationToken cancellationToken = default)
    {
        var priorities = await GetPrioritisedCoordinatesAsync(userId, cancellationToken);
        var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);

        var ceiling = await LoadDailyVolumeAsync(userId, cancellationToken);
        var ceilingReached = ceiling is { } v
            && (v.AnsweredCount >= FreeDrillState.DailyObjectiveCeiling
                || v.ProductiveTaskCount >= FreeDrillState.DailyProductiveCeiling);

        var cellUsedToday = topicId is { } topic
            && level is { } lvl
            && await CellPractisedTodayAsync(userId, topic, lvl, cancellationToken);

        var incomplete = await LoadIncompleteWeekStepsAsync(userId, activeCertId, cancellationToken);
        var remaining = incomplete.Sum(s => s.EstimatedMinutes);

        // The three are computed INDEPENDENTLY, and the notice reports EVERY lock that fires. Reporting
        // only the first would leave a learner to discover the second one after clearing it, which is
        // the "lock with no exit" shape this ticket refuses.
        var notices = new List<string>(3);

        if (incomplete.Count > 0)
        {
            notices.Add(
                $"Free drilling opens once this week's steps are done. {incomplete.Count} step(s) remain, about "
                + $"{remaining} minute(s): " + string.Join("; ", incomplete.Select(s => $"{s.Title} ({s.EstimatedMinutes} min)")));
        }

        if (ceilingReached)
        {
            notices.Add(
                "You have reached today's practice ceiling. Spacing the rest overnight is what makes it stick — "
                + "review a completed lesson or read an explanation instead, and drilling reopens tomorrow.");
        }

        if (cellUsedToday)
        {
            notices.Add(
                "This topic and level have already had their session today. One session per cell per day keeps a "
                + "cell from being exhausted in one afternoon — it reopens tomorrow, or drill a different cell now.");
        }

        var notice = notices.Count == 0 ? null : string.Join(" ", notices);

        return new FreeDrillState(
            IsUnlocked: notice is null,
            incomplete,
            remaining,
            priorities,
            cellUsedToday,
            ceilingReached,
            ceiling?.AnsweredCount ?? 0,
            FreeDrillState.DailyObjectiveCeiling,
            notice);
    }

    /// <summary>Today's derived volume, or null when nothing has been answered today. Read from the view
    /// so the number and the database cannot disagree (D12).</summary>
    private async Task<vw_DailyPracticeVolume?> LoadDailyVolumeAsync(int userId, CancellationToken cancellationToken)
    {
        var today = _clock.GetUtcNow().UtcDateTime.Date;
        return await db.vw_DailyPracticeVolumes
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.UserId == userId && v.PractisedOn == today, cancellationToken);
    }

    /// <summary>
    /// PLC-64's first ceiling: one session per <c>(topic, level)</c> cell per day, read from the
    /// answers rather than from a session row the learner may never have opened.
    /// <para>
    /// The answered set is loaded and tested in memory rather than as four SQL <c>OR</c>s: the same four
    /// columns are <c>AnswerContent.IsAnswered</c>'s, and a copied predicate here would be the second
    /// definition of "answered" this codebase refuses.
    /// </para>
    /// </summary>
    private async Task<bool> CellPractisedTodayAsync(
        int userId, int topicId, CefrLevel level, CancellationToken cancellationToken)
    {
        var today = _clock.GetUtcNow().UtcDateTime.Date;
        var tomorrow = today.AddDays(1);

        var answers = await db.PracticeAnswers
            .AsNoTracking()
            .Where(pa => pa.Session.UserId == userId
                && pa.Session.TopicId == topicId
                && pa.Session.CefrLevel == level
                && pa.AnsweredAt != null
                && pa.AnsweredAt >= today
                && pa.AnsweredAt < tomorrow)
            .Select(pa => new { pa.SelectedOptionId, pa.SelectedLabel, pa.ResponseText, pa.SelectedOptionIdsJson })
            .ToListAsync(cancellationToken);

        return answers.Any(a => AnswerContent.IsAnswered(
            a.SelectedOptionId, a.SelectedLabel, a.ResponseText, a.SelectedOptionIdsJson));
    }

    /// <summary>The week's unfinished steps, in plan order, with the minutes each still needs. The
    /// minutes are the step's own plan value — the same number the week's budget was spent on.</summary>
    private async Task<IReadOnlyList<IncompleteStep>> LoadIncompleteWeekStepsAsync(
        int userId, int certId, CancellationToken cancellationToken)
    {
        var path = await db.LearnerPaths
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.CertId == certId && p.Status == "active")
            .Select(p => new { p.LearnerPathId, p.ActiveWeek })
            .FirstOrDefaultAsync(cancellationToken);

        if (path is null)
        {
            return [];
        }

        return await db.LearnerPathSteps
            .AsNoTracking()
            .Include(s => s.Topic)
            .Where(s => s.LearnerPathId == path.LearnerPathId
                && s.WeekNumber == path.ActiveWeek
                && s.Status != LearnerPathStepStatus.Done.ToCode())
            .OrderBy(s => s.WeekSequence)
            .ThenBy(s => s.StepId)
            .Select(s => new IncompleteStep(
                s.StepId,
                s.WeekNumber ?? 0,
                s.Topic.Name,
                s.Topic.Code,
                s.EstimatedMinutes ?? 0))
            .ToListAsync(cancellationToken);
    }
}
