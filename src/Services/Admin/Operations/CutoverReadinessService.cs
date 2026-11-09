using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #247 / #248: the automated check both tickets demand - "no attempt is mid-sitting on a legacy exam,
/// and the check that enforces it is automated".
/// <para>
/// <b>What "legacy exam" means here, and why it is not a column.</b> There is no flag marking an exam as
/// old, because an exam is old precisely when the pool has no roster for it: its content lives in
/// <c>Questions</c>, which #250 deletes. So the condition is structural - an exam with no
/// <c>ExamItems</c> rows is one whose sitting cannot survive the cutover, and an attempt still open
/// against it is a learner who would lose their paper mid-sitting.
/// </para>
/// <para>
/// <b>Why an open attempt is the blocker and a finished one is not.</b> A completed attempt is history:
/// #246 preserves it (tagged with its epoch) and it renders read-only afterwards. An attempt that is
/// still <c>in_progress</c> is a sitting somebody is expected to resume, and the pool cannot serve the
/// paper it was sitting. Blocking on it forces the cutover to wait for it to end, or to abandon it
/// deliberately - both are decisions a person makes, which is why this reports rather than fixes.
/// </para>
/// </summary>
public sealed class CutoverReadinessService(CompanionDbContext db) : ICutoverReadinessService
{
    /// <summary>An open sitting whose exam the pool has no roster for.</summary>
    public const string MidSittingOnLegacyExam = "mid_sitting_on_legacy_exam";

    /// <summary>An open sitting whose placement or mock the pool cannot rebuild from <c>ExamItems</c>.</summary>
    public const string LegacyExamsCarryingNoRoster = "legacy_exams_carrying_no_roster";

    public async Task<CutoverReadinessDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var blockers = new List<CutoverBlockerDto>();

        // The open attempts that would lose their paper. Counted, not listed: a cutover run wants to
        // know the size of the problem, and the rows themselves are one query away for whoever fixes it.
        var midSitting = await db.ExamAttempts
            .AsNoTracking()
            .Where(a => a.Status == "in_progress")
            .Where(a => !db.ExamItems.Any(i => i.ExamId == a.ExamId))
            .CountAsync(cancellationToken);

        if (midSitting > 0)
        {
            blockers.Add(new CutoverBlockerDto(
                MidSittingOnLegacyExam,
                $"{midSitting} attempt(s) are still in progress on an exam the pool has no roster for. "
                + "Finish or abandon them before the cutover: the pool cannot serve a paper it cannot "
                + "rebuild from ExamItems, so the learner would resume into nothing.",
                midSitting));
        }

        // Reported separately because it is a content fact rather than a learner one, and because a
        // database can be ready to cut over while still carrying exams nobody has migrated yet - those
        // must be retired, not sat.
        var rostersMissing = await db.Exams
            .AsNoTracking()
            .Where(e => !db.ExamItems.Any(i => i.ExamId == e.ExamId))
            .CountAsync(cancellationToken);

        if (rostersMissing > 0)
        {
            blockers.Add(new CutoverBlockerDto(
                LegacyExamsCarryingNoRoster,
                $"{rostersMissing} exam(s) carry no ExamItems roster, so their content lives only in the "
                + "legacy Questions tables. They must be republished onto the pool or retired before the "
                + "legacy tables are dropped, or the content goes with them.",
                rostersMissing));
        }

        return new CutoverReadinessDto(blockers);
    }
}
