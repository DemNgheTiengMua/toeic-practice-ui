using System;

namespace BusinessObjects;

/// <summary>
/// #237 / PLC-66/68/70/71: the four-state mastery machine <b>for one cell</b> - a (topic, level) pair,
/// not an item and not a category.
/// <para>
/// <b>It reuses <see cref="MasteryState"/>.</b> The four values are the same four the category machine
/// uses, and the SRS's own vocabulary maps onto them one-to-one: <c>unmeasured</c> is
/// <see cref="MasteryState.Untested"/>, <c>low</c> is <see cref="MasteryState.Learning"/>,
/// <c>developing</c> is <see cref="MasteryState.Reviewing"/>, and <c>mastered</c> is
/// <see cref="MasteryState.Mastered"/>. PLC-70 itself names the lapsed state <c>reviewing</c>. A second
/// four-state enum beside this one would be two answers to one question, so there is not one - the
/// display vocabulary belongs in the display registry (D19), not in a parallel type.
/// </para>
/// <para>
/// <b>State changes only on an event, never by recomputation</b> (PLC-70). The named events are
/// <c>answer_correct</c>, <c>answer_wrong</c>, <c>maintenance_pass</c>, <c>inactivity_tick</c> and
/// <c>mock_section</c>. There is deliberately no "recompute" method: a design that re-derived the state
/// from the running estimate on every read would make "two consecutive wrong answers" unreachable,
/// because the first miss alone would drop the cell.
/// </para>
/// <para>
/// What the events share is the <b>estimate</b> (<see cref="MasteryEvidence"/>), which is the
/// recency-weighted Beta-Binomial of PLC-67 and is a pure function of the answers. The state is a
/// function of the events, not of the estimate alone: that separation is the whole ticket.
/// </para>
/// </summary>
public static class MasteryCellMachine
{
    /// <summary>
    /// Everything the machine needs about one cell. The two weighted sums are PLC-67's sufficient
    /// statistic; <see cref="ObservationCount"/> is the unweighted count PLC-68's minimum reads;
    /// <see cref="ConsecutiveWrong"/> is the lapse counter; <see cref="LastPractisedAt"/> is the datum
    /// the inactivity clock reads.
    /// </summary>
    public readonly record struct Snapshot(
        MasteryState State,
        MasteryEvidence Evidence,
        int ConsecutiveWrong,
        DateTimeOffset? LastPractisedAt,
        DateTimeOffset? MasteredAt,
        DateTimeOffset? NextReviewDueAt)
    {
        /// <summary>A cell with no answers. <c>Untested</c> is the absence of evidence, and the
        /// estimate is the prior (<c>p_m = 0.5</c>), not zero.</summary>
        public static Snapshot Unmeasured => new(
            MasteryState.Untested, MasteryEvidence.None, 0, null, null, null);

        public double Probability => Evidence.Probability;
    }

    /// <summary>The outcome of one event: the new snapshot and, when the event moved the state, the
    /// transition it made - so a caller can log "learning -> reviewing" without re-deriving it.</summary>
    public readonly record struct Step(
        Snapshot Next,
        MasteryState From,
        MasteryState To,
        DateTimeOffset? NextReviewDueAt)
    {
        public bool MovedState => From != To;
    }

