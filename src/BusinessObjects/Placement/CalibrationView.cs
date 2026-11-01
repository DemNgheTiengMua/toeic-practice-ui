namespace BusinessObjects;

/// <summary>
/// #156: what the system can honestly say about whether a learner's practice matches the band their
/// placement gave them. Every field is either a count it observed or a threshold it states — never a
/// verdict dressed up as a measurement. <see cref="Proposal"/> is null when there is nothing to say,
/// and <see cref="Reason"/> says which of the reasons it is, so "no proposal yet" is never a mystery.
/// </summary>
public sealed record CalibrationView(
    string PlacedBandCode,
    string? PlacedBandName,
    int ValidAnswerCount,
    int RequiredValidAnswerCount,
    int ExcludedRushedCount,
    decimal? ObservedAccuracyPct,
    string Reason,
    CalibrationProposal? Proposal);

/// <summary>
/// #156: a suggestion the learner may accept or defer. <see cref="Evidence"/> is the sentence the
/// screen prints, built from the same numbers as the fields, so the copy cannot drift from the data.
/// </summary>
public sealed record CalibrationProposal(
    string ProposedBandCode,
    string ProposedBandName,
    decimal ObservedAccuracyPct,
    int ValidAnswerCount,
    int RequiredAccuracyPct,
    string Evidence);

/// <summary>#156: why a <see cref="CalibrationView"/> says what it says.</summary>
public static class CalibrationReason
{
    /// <summary>No finished placement, so there is no band to diverge from.</summary>
    public const string NotPlaced = "not_placed";

    /// <summary>Fewer valid answers than the threshold: the honest answer is "not yet".</summary>
    public const string BelowThreshold = "below_threshold";

    /// <summary>Enough valid answers, and accuracy is at or above the floor. Nothing to correct.</summary>
    public const string Consistent = "consistent";

    /// <summary>Enough valid answers and accuracy is below the floor: there is a proposal.</summary>
    public const string Diverged = "diverged";

    /// <summary>Enough valid answers, below the floor, but no band below the placed one exists.</summary>
    public const string LowestBand = "lowest_band";

    /// <summary>
    /// #156: the learner accepted an earlier proposal and the path already starts at or below the
    /// band this evidence supports. Asking again would be asking them to repeat a decision.
    /// </summary>
    public const string Calibrated = "calibrated";

    /// <summary>The learner already answered this proposal and chose to keep their path.</summary>
    public const string Deferred = "deferred";
}

/// <summary>#156: what answering a proposal did.</summary>
public sealed record CalibrationDecisionResult(
    string Decision,
    string PlacedBandCode,
    string ProposedBandCode,
    bool PathAdjusted);
