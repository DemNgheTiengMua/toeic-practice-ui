namespace BusinessObjects;

/// <summary>
/// #155: the four-state mastery machine, as one pure function. An answer goes in, the new state comes
/// out - no database, no clock of its own, so every rule can be tested on its own and the caller cannot
/// quietly invent a fifth state.
///
/// The transition table this implements:
///
/// | from      | event                                                       | to        | next review |
/// |-----------|-------------------------------------------------------------|-----------|-------------|
/// | Untested  | no answers yet - the absence of a row                       | -         | always due  |
/// | Untested  | first answer, right or wrong                                | Learning  | +24h        |
/// | Learning  | answer: accuracy &lt; 75% or &lt; 5 answers or a wrong answer  | Learning  | +24h        |
/// | Learning  | accuracy &gt;= 75% over &gt;= 5 answers, or streak &gt;= 3        | Reviewing | +3d         |
/// | Reviewing | answer, still short of the Mastered proof                   | Reviewing | +7d         |
/// | Reviewing | 85% over &gt;= 10 answers across &gt;= 2 days in session        | Mastered  | +14d        |
/// | Mastered  | answer, no lapse                                            | Mastered  | +14d        |
/// | Mastered  | second wrong answer in a row                                | Reviewing | +24h        |
///
/// Mastered is earned and then held: it is not re-derived from the running percentage on every answer,
/// because then a single miss would drop a mastered learner to Reviewing and the ticket's "two wrong in
/// a row is a lapse" would never be the thing that fires. Only the lapse takes it away.
/// </summary>
public static class MasteryMachine
{
    public readonly record struct Snapshot(
        MasteryState State,
        int AnsweredCount,
        int CorrectCount,
        int Streak,
        int ConsecutiveWrong,
        int SessionDays,
        DateTimeOffset? NextReviewDueAt,
        DateTimeOffset? MasteredAt);

    public readonly record struct Step(Snapshot Next, DateTimeOffset NextReviewDueAt);

    /// <summary>Applies one graded answer. <paramref name="firstAnswerOfDay"/> is the caller's answer to
    /// "is this the first answer for this category today", which is what makes "&gt;= 2 days in session"
    /// countable; a skipped question writes nothing and so never reaches here.</summary>
    public static Step Apply(Snapshot current, bool isCorrect, DateTimeOffset at, bool firstAnswerOfDay)
    {
        var answered = current.AnsweredCount + 1;
        var correct = current.CorrectCount + (isCorrect ? 1 : 0);
        var streak = isCorrect ? current.Streak + 1 : 0;
        var consecutiveWrong = isCorrect ? 0 : current.ConsecutiveWrong + 1;
        var sessionDays = current.SessionDays + (firstAnswerOfDay ? 1 : 0);

        var lapsed = current.State == MasteryState.Mastered
            && consecutiveWrong >= MasterySchedule.WrongInARowToLapse;

        var (state, due) = current.State switch
        {
            // Earned state. One miss is not enough to lose it, but it does pull the next review back to
            // tomorrow: otherwise the category would not come round again for a fortnight and the
            // "two in a row" lapse could never actually be reached.
            MasteryState.Mastered when !lapsed && !isCorrect =>
                (MasteryState.Mastered, at + MasterySchedule.LapseInterval),

            MasteryState.Mastered when !lapsed => (MasteryState.Mastered, at + MasterySchedule.MasteredInterval),

            // A lapse is the one transition out of Mastered, and it lands on the daily review.
            MasteryState.Mastered => (MasteryState.Reviewing, at + MasterySchedule.LapseInterval),

            // A wrong answer is never evidence for a longer interval.
            _ when !isCorrect => (MasteryState.Learning, at + MasterySchedule.LearningInterval),

            _ when MasteryMachine.MeetsMastered(correct, answered, sessionDays) =>
                (MasteryState.Mastered, at + MasterySchedule.MasteredInterval),

            _ when MasteryMachine.MeetsReviewing(streak, correct, answered) =>
                (MasteryState.Reviewing, at + (current.State == MasteryState.Reviewing
                    && current.NextReviewDueAt is { } previousDue && at >= previousDue
                        ? MasterySchedule.ReviewingSecondInterval
                        : MasterySchedule.ReviewingInterval)),

            _ => (MasteryState.Learning, at + MasterySchedule.LearningInterval),
        };

        var masteredAt = current.MasteredAt ?? (state == MasteryState.Mastered ? at : null);
        return new Step(
            new Snapshot(state, answered, correct, streak, consecutiveWrong, sessionDays, due, masteredAt),
            due);
    }

    /// <summary>Accuracy &gt;= 85% over &gt;= 10 answers, across &gt;= 2 days in session.</summary>
    public static bool MeetsMastered(int correct, int answered, int sessionDays) =>
        answered >= MasterySchedule.MinAnswersToMastered
        && sessionDays >= MasterySchedule.MinSessionDaysToMastered
        && correct * 100 >= answered * MasterySchedule.MinAccuracyToMastered;

    /// <summary>Accuracy &gt;= 75% over &gt;= 5 answers, or a run of 3 correct.</summary>
    public static bool MeetsReviewing(int streak, int correct, int answered) =>
        streak >= MasterySchedule.CorrectStreakToReviewing
        || (answered >= MasterySchedule.MinAnswersToReviewing
            && correct * 100 >= MasterySchedule.MinAccuracyToReviewing);

    /// <summary>What the catalog and the picker both read: an untested category is always due, and any
    /// other is due once its review date has arrived.</summary>
    public static bool IsDue(Snapshot current, DateTimeOffset at) =>
        current.State == MasteryState.Untested
        || current.NextReviewDueAt is null
        || current.NextReviewDueAt <= at;
}
