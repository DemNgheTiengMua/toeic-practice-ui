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

    // ---- #237 / SRS §7.5. The cell estimate's numbers, quoted with their requirement ids. ----

    /// <summary>PLC-67: <c>α0</c>, the pseudo-correct count of the prior.</summary>
    public const double PriorCorrect = 2.0;

    /// <summary>PLC-67: <c>β0</c>, the pseudo-wrong count. Prior probability is 2/(2+2) = 0.5.</summary>
    public const double PriorWrong = 2.0;

    /// <summary>The prior probability a cell sits at before any answer: <c>α0/(α0+β0)</c>.</summary>
    public const double PriorProbability = PriorCorrect / (PriorCorrect + PriorWrong);

    /// <summary>PLC-67: <c>w = 0.85^(K-i)</c>. The newest observation weighs 1, the previous 0.85.</summary>
    public const double RecencyWeight = 0.85;

    /// <summary>PLC-68: <c>p_low = 0.733</c>. Below it the cell displays as <c>low</c>.</summary>
    public const double LowThreshold = 0.733;

    /// <summary>PLC-68: <c>p_high = 0.80</c>. At or above it, and with the observation minimum, mastered.</summary>
    public const double MasteredThreshold = 0.80;

    /// <summary>PLC-68: <c>n_min</c> for objective skills, in items.</summary>
    public const int MinObservationsObjective = 12;

    /// <summary>PLC-68: <c>n_min</c> for productive skills, in tasks.</summary>
    public const int MinObservationsProductive = 3;

    /// <summary>PLC-71: a mastered cell is not touched before this many days of inactivity.</summary>
    public const int InactivityOnsetDays = 28;

    /// <summary>PLC-71: the drift per week once the onset has passed.</summary>
    public const double WeeklyDecay = 0.02;

    /// <summary>PLC-70: a single wrong answer while mastered sets a review window this long.</summary>
    public static readonly TimeSpan ReviewWindow = TimeSpan.FromHours(24);
}
