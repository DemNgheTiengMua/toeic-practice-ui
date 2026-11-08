namespace BusinessObjects;

/// <summary>
/// #238: the four-state estimate of one <c>(topic, level)</c> cell, from the answers given in it.
/// <para>
/// <b>One definition.</b> The path already sizes its steps from a cell's mastery
/// (<c>LearningPathService</c>) and the practice hub colours the same cell from it. Two copies of the
/// thresholds would let the plan and the matrix disagree about the same cell on the same day, so both
/// call this. When #237 replaces the estimator, it changes this function and nothing else has to move.
/// </para>
/// <para>
/// It reads only <c>MasterySchedule</c>'s existing constants. It writes nothing (ADR-0012): the caller
/// renders a state, it does not store one.
/// </para>
/// </summary>
public static class CellMastery
{
    public static MasteryState Estimate(int answered, int correct)
    {
        if (answered <= 0)
        {
            return MasteryState.Untested;
        }

        var accuracy = (int)Math.Round((double)correct / answered * 100);

        return accuracy >= MasterySchedule.MinAccuracyToMastered
               && answered >= MasterySchedule.MinAnswersToMastered
            ? MasteryState.Mastered
            : accuracy >= MasterySchedule.MinAccuracyToReviewing
              && answered >= MasterySchedule.MinAnswersToReviewing
                ? MasteryState.Reviewing
                : MasteryState.Learning;
    }
}
