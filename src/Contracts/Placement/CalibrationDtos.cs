namespace Contracts;

/// <summary>
/// #156: what the system can say about whether practice matches the placement band.
/// <see cref="Proposal"/> is null when there is nothing to propose, and <see cref="Reason"/> says
/// which of the reasons that is.
/// </summary>
public sealed record CalibrationViewDto(
    string PlacedBandCode,
    string? PlacedBandName,
    int ValidAnswerCount,
    int RequiredValidAnswerCount,
    int ExcludedRushedCount,
    decimal? ObservedAccuracyPct,
    string Reason,
    CalibrationProposalDto? Proposal);

public sealed record CalibrationProposalDto(
    string ProposedBandCode,
    string ProposedBandName,
    decimal ObservedAccuracyPct,
    int ValidAnswerCount,
    int RequiredAccuracyPct,
    string Evidence);

/// <summary>#156: the learner's answer to a proposal. False means "later", path untouched.</summary>
public sealed record CalibrationDecisionRequest(bool Adjust);

public sealed record CalibrationDecisionResultDto(
    string Decision,
    string PlacedBandCode,
    string ProposedBandCode,
    bool PathAdjusted);
