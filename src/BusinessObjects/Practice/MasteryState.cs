namespace BusinessObjects;

/// <summary>#155: the four states a learner's mastery of one <c>QuestionCategory</c> moves through.
/// The unit is the category, never a single question: spaced repetition over one question teaches that
/// question's answer, not the grammar behind it.</summary>
public enum MasteryState
{
    /// <summary>No answer has ever been recorded for this category. Stored as the absence of a row,
    /// so 'untested' is what a learner starts in, not something any answer moves them into.</summary>
    Untested,

    /// <summary>Answered, but not well enough to space out: accuracy &lt; 75%, or fewer than 5 answers,
    /// or the last answers were wrong. Reviewed again after 24 hours.</summary>
    Learning,

    /// <summary>At least 75% over at least 5 answers, or a run of 3 correct. Reviewed after 3 days,
    /// then 7. A lapse from <see cref="Mastered"/> lands here.</summary>
    Reviewing,

    /// <summary>At least 85% over at least 10 answers, across at least 2 days in session. Reviewed
    /// after 14 days. Two wrong answers in a row while here is a lapse.</summary>
    Mastered,
}

/// <summary>#155: the spacing each state reviews at, and the review that follows a lapse.</summary>
public static class MasterySchedule
{
    public const int CorrectStreakToReviewing = 3;
    public const int MinAnswersToReviewing = 5;
    public const int MinAccuracyToReviewing = 75;
    public const int MinAnswersToMastered = 10;
    public const int MinAccuracyToMastered = 85;
    public const int MinSessionDaysToMastered = 2;
    public const int WrongInARowToLapse = 2;

    public static readonly TimeSpan LearningInterval = TimeSpan.FromHours(24);
    public static readonly TimeSpan ReviewingInterval = TimeSpan.FromDays(3);
    public static readonly TimeSpan ReviewingSecondInterval = TimeSpan.FromDays(7);
    public static readonly TimeSpan MasteredInterval = TimeSpan.FromDays(14);
    public static readonly TimeSpan LapseInterval = TimeSpan.FromHours(24);
    public static readonly TimeSpan WrongAnswerCooldown = TimeSpan.FromHours(24);
}
