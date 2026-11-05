using System;
using System.Collections.Generic;

namespace Services;

/// <summary>
/// #193: one response in the seven-response ladder. Six of these are the band benchmarks (bands 1-6);
/// the seventh is the adversarial verbose trap, which is authored at band 3 because that is where
/// verbosity is most likely to be mistaken for ability.
/// </summary>
/// <param name="Band">The band this response was written to demonstrate.</param>
/// <param name="ResponseText">The response itself, exactly as a grader will see it.</param>
/// <param name="IsVerboseTrap">
/// True for the 280-word trap of repetitive simple clauses. It carries a band like any other response,
/// but it is judged against a ceiling rather than against its neighbours (D17 gate 4).
/// </param>
public sealed record RubricLadderResponse(byte Band, string ResponseText, bool IsVerboseTrap = false);

/// <summary>One plotted point: what the rubric made of a response, on the 0-6 composite axis.</summary>
public sealed record RubricLadderPoint(byte Band, decimal Composite, bool IsVerboseTrap = false);

/// <summary>Why a ladder was refused, or that it was not.</summary>
public enum RubricCalibrationOutcome
{
    Accepted,

    /// <summary>A segment does not rise. Distinct from a threshold miss so an author is not sent to
    /// widen a gap that is actually flat.</summary>
    NotStrictlyMonotonic,

    /// <summary>Every segment rises, but one rises by less than 0.65.</summary>
    SeparationBelowThreshold,

    /// <summary>The ladder separates, but the verbose trap is scored above 3.25.</summary>
    VerboseTrapAboveCeiling,

    /// <summary>The grader did not return a usable score for one of the responses.</summary>
    GraderFailed,
}

/// <summary>
/// #193: the verdict on a rubric's calibration. <see cref="Ladder"/> is returned on every refusal too:
/// an author has to see the plot, not only the verdict.
/// </summary>
/// <param name="ResponsibleCriteria">
/// The criteria whose own movement across the failing segment was the smallest - the ones an author has
/// to rewrite. Empty on acceptance, and on a trap refusal (the trap is a whole-rubric failure).
/// </param>
public sealed record RubricCalibrationResult(
    RubricCalibrationOutcome Outcome,
    bool Accepted,
    string? Refusal,
    IReadOnlyList<RubricLadderPoint> Ladder,
    IReadOnlyList<string> ResponsibleCriteria);
