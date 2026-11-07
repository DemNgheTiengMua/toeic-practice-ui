using BusinessObjects;

namespace Services;

/// <summary>
/// #222 / OQ-TR-01 / D-26: a mock exam keeps its own certificate score and gains a separately labelled
/// projected level beside it.
/// <para>
/// <b>The read cannot write.</b> <see cref="GetResultAsync"/> returns a
/// <see cref="MockResultProjection"/> and nothing on that path calls the skill-state writer: one rushed
/// section must never overwrite a four-week plan. The sync is a second, explicit method whose caller is
/// the learner's click, and it refuses everything the read refused - a historical attempt with no
/// cut-score version, a score that fell between two rows, and a skill whose level already matches.
/// </para>
/// </summary>
public interface IMockResultSyncService
{
    /// <summary>
    /// The mock result: certificate score, band, and the per-skill projection read against the
    /// certificate's cut-score table. Pure read - no row is written.
    /// </summary>
    Task<MockResultProjection> GetResultAsync(
        int userId, int attemptId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes every projected skill that differs from the learner's stored state, through
    /// <see cref="ISkillStateWriter"/> with <c>mock_section</c> as the source, and returns the standing
    /// afterwards. Refuses when there is nothing to write, so the button cannot write an invention.
    /// </summary>
    Task<LearnerStanding> SyncAsync(
        int userId, int attemptId, CancellationToken cancellationToken = default);
}