    /// <summary>
    /// Records one graded answer. <paramref name="minimumObservations"/> is PLC-68's <c>n_min</c> for
    /// the cell's modality (12 objective items, 3 productive tasks) - carried in rather than looked up,
    /// because this type is database-free.
    /// <para>
    /// <b>The rule order is the ticket's.</b> While <c>mastered</c>, a wrong answer is <i>not</i> allowed
    /// to consult the estimate: a single miss sets the review window (PLC-70), and only the second in a
    /// row lapses. Every other state is reached from the estimate plus the count.
    /// </para>
    /// </summary>
    public static Step RecordAnswer(
        Snapshot current,
        bool isCorrect,
        DateTimeOffset at,
        int minimumObservations)
    {
        var evidence = current.Evidence.Observe(isCorrect);
        var consecutiveWrong = isCorrect ? 0 : current.ConsecutiveWrong + 1;
        var p = evidence.Probability;

        // ---- mastered: the two-answer rule, never the estimate ----
        if (current.State == MasteryState.Mastered)
        {
            if (!isCorrect)
            {
                if (consecutiveWrong >= MasterySchedule.WrongInARowToLapse)
                {
                    // The lapse. `reviewing`, not `learning`: a lapsed cell has evidence and a
                    // position, so it is developing rather than low. MasteredAt is kept: earned once is
                    // a fact about the learner, and a lapse does not rewrite it.
                    return Move(current, evidence, consecutiveWrong, MasteryState.Reviewing,
                        at + MasterySchedule.LapseInterval, at);
                }

                // One miss: still mastered, and the review window pulls the cell round tomorrow so the
                // second wrong answer can actually arrive.
                return Move(current, evidence, consecutiveWrong, MasteryState.Mastered,
                    at + MasterySchedule.ReviewWindow, at);
            }

            return Move(current, evidence, consecutiveWrong, MasteryState.Mastered,
                at + MasterySchedule.MasteredInterval, at);
        }

        // ---- not mastered: the state follows the estimate and the count ----
        var next = EnterFromEstimate(current.State, p, evidence.ObservationCount, minimumObservations);
        var due = next switch
        {
            MasteryState.Mastered => at + MasterySchedule.MasteredInterval,
            MasteryState.Reviewing => at + MasterySchedule.ReviewingInterval,
            _ => at + MasterySchedule.LearningInterval,
        };

        // A wrong answer is never evidence for a longer interval, whatever the state it leaves.
        if (!isCorrect && next != MasteryState.Mastered)
        {
            due = at + MasterySchedule.LearningInterval;
        }

        return Move(current, evidence, consecutiveWrong, next, due, at);
    }

    /// <summary>
    /// PLC-68's decision for a cell that is not currently mastered. Both halves are required for
    /// <c>mastered</c>: the probability <b>and</b> the observation minimum. The minimum is what stops
    /// four correct answers (p = 0.75, above the lower cut) from pronouncing a topic learned.
    /// </summary>
    public static MasteryState EnterFromEstimate(
        MasteryState from, double probability, int observationCount, int minimumObservations)
    {
        // Nothing returns to `unmeasured`: evidence cannot be unobserved. A row that has answers has a
        // state above the floor even when its estimate has collapsed, which PLC-71's reversion relies
        // on (`reviewing`, never `untested`).
        if (from == MasteryState.Untested && observationCount == 0)
        {
            return MasteryState.Untested;
        }

        if (probability >= MasterySchedule.MasteredThreshold
            && observationCount >= minimumObservations)
        {
            return MasteryState.Mastered;
        }

        // The mastered state may only be entered through the gate above. A cell already mastered that
        // is being re-evaluated (a maintenance pass that failed) falls to reviewing, never further.
        if (from == MasteryState.Mastered)
        {
            return MasteryState.Reviewing;
        }

        return probability >= MasterySchedule.LowThreshold
            ? MasteryState.Reviewing
            : MasteryState.Learning;
    }

    /// <summary>
    /// PLC-70's <c>maintenance_pass</c>: a re-verification of a mastered cell that succeeded. It keeps
    /// the cell mastered and restarts the inactivity clock. A failed re-verification is recorded as a
    /// wrong answer (<see cref="RecordAnswer"/>), because that is the evidence it produced.
    /// </summary>
    public static Step MaintenancePass(Snapshot current, DateTimeOffset at) =>
        current.State == MasteryState.Mastered
            ? Move(current, current.Evidence, 0, MasteryState.Mastered,
                at + MasterySchedule.MasteredInterval, at)
            : throw new InvalidOperationException(
                "A maintenance pass re-verifies a mastered cell; a cell that is not mastered has nothing to maintain.");

