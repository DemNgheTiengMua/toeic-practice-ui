using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Services;

/// <summary>
/// #193: the calibration ladder from D0.6 mechanism 5 and D17 gate 4. Six benchmark responses at bands
/// 1-6 plus an adversarial verbose trap are graded through the repo's one grading seam
/// (<see cref="IAiGrader"/>, the same interface <see cref="AiGradingProcessor"/> drives), and the
/// composites are plotted. A rubric is refused when a segment does not rise, when a segment rises by
/// less than <see cref="MinimumSeparation"/>, or when the trap climbs above
/// <see cref="VerboseTrapCeiling"/>.
/// <para>
/// Nothing here writes or reads a database: the ladder is an authoring check over synthetic text, so a
/// response has no <c>FreeResponses</c> row and no learner. That is also why it takes an
/// <see cref="IAiGrader"/> rather than the processor - a queue member exists to grade a learner's
/// submitted work, and the ladder's text is not that.
/// </para>
/// </summary>
public static class RubricCalibrationLadder
{
    /// <summary>D17 gate 4: the minimum composite gap between adjacent bands.</summary>
    public const decimal MinimumSeparation = 0.65m;

    /// <summary>D17 gate 4: the highest composite the 280-word verbose trap may reach.</summary>
    public const decimal VerboseTrapCeiling = 3.25m;

    /// <summary>The band benchmarks, in ladder order. Band 0 is not one: a response written to be
    /// non-scorable is not a benchmark, and the ladder is about separation between real bands.</summary>
    public static readonly IReadOnlyList<byte> BenchmarkBands = [1, 2, 3, 4, 5, 6];

    public static async Task<RubricCalibrationResult> RunAsync(
        IReadOnlyList<GradingCriterionDetail> criteria,
        IAiGrader grader,
        IReadOnlyList<RubricLadderResponse> responses,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(grader);
        ArgumentNullException.ThrowIfNull(responses);

        if (criteria.Count == 0)
        {
            throw new ArgumentException(
                "A calibration ladder needs the rubric's criteria; none were given, so there is nothing to calibrate.",
                nameof(criteria));
        }

        var benchmarks = responses.Where(r => !r.IsVerboseTrap).OrderBy(r => r.Band).ToList();
        var missingBands = BenchmarkBands.Where(b => benchmarks.All(r => r.Band != b)).ToList();
        if (missingBands.Count > 0)
        {
            throw new ArgumentException(
                "A calibration ladder is six benchmark responses at bands 1-6 plus one verbose trap; "
                + $"this one has no benchmark response for band {string.Join(", band ", missingBands)}.",
                nameof(responses));
        }

        var trap = responses.SingleOrDefault(r => r.IsVerboseTrap)
            ?? throw new ArgumentException(
                "A calibration ladder needs the adversarial verbose trap as well as the six benchmarks; "
                + "without it verbosity is never tested.", nameof(responses));

        // One grade per response, through the same IAiGrader the processor uses. The per-criterion
        // marks are kept because the diagnostic has to say which criterion failed to move.
        var marks = new List<(RubricLadderResponse Response, IReadOnlyDictionary<string, decimal> ByCode)>();
        foreach (var response in benchmarks.Append(trap))
        {
            var request = new AiGradingRequest(
                FreeResponseId: 0,          // synthetic text: no response row exists at authoring time
                SkillId: 0,
                CertId: 0,
                QuestionStem: string.Empty,
                MinWords: null,
                MaxWords: null,
                ResponseText: response.ResponseText,
                Criteria: criteria);

            var result = await grader.GradeAsync(request, cancellationToken);

            if (!result.Success)
            {
                return Refuse(
                    RubricCalibrationOutcome.GraderFailed,
                    $"The ladder could not be plotted: the grader returned no usable result for the band {response.Band} "
                    + $"response ({result.FailureReason ?? "no reason given"}). Fix the grader or the response, then run the ladder again.",
                    [], []);
            }

            var byCode = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var criterion in criteria)
            {
                var score = result.Scores.FirstOrDefault(s => s.CriterionId == criterion.CriterionId);
                if (score is null)
                {
                    // A composite over three of four criteria is not the mean D0.6 defines, so a hole is
                    // a failed ladder rather than a smaller average.
                    return Refuse(
                        RubricCalibrationOutcome.GraderFailed,
                        $"The ladder could not be plotted: the grader returned no score for {criterion.Code} "
                        + $"on the band {response.Band} response, so its composite would be an average over "
                        + "the criteria it did score.",
                        [], []);
                }

                byCode[criterion.Code] = criterion.MaxScore <= 0m
                    ? 0m
                    : Math.Clamp(score.Score / criterion.MaxScore, 0m, 1m) * CanonicalRubric.HighestBand;
            }

            marks.Add((response, byCode));
        }

