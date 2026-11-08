namespace BusinessObjects;

/// <summary>#159: the learner's dashboard is exactly three blocks - the next action, where they stand
/// against their goal, and one card per skill of the certificate. Eleven blocks of guidance became
/// three because the learner needs one thing to do, not a page of commentary.
/// <para>
/// Every list here belongs to <see cref="Certificate"/> only: TOEIC history never shows while IELTS
/// is active. <see cref="Skills"/> is the certificate's own skill list, so it is never a fixed pair.
/// </para></summary>
public sealed record LearnerDashboard(
    CertificateOption Certificate,
    NextAction NextAction,
    GoalStanding Goal,
    IReadOnlyList<SkillCard> Skills,
    int AttemptCount,
    int PracticeCount,
    RecentAttempt? UnfinishedAttempt = null,
    RecentPractice? UnfinishedPractice = null,
    StandingSummary? Standing = null)
{
    /// <summary>No attempt and no practice yet for this certificate: the hero says "start here"
    /// rather than inventing a weakness the learner has no history for.</summary>
    public bool IsNew => AttemptCount == 0 && PracticeCount == 0;

    /// <summary>#241 / LRN-13: nothing has been measured, so the page owes the learner a placement and
    /// a sentence, not a grid of empty figures.</summary>
    public bool IsUnplaced => Standing is null || Standing.HasNoMeasurement;
}
