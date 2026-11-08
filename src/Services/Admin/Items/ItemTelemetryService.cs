using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #199 / D0.7 / CNT-44 / CNT-45: telemetry quarantine and monotonic learner protection.
/// <para>
/// <b>Nothing here maintains a counter.</b> Every administration count, facility, share and mean is
/// read from the stored answers on each run — <c>PracticeAnswers</c>, <c>AttemptAnswers</c> and
/// <c>AiGradingScores</c> — so a recount cannot disagree with the stored number, which is the defect
/// NFI-05 forbids. <see cref="ItemTelemetry"/> owns the arithmetic; this type owns which rows to hand it
/// and what to do with the verdict.
/// </para>
/// <para>
/// <b>What "target-band learner" means here.</b> The specification says "learners whose measured level
/// equals the item's own level". The measured per-skill level now lives in <c>LearnerSkillStates</c>
/// (#211), but the reading below has not been moved onto it yet. Until it is, the closest stored fact
/// is the level of the sitting the learner chose: a pool drill pins its cell's level on
/// <c>PracticeSessions.CefrLevel</c>, and a paper's item level is the bound version's item. That is a
/// <em>requested</em> level, not a measured one, so a learner who over-reaches is counted as
/// target-band for a band they are not at.
/// <para>
/// shortcut: target band = the sitting's level although <c>LearnerSkillStates</c> now exists (#211).
/// Upgrade when telemetry is next touched: swap <see cref="IsAtTargetBand"/> for a read of the
/// learner's measured level.
/// </para>
/// </para>
/// </summary>
public sealed class ItemTelemetryService(CompanionDbContext db, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>
    /// The one state selection reads. A quarantined item is never evaluated again.
    /// <para>
    /// #255 item 4, stated rather than left implied: this filter stays <c>active</c>-only, and that is
    /// correct. Telemetry's question is "how did learners do on the stock we serve", so it has nothing
    /// to say about a <c>validated</c> item - nobody was served it, and an evaluation over zero
    /// administrations would be a row claiming evidence it does not have. The edge that acts on that
    /// question is <c>active -> quarantined</c>, which also only means something once an item is live.
    /// </para>
    /// </summary>
    private const string ActiveLifecycle = "active";
    private const string QuarantinedLifecycle = "quarantined";

    /// <summary>
    /// D0.7's schedule: evaluate every active item whose administration count has reached a checkpoint
    /// it has not been evaluated at. Re-running this at the same counts writes nothing, because the
    /// checkpoint a row answers is part of its primary key.
    /// </summary>
    public async Task<ItemTelemetryRunResult> EvaluateActiveItemsAsync(CancellationToken cancellationToken = default)
    {
        var items = await db.Items
            .AsNoTracking()
            .Where(i => i.LifecycleStatus == ActiveLifecycle)
            .Select(i => new { i.ItemId, i.CefrLevel, i.Modality, i.TopicId })
            .ToListAsync(cancellationToken);

        if (items.Count == 0)
        {
            return new ItemTelemetryRunResult([]);
        }

        var itemIds = items.Select(i => i.ItemId).ToList();

        // The version each item currently is. An answer pins a version, so the observations are read
        // per version and attributed back to the item: telemetry follows the item, never a version.
        var versionsByItem = (await db.ItemVersions
                .AsNoTracking()
                .Where(v => itemIds.Contains(v.ItemId))
                .Select(v => new { v.ItemId, v.ItemVersionId, v.VersionNumber, v.KeyLabel, v.OptionA })
                .ToListAsync(cancellationToken))
            .GroupBy(v => v.ItemId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var versionToItem = versionsByItem
            .SelectMany(kv => kv.Value.Select(v => (v.ItemVersionId, ItemId: kv.Key)))
            .ToDictionary(x => x.ItemVersionId, x => x.ItemId);

        var versionIds = versionToItem.Keys.ToList();

        var practice = await LoadPracticeObservationsAsync(versionToItem, cancellationToken);
        var attempts = await LoadAttemptObservationsAsync(versionToItem, cancellationToken);
        var productiveBands = await LoadProductiveBandsAsync(versionToItem, cancellationToken);

        var stored = (await db.ItemTelemetryEvaluations
                .AsNoTracking()
                .Where(e => itemIds.Contains(e.ItemId))
                .Select(e => new { e.ItemId, e.CheckpointSize })
                .ToListAsync(cancellationToken))
            .Select(e => (e.ItemId, e.CheckpointSize))
            .ToHashSet();

        var outcomes = new List<ItemTelemetryOutcome>();
        var toQuarantine = new List<int>();

        foreach (var item in items)
        {
            if (!versionsByItem.TryGetValue(item.ItemId, out var versions) || versions.Count == 0)
            {
                continue;
            }

            // An item's key is the same across its versions unless the item was re-versioned with a new
            // key, which is a copy-on-write event and therefore a different item in every sense that
            // matters here. The newest version's key is what the live item is.
            var keyLabel = GateContext.Newest(versions, v => v.VersionNumber)!.KeyLabel;

            var observations = practice.GetValueOrDefault(item.ItemId, [])
                .Concat(attempts.GetValueOrDefault(item.ItemId, []))
                .ToList();

            if (productiveBands.TryGetValue(item.ItemId, out var bands))
            {
                observations.AddRange(bands);
            }

            if (observations.Count == 0)
            {
                continue;
            }

            var verdict = ItemTelemetry.Evaluate(item.CefrLevel, keyLabel, observations);
            if (!verdict.Evaluated)
            {
                // Below the first checkpoint there is no row to write: an item with 29 answers has not
                // been evaluated, and recording that would be evidence of nothing.
                outcomes.Add(new ItemTelemetryOutcome(item.ItemId, verdict, NewlyRecorded: false, QuarantinedNow: false));
                continue;
            }

            var checkpoint = ItemTelemetry.ReachedCheckpoint(verdict.Administrations);
            if (checkpoint is not { } size)
            {
                outcomes.Add(new ItemTelemetryOutcome(item.ItemId, verdict, NewlyRecorded: false, QuarantinedNow: false));
                continue;
            }

            if (stored.Contains((item.ItemId, size)))
            {
                // Idempotent: this checkpoint was answered on an earlier run. Nothing is written, so a
                // schedule that runs twice cannot double-count or flip the item twice.
                outcomes.Add(new ItemTelemetryOutcome(item.ItemId, verdict, NewlyRecorded: false, QuarantinedNow: false));
                continue;
            }

            db.ItemTelemetryEvaluations.Add(new ItemTelemetryEvaluation
            {
                ItemId = item.ItemId,
                CheckpointSize = size,
                AdministrationCount = verdict.Administrations,
                TargetBandCount = verdict.TargetBandAdministrations,
                Discrimination = verdict.Discrimination is { } r ? Math.Round((decimal)r, 6) : null,
                TargetFacility = verdict.TargetFacility is { } p ? Math.Round((decimal)p, 6) : null,
                MeanResponseBand = verdict.MeanResponseBand is { } m ? Math.Round((decimal)m, 6) : null,
                BreachDiscrimination = verdict.Breaches.Contains(ItemTelemetry.BreachDiscrimination),
                BreachFacility = verdict.Breaches.Contains(ItemTelemetry.BreachFacility),
                BreachDistractorDominance = verdict.Breaches.Contains(ItemTelemetry.BreachDistractorDominance),
                BreachProductiveDrift = verdict.Breaches.Contains(ItemTelemetry.BreachProductiveDrift),
                Quarantined = verdict.ShouldQuarantine,
                EvaluatedAt = _clock.GetUtcNow(),
            });

            if (verdict.ShouldQuarantine)
            {
                toQuarantine.Add(item.ItemId);
            }

            outcomes.Add(new ItemTelemetryOutcome(
                item.ItemId, verdict, NewlyRecorded: true, QuarantinedNow: verdict.ShouldQuarantine));
        }

        if (toQuarantine.Count > 0)
        {
            // `active -> quarantined` is one of the transitions trg_Items_LifecycleTransitions names,
            // so this write is legal; the database is still the authority on that.
            var itemsToFlip = await db.Items
                .Where(i => toQuarantine.Contains(i.ItemId) && i.LifecycleStatus == ActiveLifecycle)
                .ToListAsync(cancellationToken);

            foreach (var item in itemsToFlip)
            {
                item.LifecycleStatus = QuarantinedLifecycle;
            }
        }

        if (outcomes.Any(o => o.NewlyRecorded))
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return new ItemTelemetryRunResult(outcomes);
    }

    // ---- observation sources --------------------------------------------------------------------

    /// <summary>
    /// Pool drill answers. The sitting's own correct count is the "total test score" the point-biserial
    /// correlation needs; a cell drill is the instrument the learner actually sat.
    /// </summary>
    private async Task<Dictionary<int, List<ItemObservation>>> LoadPracticeObservationsAsync(
        IReadOnlyDictionary<int, int> versionToItem, CancellationToken cancellationToken)
    {
        var ids = versionToItem.Keys.ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var answers = await db.PracticeAnswers
            .AsNoTracking()
            .Where(a => a.ItemVersionId != null && ids.Contains(a.ItemVersionId.Value))
            .Select(a => new
            {
                a.ItemVersionId,
                a.SelectedLabel,
                a.IsCorrect,
                // A pool answer is an administration of its item whether the drill finished or not, so
                // the session's status is deliberately not filtered. What makes it an administration is
                // that a choice or a response was recorded, which the Where clause above enforces.
                SittingTotal = (double)a.Session.CorrectCount,
                // #210 is open: the learner's measured level does not exist yet, so target band is the
                // level of the cell they chose (see the class summary).
                AtTargetBand = a.Session.CefrLevel == a.ItemVersion!.Item.CefrLevel,
            })
            .ToListAsync(cancellationToken);

        var byItem = new Dictionary<int, List<ItemObservation>>();
        foreach (var answer in answers)
        {
            var itemId = versionToItem[answer.ItemVersionId!.Value];
            if (!byItem.TryGetValue(itemId, out var list))
            {
                list = [];
                byItem[itemId] = list;
            }

            list.Add(new ItemObservation(
                answer.SelectedLabel,
                answer.IsCorrect,
                answer.SittingTotal,
                answer.AtTargetBand,
                ResponseBand: null));
        }

        return byItem;
    }

    /// <summary>
    /// Paper answers. Only a graded attempt is an administration: an in-progress sitting's rows exist
    /// before the learner has chosen anything, and counting them would put every item at 0% facility
    /// from the moment its paper was created.
    /// </summary>
    private async Task<Dictionary<int, List<ItemObservation>>> LoadAttemptObservationsAsync(
        IReadOnlyDictionary<int, int> versionToItem, CancellationToken cancellationToken)
    {
        var ids = versionToItem.Keys.ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var answers = await db.AttemptAnswers
            .AsNoTracking()
            .Where(a => a.ItemVersionId != null
                && ids.Contains(a.ItemVersionId.Value)
                && a.Attempt.Status == "graded"
                && a.SelectedLabel != null)
            .Select(a => new
            {
                a.ItemVersionId,
                a.SelectedLabel,
                a.IsCorrect,
                SittingTotal = (double)(a.Attempt.TotalScore ?? 0),
                // #210 is open, so a learner's measured level does not exist. A paper is not drawn at a
                // learner's level either — it is a fixed form — so there is no honest per-learner band to
                // compare against yet. Until the skill state exists this arm treats every graded paper
                // answer as target-band, which makes the facility of a paper-sourced item the paper's own
                // facility. That is a real, named limit: it over-counts a paper's hard items as having
                // been sat by learners of the item's level.
                AtTargetBand = true,
            })
            .ToListAsync(cancellationToken);

        var byItem = new Dictionary<int, List<ItemObservation>>();
        foreach (var answer in answers)
        {
            var itemId = versionToItem[answer.ItemVersionId!.Value];
            if (!byItem.TryGetValue(itemId, out var list))
            {
                list = [];
                byItem[itemId] = list;
            }

            list.Add(new ItemObservation(
                answer.SelectedLabel,
                answer.IsCorrect,
                answer.SittingTotal,
                answer.AtTargetBand,
                ResponseBand: null));
        }

        return byItem;
    }

    /// <summary>
    /// A productive task's AI band, read from the stored rubric marks: D0.6 defines the composite as the
    /// unweighted mean of the four criterion integers 0-6, and <c>AiGradingScores.Score</c> holds exactly
    /// those integers.
    /// <para>
    /// The join runs through the paper binding, because a <c>FreeResponse</c> names a legacy
    /// <c>Question</c> and never an item version — <c>ExamItems</c> is the only stored link between a
    /// paper position and a pool version. The position is the 1-based rank of the question within
    /// (exam, section), which is the same resolution <c>AttemptEngine.PlaceBindings</c> uses; there is
    /// no other stored ordering to join on.
    /// </para>
    /// <para>
    /// A productive task practised outside a paper therefore has no bindable position and contributes no
    /// band, rather than a band of zero — a zero would read as "every learner scored Pre-A1" and
    /// quarantine a healthy task.
    /// </para>
    /// </summary>
    private async Task<Dictionary<int, List<ItemObservation>>> LoadProductiveBandsAsync(
        IReadOnlyDictionary<int, int> versionToItem, CancellationToken cancellationToken)
    {
        var ids = versionToItem.Keys.ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var bindings = await db.ExamItems
            .AsNoTracking()
            .Where(b => ids.Contains(b.ItemVersionId))
            .Select(b => new { b.ExamId, b.SectionId, b.DisplayOrder, b.ItemVersionId })
            .ToListAsync(cancellationToken);

        if (bindings.Count == 0)
        {
            return [];
        }

        var examIds = bindings.Select(b => b.ExamId).Distinct().ToList();

        // The papers' questions in sitting order, so a position is the rank AttemptEngine assigns.
        var questions = await db.Questions
            .AsNoTracking()
            .Where(q => examIds.Contains(q.ExamId))
            .OrderBy(q => q.Section.Skill.DisplayOrder)
            .ThenBy(q => q.Section.DisplayOrder)
            .ThenBy(q => q.Group.DisplayOrder)
            .ThenBy(q => q.DisplayOrder)
            .Select(q => new { q.QuestionId, q.ExamId, q.SectionId })
            .ToListAsync(cancellationToken);

        var positionOf = new Dictionary<(int ExamId, int SectionId, int QuestionId), int>();
        var nextRank = new Dictionary<(int, int), int>();
        foreach (var question in questions)
        {
            var section = (question.ExamId, question.SectionId);
            var rank = nextRank.GetValueOrDefault(section) + 1;
            nextRank[section] = rank;
            positionOf[(question.ExamId, question.SectionId, question.QuestionId)] = rank;
        }

        var versionAtPosition = bindings
            .GroupBy(b => (b.ExamId, b.SectionId, b.DisplayOrder))
            .ToDictionary(g => g.Key, g => g.First().ItemVersionId);

        // One grouped read of every finished grading whose question sits on a bound position.
        var graded = await db.AiGradings
            .AsNoTracking()
            .Where(g => g.Status == AiGradingStatus.Done.ToCode())
            .Select(g => new
            {
                QuestionId = g.FreeResponse.QuestionId,
                ExamId = g.FreeResponse.Question.ExamId,
                SectionId = g.FreeResponse.Question.SectionId,
                SittingTotal = (double)(g.FreeResponse.Attempt.TotalScore ?? 0),
                MeanBand = g.AiGradingScores.Average(s => (double?)s.Score),
            })
            .Where(g => g.MeanBand != null)
            .ToListAsync(cancellationToken);

        var byItem = new Dictionary<int, List<ItemObservation>>();
        foreach (var grading in graded)
        {
            if (!positionOf.TryGetValue((grading.ExamId, grading.SectionId, grading.QuestionId), out var position))
            {
                continue;
            }

            if (!versionAtPosition.TryGetValue((grading.ExamId, grading.SectionId, position), out var versionId))
            {
                continue;
            }

            if (!versionToItem.TryGetValue(versionId, out var itemId))
            {
                continue;
            }

            if (!byItem.TryGetValue(itemId, out var list))
            {
                list = [];
                byItem[itemId] = list;
            }

            // A productive response has no correct/incorrect; its band is the observation.
            list.Add(new ItemObservation(
                SelectedLabel: null,
                IsCorrect: null,
                grading.SittingTotal,
                TargetBand: true,
                ResponseBand: grading.MeanBand!.Value));
        }

        return byItem;
    }

    // ---- CNT-45: monotonic learner protection ---------------------------------------------------

    /// <summary>
    /// D0.7 / CNT-45: a quarantined item confirmed defective is excluded from every historical sitting
    /// and each affected attempt is rescored. <b>No learner is ever downgraded</b>: a recalculation that
    /// would lower a band leaves the learner exactly where they were, because a content defect is not
    /// evidence about a learner.
    /// <para>
    /// <paramref name="trueKeyLabel"/> is the corrected key when the defect was a key error — CNT-45's
    /// "credited if the learner selected the true key". Null means the item is unsolvable (a broken
    /// stimulus) and it is dropped from the denominator instead.
    /// </para>
    /// </summary>
    public async Task<TelemetryRecalculationResult> RecalculateForDefectiveItemAsync(
        int itemId, string? trueKeyLabel, CancellationToken cancellationToken = default)
    {
        var versionIds = await db.ItemVersions
            .AsNoTracking()
            .Where(v => v.ItemId == itemId)
            .Select(v => v.ItemVersionId)
            .ToListAsync(cancellationToken);

        if (versionIds.Count == 0)
        {
            return new TelemetryRecalculationResult([]);
        }

        var attemptIds = await db.AttemptAnswers
            .AsNoTracking()
            .Where(a => a.ItemVersionId != null
                && versionIds.Contains(a.ItemVersionId.Value)
                && a.Attempt.Status == "graded")
            .Select(a => a.AttemptId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var outcomes = new List<AttemptRecalculationOutcome>();

        foreach (var attemptId in attemptIds)
        {
            var outcome = await RecalculateAttemptAsync(attemptId, versionIds, trueKeyLabel, cancellationToken);
            if (outcome is not null)
            {
                outcomes.Add(outcome);
            }
        }

        return new TelemetryRecalculationResult(outcomes);
    }

    private async Task<AttemptRecalculationOutcome?> RecalculateAttemptAsync(
        int attemptId, IReadOnlyCollection<int> defectiveVersionIds, string? trueKeyLabel,
        CancellationToken cancellationToken)
    {
        var attempt = await db.ExamAttempts
            .FirstOrDefaultAsync(a => a.AttemptId == attemptId, cancellationToken);

        if (attempt is null || attempt.Status != "graded")
        {
            return null;
        }

        var answers = await db.AttemptAnswers
            .Where(a => a.AttemptId == attemptId)
            .Include(a => a.Question).ThenInclude(q => q.Section).ThenInclude(s => s.Skill)
            .ToListAsync(cancellationToken);

        var affected = answers.Where(a => a.ItemVersionId is int v && defectiveVersionIds.Contains(v)).ToList();
        if (affected.Count == 0 || affected.All(a => a.SelectedLabel is null))
        {
            // An unanswered defective position derives nothing: the learner made no claim about it.
            return null;
        }

        var skillIds = answers.Select(a => a.Question.Section.SkillId).Distinct().ToList();
        var bands = await db.ScoreBands
            .AsNoTracking()
            .Where(b => b.CertId == attempt.CertId)
            .OrderBy(b => b.MinTotal)
            .ToListAsync(cancellationToken);

        var previousBand = attempt.BandCode;
        var previousTotal = attempt.TotalScore ?? 0;

        // ---- recompute, crediting the true key or excluding the item -----------------------------
        // #251: every number is the engine's. Scaling is its ComputeScaledScoreAsync and the rule its
        // AggregateTotalScore, so a certificate with a rule the engine refuses is refused here too
        // instead of being summed by a second, weaker copy.
        var engine = new AttemptEngine(db);
        var newScores = new List<AttemptSkillScore>();
        foreach (var skillId in skillIds)
        {
            var skillAnswers = answers.Where(a => a.Question.Section.SkillId == skillId).ToList();

            var correct = 0;
            var denominator = 0;
            foreach (var answer in skillAnswers)
            {
                var isDefective = answer.ItemVersionId is int v && defectiveVersionIds.Contains(v);

                if (!isDefective)
                {
                    denominator++;
                    if (answer.IsCorrect == true)
                    {
                        correct++;
                    }

                    continue;
                }

                // CNT-45, the two named treatments. A key error credits a learner who chose the true
                // key; an unsolvable item leaves the denominator entirely. Never a zero for a question
                // the learner could not have got right.
                if (trueKeyLabel is not null)
                {
                    denominator++;
                    if (string.Equals(answer.SelectedLabel, trueKeyLabel, StringComparison.OrdinalIgnoreCase))
                    {
                        correct++;
                    }
                }
            }

            if (denominator == 0)
            {
                continue;
            }

            newScores.Add(new AttemptSkillScore
            {
                AttemptId = attemptId,
                SkillId = skillId,
                CertId = attempt.CertId,
                RawScore = correct,
                ScaledScore = await engine.ComputeScaledScoreAsync(
                    attempt.CertId, skillId, attempt.ExamId, correct, denominator, cancellationToken),
            });
        }

        if (newScores.Count == 0)
        {
            return null;
        }

        var rule = await db.Certificates
            .AsNoTracking()
            .Where(c => c.CertId == attempt.CertId)
            .Select(c => c.ScoreAggregationRule)
            .SingleAsync(cancellationToken);

        var total = AttemptEngine.AggregateTotalScore(rule, newScores);
        var recalculatedBand = AttemptEngine.MatchBandCode(total, bands);

        var previousRank = BandRank(bands, previousBand);
        var newRank = BandRank(bands, recalculatedBand);

        // ---- the rule that cannot be got wrong ---------------------------------------------------
        // A recalculation that does not lift the band writes nothing. `>` and not `>=`: an equal band is
        // not an upgrade, and writing it would rewrite a learner's history for no gain.
        if (newRank <= previousRank)
        {
            return new AttemptRecalculationOutcome(
                attemptId, previousBand, recalculatedBand, previousTotal, total, Outcome: "kept");
        }

        // ---- write: through the fact's owner -----------------------------------------------------
        // #251: the three statements and the declaration are AttemptEngine's, because it is the
        // registry's owner of `attempt_score_and_band`. Writing them here was a second writer of a
        // derived fact - the confirmed defect #191 exists to prevent - and it re-derived the rule.
        await engine.WriteAttemptScoreAsync(attemptId, newScores, cancellationToken);

        return new AttemptRecalculationOutcome(
            attemptId, previousBand, recalculatedBand, previousTotal, total, Outcome: "upgraded");
    }

    /// <summary>
    /// The band's ordering position, by the certificate's own lower bound. An unknown or absent band
    /// sorts below every real one, so "no band" can never beat "a band" and an unrecognised code cannot
    /// masquerade as an upgrade.
    /// </summary>
    private static int BandRank(IReadOnlyList<ScoreBand> bands, string? code)
    {
        if (code is null)
        {
            return int.MinValue;
        }

        var band = bands.FirstOrDefault(b => string.Equals(b.Code, code, StringComparison.OrdinalIgnoreCase));
        return band?.MinTotal ?? int.MinValue;
    }
}

/// <summary>#199: one run of the schedule over every active item.</summary>
public sealed record ItemTelemetryRunResult(IReadOnlyList<ItemTelemetryOutcome> Outcomes)
{
    /// <summary>Items whose checkpoint row was written by this run. Zero on a re-run.</summary>
    public IReadOnlyList<ItemTelemetryOutcome> NewlyEvaluated =>
        Outcomes.Where(o => o.NewlyRecorded).ToList();

    /// <summary>Items this run flipped to <c>quarantined</c>.</summary>
    public IReadOnlyList<ItemTelemetryOutcome> Quarantined =>
        Outcomes.Where(o => o.QuarantinedNow).ToList();
}

/// <summary>
/// #199: what one item's evaluation said and what was done about it.
/// <see cref="NewlyRecorded"/> is false when the checkpoint was already answered, so a caller can tell
/// "nothing to do" from "evaluated and clean".
/// </summary>
public sealed record ItemTelemetryOutcome(
    int ItemId,
    ItemTelemetryVerdict Verdict,
    bool NewlyRecorded,
    bool QuarantinedNow);

/// <summary>#199 / CNT-45: the recalculation over every attempt that answered a confirmed-defective item.</summary>
public sealed record TelemetryRecalculationResult(IReadOnlyList<AttemptRecalculationOutcome> Attempts)
{
    public IReadOnlyList<AttemptRecalculationOutcome> Upgraded =>
        Attempts.Where(a => a.Outcome == "upgraded").ToList();

    public IReadOnlyList<AttemptRecalculationOutcome> Kept =>
        Attempts.Where(a => a.Outcome == "kept").ToList();
}

/// <summary>
/// #199 / CNT-45: one attempt's recalculation. <see cref="RecalculatedBand"/> is what the arithmetic
/// produced, and <see cref="Outcome"/> is what was written: <c>upgraded</c> when the band rose,
/// <c>kept</c> when it did not. A <c>kept</c> row whose recalculated band is lower is the monotonic rule
/// working, not a failure.
/// </summary>
public sealed record AttemptRecalculationOutcome(
    int AttemptId,
    string? PreviousBand,
    string? RecalculatedBand,
    int PreviousTotal,
    int RecalculatedTotal,
    string Outcome);