        // D0.6: M_t = 1/4 * sum c_j. Unweighted, on the 0-6 band axis, so a criterion authored against a
        // shorter section ceiling still plots on the same ladder as the others.
        decimal Composite(IReadOnlyDictionary<string, decimal> byCode) =>
            Math.Round(byCode.Values.Sum() / byCode.Count, 4, MidpointRounding.AwayFromZero);

        var ladder = marks
            .Select(m => new RubricLadderPoint(
                m.Response.Band, Composite(m.ByCode), m.Response.IsVerboseTrap))
            .ToList();

        var points = ladder.Where(p => !p.IsVerboseTrap).ToList();

        for (var i = 0; i < points.Count - 1; i++)
        {
            var lower = points[i];
            var upper = points[i + 1];

            if (upper.Composite <= lower.Composite)
            {
                var responsible = StalledOn(marks, lower.Band, upper.Band, criteria, strict: false);
                return Refuse(
                    RubricCalibrationOutcome.NotStrictlyMonotonic,
                    $"The ladder is not strictly monotonic: band {upper.Band} scored "
                    + $"{Format(upper.Composite)} where band {lower.Band} scored {Format(lower.Composite)}. "
                    + $"Criterion {string.Join(", ", responsible)} did not move between those bands; rewrite "
                    + "its descriptors so the higher band asks for something the lower one does not.",
                    ladder, responsible);
            }

            var separation = upper.Composite - lower.Composite;
            if (separation < MinimumSeparation)
            {
                var responsible = StalledOn(marks, lower.Band, upper.Band, criteria, strict: true);
                return Refuse(
                    RubricCalibrationOutcome.SeparationBelowThreshold,
                    $"Bands {lower.Band} and {upper.Band} are only {Format(separation)} apart and the rubric needs "
                    + $"at least {Format(MinimumSeparation)}. Criterion {string.Join(", ", responsible)} moved the "
                    + "least across that step; widen its descriptors before the rubric is locked.",
                    ladder, responsible);
            }
        }

        var trapPoint = ladder.Single(p => p.IsVerboseTrap);
        if (trapPoint.Composite > VerboseTrapCeiling)
        {
            return Refuse(
                RubricCalibrationOutcome.VerboseTrapAboveCeiling,
                $"The verbose trap scored {Format(trapPoint.Composite)} and must stay at or below "
                + $"{Format(VerboseTrapCeiling)}. The rubric rewards length: cap the criteria the trap climbed "
                + "on, or require evidence the padding does not contain.",
                ladder, []);
        }

        return new RubricCalibrationResult(RubricCalibrationOutcome.Accepted, true, null, ladder, []);
    }

    /// <summary>
    /// The criteria whose own movement across the failing segment was the smallest. That is the one an
    /// author has to rewrite: a composite that did not rise is the sum of four criteria that did not
    /// rise, and naming all four when only one is stuck sends the author to the wrong row.
    /// </summary>
    private static IReadOnlyList<string> StalledOn(
        IReadOnlyList<(RubricLadderResponse Response, IReadOnlyDictionary<string, decimal> ByCode)> marks,
        byte lowerBand, byte upperBand,
        IReadOnlyList<GradingCriterionDetail> criteria,
        bool strict)
    {
        var lower = marks.Single(m => !m.Response.IsVerboseTrap && m.Response.Band == lowerBand).ByCode;
        var upper = marks.Single(m => !m.Response.IsVerboseTrap && m.Response.Band == upperBand).ByCode;

        var movements = criteria
            .Select(c => (c.Code, Delta: upper[c.Code] - lower[c.Code]))
            .ToList();

        if (strict)
        {
            // A rising segment that is too shallow: the laggard is the criterion that moved least, and
            // if every criterion moved the same amount they are all equally responsible.
            var minimum = movements.Min(m => m.Delta);
            return movements.Where(m => m.Delta == minimum).Select(m => m.Code).ToList();
        }

        var stalled = movements.Where(m => m.Delta <= 0m).Select(m => m.Code).ToList();
        return stalled.Count > 0
            ? stalled
            : movements.OrderBy(m => m.Delta).Take(1).Select(m => m.Code).ToList();
    }

    private static RubricCalibrationResult Refuse(
        RubricCalibrationOutcome outcome, string reason,
        IReadOnlyList<RubricLadderPoint> ladder, IReadOnlyList<string> responsible) =>
        new(outcome, false, reason, ladder, responsible);

    private static string Format(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);
}
