using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class AttemptEngine
{
    public Task<int> ComputeScaledScoreAsync(
        int certId,
        int skillId,
        int examId,
        int rawScore,
        int totalQuestionsInSkill,
        CancellationToken cancellationToken = default) =>
        // Both entry points share one behaviour: interpolate between the surrounding rows, round by the skill's step.
        ComputeScaledScoreAsync(certId, skillId, examId, (decimal)rawScore, totalQuestionsInSkill, cancellationToken);

    public static int RoundHalfStep(double average)
    {
        // IELTS official rounding:
        // if decimal is < 0.25 -> .0; >= 0.25 and < 0.75 -> .5; >= 0.75 -> next whole band
        if (average > 10.0)
        {
            double band = average / 10.0;
            double floor = Math.Floor(band);
            double rem = band - floor;
            double roundedBand = rem < 0.25 ? floor : rem < 0.75 ? floor + 0.5 : floor + 1.0;
            return (int)Math.Round(roundedBand * 10.0);
        }
        else
        {
            double floor = Math.Floor(average);
            double rem = average - floor;
            double roundedBand = rem < 0.25 ? floor : rem < 0.75 ? floor + 0.5 : floor + 1.0;
            return (int)Math.Round(roundedBand * 10.0);
        }
    }

    /// <summary>
    /// The one aggregation rule, for a winner-takes-nothing set of three. <paramref name="scoreAggregationRule"/>
    /// is a certificate column bounded by <c>CK_Certificates_ScoreAggregationRule</c>; a value outside
    /// that set means the database predates the constraint or was edited around it, and is a
    /// configuration error rather than a fourth behaviour to guess at. #171 removed the
    /// <c>independent</c> rule, which returned a total of 0 for every attempt while the trigger that
    /// checks a total against its skills exempted it: a certificate could be authored whose learners
    /// could never be scored, and nothing reported it.
    /// </summary>
    public static int AggregateTotalScore(
        string? scoreAggregationRule,
        IReadOnlyCollection<AttemptSkillScore> skillScores)
    {
        if (skillScores.Count == 0) return 0;

        var average = skillScores.Average(score => (double)score.ScaledScore);
        return scoreAggregationRule?.Trim().ToLowerInvariant() switch
        {
            null or "" or "sum" => skillScores.Sum(score => score.ScaledScore),
            "avg_half_step" => RoundHalfStep(average),
            "avg_whole" => (int)Math.Round(average, MidpointRounding.AwayFromZero),
            // Never a silent zero and never a silent sum: the certificate has to be fixed.
            _ => throw new ScoreConversionException(
                ScoreConversionException.AggregationRuleUnknown,
                $"Certificate aggregation rule '{scoreAggregationRule}' is not one of sum, avg_half_step or avg_whole, so this attempt has no defensible total score."),
        };
    }

    private async Task<int> ComputeScaledScoreAsync(
        int certId,
        int skillId,
        int examId,
        decimal rawScore,
        int totalQuestionsInSkill,
        CancellationToken cancellationToken)
    {
        // The scale belongs to the skill; a skill without one is a configuration error, never a guess.
        var skill = await db.Skills
            .AsNoTracking()
            .Where(s => s.SkillId == skillId && s.CertId == certId)
            .Select(s => new { s.Code, s.MinScaledScore, s.MaxScaledScore, s.ScoreStep })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ScoreConversionException(
                ScoreConversionException.SkillScaleNotConfigured,
                $"Skill {skillId} of certificate {certId} does not exist, so it has no score scale.");

        if (skill.MinScaledScore is not int minimum
            || skill.MaxScaledScore is not int maximum
            || skill.ScoreStep is not int step)
        {
            throw new ScoreConversionException(
                ScoreConversionException.SkillScaleNotConfigured,
                $"Skill '{skill.Code}' has no score scale configured (MinScaledScore, MaxScaledScore and ScoreStep must all be set).");
        }

        var scaleRows = await db.ScoreScales
            .AsNoTracking()
            .Where(s => s.CertId == certId && s.SkillId == skillId && s.ExamId == examId)
            .OrderBy(s => s.RawScore)
            .ToListAsync(cancellationToken);
        if (scaleRows.Count == 0)
        {
            scaleRows = await db.ScoreScales
                .AsNoTracking()
                .Where(s => s.CertId == certId && s.SkillId == skillId && s.ExamId == null)
                .OrderBy(s => s.RawScore)
                .ToListAsync(cancellationToken);
        }

        var (lower, upper) = (scaleRows.LastOrDefault(row => row.RawScore <= rawScore),
                              scaleRows.FirstOrDefault(row => row.RawScore >= rawScore));
        return ProjectOntoScale(
            rawScore,
            lower?.RawScore,
            lower?.ScaledScore,
            upper?.RawScore,
            upper?.ScaledScore,
            minimum,
            maximum,
            step,
            totalQuestionsInSkill);
    }

    /// <summary>
    /// E1.1 (#146): the one score-conversion behaviour. Interpolate linearly between the two rows that
    /// bracket the raw score, then round onto the skill's own grid. With no rows at all, spread the raw
    /// score proportionally across the skill's range instead. <paramref name="minimum"/> is 0..n and
    /// <paramref name="step"/> &gt; 0, so callers must have already rejected an unconfigured skill.
    /// </summary>
    public static int ProjectOntoScale(
        decimal rawScore,
        int? lowerRaw,
        int? lowerScaled,
        int? upperRaw,
        int? upperScaled,
        int minimum,
        int maximum,
        int step,
        int totalQuestionsInSkill = 0)
    {
        if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step), step, "ScoreStep must be positive.");

        decimal scaled;
        if (lowerScaled is not int low)
        {
            // No lower row: below the bottom of the table (or no table at all).
            scaled = upperScaled is int top
                ? top
                : totalQuestionsInSkill > 0
                    ? minimum + Math.Clamp(rawScore / totalQuestionsInSkill, 0m, 1m) * (maximum - minimum)
                    : minimum;
        }
        else if (upperScaled is not int high || upperRaw is not int upperRawScore || upperRawScore == lowerRaw)
        {
            scaled = low;
        }
        else
        {
            scaled = low + (rawScore - lowerRaw!.Value) * (high - low) / (upperRawScore - lowerRaw!.Value);
        }

        return (int)Math.Round(scaled / step, MidpointRounding.AwayFromZero) * step;
    }

    /// <summary>
    /// #251: the one write of an attempt's score and band outside a submission, for a caller that
    /// recomputed the skill scores for its own reason (telemetry's recalculation after a confirmed
    /// content defect). The caller supplies the skill rows; every number the derived fact is made of ΓÇö
    /// the total through <see cref="AggregateTotalScore"/> and the band through
    /// <see cref="FindBandCodeAsync"/> ΓÇö is decided here, and the write declares this table's owner.
    /// <para>
    /// #191: the clear, the skill rows and the result are three statements because the guards require
    /// them in that order, and each score write declares the writer and moves the projection version
    /// forward. <c>Property(...).IsModified</c> is needed because EF omits an unchanged column, and a
    /// second write of the stamp would otherwise carry no declaration.
    /// </para>
    /// </summary>
    public async Task<(int Total, string? Band)> WriteAttemptScoreAsync(
        int attemptId,
        IReadOnlyList<AttemptSkillScore> skillScores,
        CancellationToken cancellationToken = default)
    {
        var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var attempt = await db.ExamAttempts
                .SingleOrDefaultAsync(a => a.AttemptId == attemptId, cancellationToken)
                ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

            // Both numbers are decided before the first write, so a certificate whose rule this engine
            // refuses leaves the attempt exactly as it found it rather than relying on a rollback.
            // #227 / #257: the rule is the one the edition froze; the certificate column is the
            // fallback for an attempt with no edition row, and that fallback is named inside the helper.
            var rule = await LoadAggregationRuleAsync(attempt.ExamId, attempt.CertId, cancellationToken);
            var total = AggregateTotalScore(rule, skillScores);
            var band = await FindBandCodeAsync(attempt.CertId, total, cancellationToken);

            attempt.TotalScore = null;
            attempt.WrittenByService = DerivedFactWriters.AttemptScoreAndBand;
            attempt.ProjectionVersion++;
            db.Entry(attempt).Property(a => a.WrittenByService).IsModified = true;
            await db.SaveChangesAsync(cancellationToken);

            // Deleted and saved before the replacements are added: the primary key is (attempt, skill),
            // so tracking a removal and an insert of the same key in one save would collide.
            var existing = await db.AttemptSkillScores
                .Where(s => s.AttemptId == attemptId)
                .ToListAsync(cancellationToken);
            db.AttemptSkillScores.RemoveRange(existing);
            await db.SaveChangesAsync(cancellationToken);

            db.AttemptSkillScores.AddRange(skillScores);
            await db.SaveChangesAsync(cancellationToken);

            attempt.TotalScore = total;
            attempt.BandCode = band;
            attempt.WrittenByService = DerivedFactWriters.AttemptScoreAndBand;
            attempt.ProjectionVersion++;
            db.Entry(attempt).Property(a => a.WrittenByService).IsModified = true;
            await db.SaveChangesAsync(cancellationToken);

            if (tx is not null) await tx.CommitAsync(cancellationToken);
            return (total, band);
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync();
        }
    }

    private async Task<string?> FindBandCodeAsync(int certId, int totalScore, CancellationToken cancellationToken)
    {
        var bands = await db.ScoreBands
            .Where(b => b.CertId == certId)
            .OrderBy(b => b.MinTotal)
            .ToListAsync(cancellationToken);

        return MatchBandCode(totalScore, bands);
    }

    /// <summary>
    /// The one band rule: the band whose range holds the score, else the nearest one below, else the
    /// lowest band (a score under the table is not a refusal). <paramref name="bands"/> must be ordered
    /// by lower bound. Every caller in the solution — the attempt engine, the review pages and the
    /// certificate wizard's preview — matches through here.
    /// </summary>
    internal static T? MatchBand<T>(
        int score, IReadOnlyList<T> bands, Func<T, int> minTotal, Func<T, int> maxTotal)
        where T : class
    {
        if (bands.Count == 0) return null;

        var matchingBand = bands.FirstOrDefault(b => score >= minTotal(b) && score <= maxTotal(b));
        if (matchingBand is null)
        {
            matchingBand = score < minTotal(bands[0])
                ? bands[0]
                : bands.LastOrDefault(b => score >= minTotal(b)) ?? bands[^1];
        }

        return matchingBand;
    }

    public static string? MatchBandCode(int score, IReadOnlyList<ScoreBand> bands) =>
        MatchBand(score, bands, b => b.MinTotal, b => b.MaxTotal)?.Code;

    /// <summary>The one copy: the band grid (here) and the skill-step derivation both reduce with it.</summary>
    internal static int GreatestCommonDivisor(int a, int b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return Math.Abs(a);
    }

    /// <summary>
    /// #152: how many stored units make one reported point. IELTS and VSTEP keep their marks in
    /// tenths — their <c>ScoreBands</c> read 40–90 for 4.0–9.0, their <c>ScoreScales</c> store 65 for
    /// 6.5, and <see cref="RoundHalfStep"/> works in tenths too — so a stored 75 is 7.5. Every
    /// certificate that does not say otherwise stores the number it reports.
    /// </summary>
    public static int DisplayFactor(string? scoreAggregationRule) => ScoreDisplay.FactorFor(scoreAggregationRule);

    /// <summary>
    /// #152: a stored integer as the learner reads it. A page that prints the stored value is wrong
    /// by this factor on every certificate whose scale is in tenths, which is how "75 out of 90"
    /// appeared for a 7.5.
    /// </summary>
    public static decimal ReportedScore(int stored, int displayFactor) => (decimal)stored / displayFactor;

    /// <summary>
    /// #152: the axis a rubric score is reported on. The skill's own configured scale wins where it
    /// exists — the ticket asks for exactly that, and it is data, not a guess (TOEIC S&amp;W's
    /// speaking scale genuinely starts at 50). Where the skill has none (#148 found them NULL for
    /// every seeded writing and speaking skill) the axis runs from zero to the top of the
    /// certificate's own highest band, on whatever grid those band bounds sit on: IELTS stores tenths
    /// and its bounds are multiples of 5, so the grid is half a band. A grid too coarse to tell two
    /// learners apart falls back to 1. Zero is the fallback floor because a rubric of nothing is zero
    /// — the lowest band's bound is a floor for a *band*, not for a score.
    /// </summary>
    public static async Task<(int Min, int Top, int Step, IReadOnlyList<ScoreBand> Bands)?> RubricAxisAsync(
        CompanionDbContext db, int certId, int? skillId, CancellationToken cancellationToken = default)
    {
        var bands = await db.ScoreBands
            .AsNoTracking()
            .Where(b => b.CertId == certId)
            .OrderBy(b => b.MinTotal)
            .ToListAsync(cancellationToken);

        if (skillId is int id)
        {
            var skill = await db.Skills
                .AsNoTracking()
                .Where(s => s.SkillId == id && s.CertId == certId)
                .Select(s => new { s.MinScaledScore, s.MaxScaledScore, s.ScoreStep })
                .FirstOrDefaultAsync(cancellationToken);

            if (skill?.MinScaledScore is int min
                && skill.MaxScaledScore is int max
                && skill.ScoreStep is int step
                && max > min)
            {
                return (min, max, step, bands);
            }
        }

        // No bands, no axis: a score would be a number with nothing behind it.
        if (bands.Count == 0) return null;

        var top = bands.Max(b => b.MaxTotal);
        var grid = bands.SelectMany(b => new[] { b.MinTotal, b.MaxTotal })
            .Aggregate(0, (a, b) => GreatestCommonDivisor(a, b));

        return (0, top, grid >= 2 && top / grid >= 10 ? grid : 1, bands);
    }

    /// <summary>
    /// #152: where a rubric fraction is reported: the score, the band, and the axis both came from.
    /// One placement, one number — the band is the band whose range holds the score this call
    /// returned, so a page cannot print a score beside a band that score cannot reach. Before this the
    /// score was placed on a 0–top axis and the band on a lowest-band-to-highest-band one, and the
    /// same attempt could show C1 beside a score that no C1 range contains.
    /// </summary>
    /// <remarks>
    /// Zero, or a certificate that declares no axis, reports nothing: the page then says so rather
    /// than printing a number with no table behind it (D14, #148).
    /// </remarks>
    public static async Task<(int Score, int Top, int Step, ScoreBand? Band)?> PlaceRubricAsync(
        CompanionDbContext db, int certId, int? skillId, decimal rubricTotal, decimal rubricMax,
        CancellationToken cancellationToken = default)
    {
        if (rubricMax <= 0m || rubricTotal <= 0m) return null;

        var axis = await RubricAxisAsync(db, certId, skillId, cancellationToken);
        if (axis is not (var min, var top, var step, var bands)) return null;

        var fraction = Math.Clamp(rubricTotal / rubricMax, 0m, 1m);
        // totalQuestionsInSkill = 1 is the honest value: the numerator is already a fraction of the
        // rubric, so "out of 1" is exactly the proportional spread this call asks for.
        var score = ProjectOntoScale(fraction, null, null, null, null, min, top, step, 1);

        return (score, top, step, bands.Count == 0 ? null : MatchBand(score, bands, b => b.MinTotal, b => b.MaxTotal));
    }

    /// <summary>
    /// E1.3 (#148): the band a single AI-graded skill's rubric total belongs to — the same placement
    /// <see cref="PlaceRubricAsync"/> reports, so the stored column and the page cannot disagree.
    /// </summary>
    public static async Task<ScoreBand?> EstimateBandForRubricAsync(
        CompanionDbContext db, int certId, decimal rubricTotal, decimal rubricMax,
        int? skillId = null, CancellationToken cancellationToken = default) =>
        (await PlaceRubricAsync(db, certId, skillId, rubricTotal, rubricMax, cancellationToken))?.Band;

    /// <summary>
    /// E1.3 (#148): the band of a skill-practice attempt, decided once from every graded response.
    /// Both the grading worker (which stamps it on the attempt's <c>AiGradings</c> rows) and the two
    /// review pages call this, so the stored column and the page cannot disagree, and an attempt with
    /// nothing scored gets no band rather than the certificate's floor.
    /// </summary>
    public static async Task<ScoreBand?> EstimateBandForAttemptAsync(
        CompanionDbContext db, int attemptId, int certId, CancellationToken cancellationToken = default)
    {
        var (total, max) = await RubricTotalsAsync(db, attemptId, cancellationToken);
        // The attempt's own skill is the one a scale would be configured on (#152). One rubric level,
        // one skill: a skill-practice attempt grades a single skill.
        var skillId = await db.FreeResponses
            .AsNoTracking()
            .Where(fr => fr.AttemptId == attemptId)
            .Select(fr => (int?)fr.SkillId)
            .FirstOrDefaultAsync(cancellationToken);
        return await EstimateBandForRubricAsync(db, certId, total, max, skillId, cancellationToken);
    }

    /// <summary>
    /// E1.3 (#148): the rubric aggregate the attempt-level band is stored from — every AI-graded
    /// response's score over every applicable criterion maximum (section override when there is one,
    /// else the criterion's). One definition, used by the grading worker so the stored band and the
    /// review page cannot drift apart.
    /// </summary>
    public static async Task<(decimal Total, decimal Max)> RubricTotalsAsync(
        CompanionDbContext db, int attemptId, CancellationToken cancellationToken = default)
    {
        var scores = await db.AiGradingScores
            .AsNoTracking()
            .Where(s => s.AiGrading.FreeResponse.AttemptId == attemptId
                && s.AiGrading.Status == AiGradingStatus.Done.ToCode())
            .Include(s => s.GradingCriterion)
            .Include(s => s.AiGrading).ThenInclude(g => g.FreeResponse)
            .ToListAsync(cancellationToken);

        if (scores.Count == 0) return (0m, 0m);

        // #269: a pool free response has no Question row, so `fr.Question.SectionId` threw here and the
        // whole band estimate died with it - the grading itself had succeeded, so the learner was charged
        // for a result the app could not show. The section comes from the position the response names.
        var sectionByResponse = await RosterSkills.ByResponseSectionAsync(db, attemptId, cancellationToken);

        int? SectionOf(BusinessObjects.Entities.AiGradingScore score) =>
            sectionByResponse.GetValueOrDefault(score.AiGrading.FreeResponseId);

        var sectionIds = scores
            .Select(SectionOf)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var overrides = await db.SectionCriteria
            .AsNoTracking()
            .Where(sc => sectionIds.Contains(sc.SectionId))
            .ToListAsync(cancellationToken);

        decimal total = 0m, max = 0m;
        var marks = new List<RubricMark>(scores.Count);
        foreach (var score in scores)
        {
            var sectionId = SectionOf(score);
            var markMax = sectionId is int id
                ? overrides
                    .FirstOrDefault(sc => sc.SectionId == id && sc.CriterionId == score.CriterionId)
                    ?.MaxScore ?? score.GradingCriterion.MaxScore
                : score.GradingCriterion.MaxScore;
            marks.Add(new RubricMark(score.Score, markMax, score.GradingCriterion.Weight));
        }

        // E2.1 (#151): the same weighting the grading worker used to store OverallScaled, because the
        // band has to be the band of the score the page prints.
        (total, max) = WeightedRubricTotal(marks);
        return (total, max);
    }

    /// <summary>
    /// E2.1 (#151): one criterion's mark, the ceiling it was marked against, and how much it counts.
    /// </summary>
    public readonly record struct RubricMark(decimal Score, decimal MaxScore, decimal Weight);

    /// <summary>
    /// E2.1 (#151): the rubric total, with each criterion counting for its own <c>Weight</c>:
    /// <c>S = Σ(Score_i / MaxScore_i × Weight_i) / Σ Weight_i × Σ MaxScore_i</c>.
    /// </summary>
    /// <remarks>
    /// The fraction in the middle is the documented rule; multiplying it back out by the sum of the
    /// ceilings keeps the number on the same "x out of Σ MaxScore" scale the review page already
    /// prints, so the column, the page and the band all read one figure. With every weight at 1 this
    /// is exactly the plain sum it replaces, which is why no stored score moves.
    /// </remarks>
    public static (decimal Total, decimal Max) WeightedRubricTotal(IEnumerable<RubricMark> marks)
    {
        decimal max = 0m, weight = 0m, weightedFraction = 0m;
        foreach (var mark in marks)
        {
            // Skip before counting the ceiling: a criterion with no weight contributes nothing to the
            // numerator, so counting its MaxScore in the denominator would cap a perfect answer below
            // full marks (weight 0/1 over max 5/5 used to report 5 out of 10).
            if (mark.MaxScore <= 0m || mark.Weight <= 0m) continue;
            max += mark.MaxScore;
            weight += mark.Weight;
            weightedFraction += mark.Score / mark.MaxScore * mark.Weight;
        }

        if (max <= 0m || weight <= 0m) return (0m, max);
        // Four decimals then round: the column is DECIMAL(6,2), and rounding a weighted fraction to two
        // places before it is scaled would throw away the difference the weights just made.
        var fraction = Math.Clamp(weightedFraction / weight, 0m, 1m);
        return (Math.Round(fraction * max, 2, MidpointRounding.AwayFromZero), max);
    }
}