    /// <summary>
    /// PLC-71's <c>inactivity_tick</c>. Suppressed when a mock section at or above the cell's level has
    /// demonstrated the cell since it was last practised - that is the ticket's exception, and it is
    /// scoped to the <b>cell</b>: the same skill at the same level or above, at this topic's own level.
    /// A below-level mock is not evidence about a B2 cell, and a different skill's mock is not evidence
    /// about this one.
    /// </summary>
    /// <param name="demonstratedByMockSection">
    /// True when, since <paramref name="current"/>'s <see cref="Snapshot.LastPractisedAt"/>, a mock
    /// section <b>at or above this cell's level for this cell's skill</b> was sat. The caller is
    /// <c>MasteryService</c>, which is the only place that can see the attempt tables.
    /// </param>
    public static Step InactivityTick(
        Snapshot current, DateTimeOffset at, bool demonstratedByMockSection)
    {
        // Decay describes a held position eroding. A cell that was never mastered holds nothing, so the
        // tick is a no-op - the trap named in the ticket, made unrepresentable rather than guarded.
        if (current.State != MasteryState.Mastered)
        {
            return new Step(current, current.State, current.State, current.NextReviewDueAt);
        }

        if (current.LastPractisedAt is not { } lastPractisedAt)
        {
            return new Step(current, current.State, current.State, current.NextReviewDueAt);
        }

        // The exception: a mock section at or above the level demonstrated the cell, so the calendar is
        // not evidence against it. The clock restarts from that demonstration.
        if (demonstratedByMockSection)
        {
            return Move(current, current.Evidence, current.ConsecutiveWrong, MasteryState.Mastered,
                at + MasterySchedule.MasteredInterval, practisedAt: at);
        }

        var weeks = MasteryDecay.WeeksOverdue(lastPractisedAt, at);
        if (weeks <= 0)
        {
            return new Step(current, current.State, current.State, current.NextReviewDueAt);
        }

        var decayed = MasteryDecay.Apply(current.Probability, weeks);
        var nextState = decayed < MasterySchedule.MasteredThreshold
            ? MasteryState.Reviewing
            : MasteryState.Mastered;

        // The two weighted sums are re-based on the decayed probability so the stored estimate and the
        // stored state cannot disagree: with -0 = -0 = 2, p = (2 + wc)/(4 + wn) and (2+wc)+(2+wn) is
        // scaled to hold wn (the evidence volume) while p takes the decayed value.
        var rebased = RebaseToProbability(current.Evidence, decayed);

        return new Step(
            current with
            {
                State = nextState,
                Evidence = rebased,
                ConsecutiveWrong = 0,
                // The decay is not an answer: it does not move LastPractisedAt, so the next tick
                // measures from the same origin and the drift continues at the stated rate.
                NextReviewDueAt = nextState == MasteryState.Mastered
                    ? at + MasterySchedule.MasteredInterval
                    : at + MasterySchedule.LearningInterval,
            },
            current.State,
            nextState,
            nextState == MasteryState.Mastered
                ? at + MasterySchedule.MasteredInterval
                : at + MasterySchedule.LearningInterval);
    }

    /// <summary>
    /// The evidence pair that produces <paramref name="probability"/> while keeping the observed
    /// volume. Solving <c>p = (-0 + wc)/(-0 + -0 + wn)</c> for <c>wc</c> with <c>wn</c> fixed:
    /// <c>wc = p-(-0+-0+wn) - -0</c>. Clamped at zero because a probability below the prior would need
    /// a negative correct count, and the decay never goes below the prior.
    /// </summary>
    private static MasteryEvidence RebaseToProbability(MasteryEvidence evidence, double probability)
    {
        var weightedCorrect =
            (probability * (MasterySchedule.PriorCorrect + MasterySchedule.PriorWrong
                            + evidence.WeightedObservations))
            - MasterySchedule.PriorCorrect;

        return evidence with { WeightedCorrect = Math.Max(0d, weightedCorrect) };
    }

    private static Step Move(
        Snapshot current,
        MasteryEvidence evidence,
        int consecutiveWrong,
        MasteryState state,
        DateTimeOffset due,
        DateTimeOffset practisedAt)
    {
        var masteredAt = current.MasteredAt ?? (state == MasteryState.Mastered ? practisedAt : null);

        return new Step(
            new Snapshot(
                state,
                evidence,
                consecutiveWrong,
                practisedAt,
                masteredAt,
                due),
            current.State,
            state,
            due);
    }

    /// <summary>
    /// The same "due" test the category scheduler uses: an unmeasured cell is always due, and any other
    /// is due once its review date has arrived.
    /// </summary>
    public static bool IsDue(Snapshot current, DateTimeOffset at) =>
        current.State == MasteryState.Untested
        || current.NextReviewDueAt is null
        || current.NextReviewDueAt <= at;
}
