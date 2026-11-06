using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;

namespace Services;

/// <summary>#197: gate four's verdict for one item. <see cref="Predicted"/> is the band the classifier chose, or null.</summary>
public sealed record GateFourVerdict(IReadOnlyList<GateFourRefusal> Refusals, CefrLevel? Predicted)
{
    public bool Accepted => Refusals.Count == 0;
}

/// <summary>
/// #197: gate four's two halves that need something the pure rules cannot have.
/// <list type="number">
/// <item><b>The classifier.</b> An AI call, behind <see cref="ILevelClassifier"/> so the test suite
/// passes <see cref="FakeLevelClassifier"/> and a real model is an explicit opt-in at composition
/// time. The threshold stays in <see cref="GateFour.ReviewLevel"/>; this only makes the call.</item>
/// <item><b>The seven-response ladder.</b> D17 requires separation and the trap ceiling to hold
/// <em>at ingestion</em>, not only in the studio. That arithmetic is
/// <see cref="RubricCalibrationLadder"/> (#193) and is called here - never re-implemented. The only
/// thing this class adds is the translation from the ladder's outcome to gate four's rule codes, so a
/// second copy of the ladder cannot answer the same question differently.</item>
/// </list>
/// </summary>
public static class GateFourIngestion
{
    /// <summary>Classifies the item's text and judges the authored band against the prediction.</summary>
    public static async Task<GateFourVerdict> ReviewLevelAsync(
        string authorFamily,
        CefrLevel authoredLevel,
        string text,
        ILevelClassifier? classifier,
        CancellationToken cancellationToken = default)
    {
        GateFourClassification classification = classifier is null
            ? new GateFourClassification.Unavailable(
                "no level classifier is configured, so the item's level was not predicted")
            : await classifier.ClassifyAsync(
                new GateFourClassifyRequest(text, authoredLevel), cancellationToken);

        var refusals = GateFour.ReviewLevel(authorFamily, authoredLevel, classification);
        var predicted = classification is GateFourClassification.Predicted p ? p.Level : (CefrLevel?)null;

        return new GateFourVerdict(refusals, predicted);
    }

    /// <summary>
    /// Runs the seven-response ladder through gate four's rule codes. <paramref name="responses"/> is
    /// the six band benchmarks plus the verbose trap; <paramref name="grader"/> is the repo's one
    /// grading seam, so the ladder and a learner's grading are judged by the same arithmetic.
    /// <para>
    /// A ladder that cannot be run is a refusal, not an exception: an ingestion loop reports one line
    /// per record (#198), and an exception thrown for one rubric would lose the lines around it.
    /// </para>
    /// </summary>
    public static async Task<IReadOnlyList<GateFourRefusal>> ReviewProductiveAsync(
        IReadOnlyList<GradingCriterionDetail> criteria,
        IAiGrader grader,
        IReadOnlyList<RubricLadderResponse> responses,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(grader);
        ArgumentNullException.ThrowIfNull(responses);

        RubricCalibrationResult result;
        try
        {
            result = await RubricCalibrationLadder.RunAsync(criteria, grader, responses, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            // The ladder's own refusal to run - a missing benchmark, a trap that is not there, no
            // criteria. Its message names the missing thing, which is what the author needs.
            return [new GateFourRefusal(
                GateFourRules.LadderIncomplete,
                $"the rubric's seven-response ladder was not run: {exception.Message}")];
        }

        if (result.Accepted)
        {
            return [];
        }

        return [new GateFourRefusal(RuleFor(result.Outcome), result.Refusal ?? "the ladder was refused")];
    }

    /// <summary>
    /// The ladder's outcome, read as one of gate four's codes. Every outcome maps to a distinct code,
    /// so a caller branching on the rule is never told "the ladder" when the diagnosis is "the gap is
    /// flat" - the distinction the ladder itself makes.
    /// </summary>
    private static string RuleFor(RubricCalibrationOutcome outcome) => outcome switch
    {
        RubricCalibrationOutcome.NotStrictlyMonotonic => GateFourRules.LadderMonotonic,
        RubricCalibrationOutcome.SeparationBelowThreshold => GateFourRules.LadderSeparation,
        RubricCalibrationOutcome.VerboseTrapAboveCeiling => GateFourRules.LadderTrapCeiling,
        _ => GateFourRules.LadderNotPlotted,
    };
}
