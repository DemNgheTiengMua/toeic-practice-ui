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

/// <summary>
/// #226 / ADM-12: the six publish invariants, evaluated over the rows a paper actually binds.
/// <para>
/// <b>Why this is its own file.</b> <c>AdminExamService.Audit.cs</c> already carries ~26 authoring
/// checks - stems, options, keys, audio slices, rubric criteria per question. Those judge a paper's
/// <em>fitness as content</em>. The six here judge something different: whether a paper assembled from
/// the warehouse may go live, which is what ADM-12 states and what the ticket calls the publish gate.
/// Keeping them apart means the six can be counted and named individually, which is what the rail and
/// the refusal both need.
/// </para>
/// <para>
/// <b>Every check is a query, and every check names its deficit.</b> "Cannot publish" is the failure
/// the ticket forbids: each refusal carries the section, item or skill that failed, and the element
/// target the rail jumps to.
/// </para>
/// <para>
/// <b>What already existed, and is therefore called rather than re-implemented.</b>
/// <list type="bullet">
/// <item>INV-1's "exactly the number of active items it declares" reuses the count the audit already
/// takes; it adds only the skill-coverage half, which nothing checked.</item>
/// <item>INV-2's envelope, span-independence and audio-gap rules are <see cref="GateFour"/>'s pure
/// functions, reached through <see cref="PoolGateFour.ReviewBundleAsync"/> exactly as ingestion reaches
/// them. Re-writing the IoU or the 8.0 s gap here would be a second answer to one question.</item>
/// <item>INV-3's "active" is what every pool reader filters on, and <c>validated -> active</c> is
/// #205's <c>ClearForServiceAsync</c> and #206's <c>DecideAsync</c>. Both write a
/// <see cref="ReviewSampleDecision"/> with the <c>clear</c> verb, which is the stored evidence a human
/// cleared the item - and <c>trg_ReviewSampleDecisions_NoSelfApproval</c> makes that evidence
/// unforgeable by its own author. This check reads that record; it does not create a second promotion
/// path.</item>
/// <item>INV-4's rubric shape is <c>trg_GradingRubricLocks_CompleteCanonicalRubric</c> and the ladder
/// verdict is <c>GradingRubricLocks.LadderOutcome</c> (#208). This checks the link the ticket says is
/// missing: that an <em>enabled productive skill</em> has a lock at all, and that the criteria every
/// bound task is graded against carry the three anchors.</item>
/// <item>INV-6 is <c>trg_ExamItems_ProductionBindsProduction</c> plus <c>trg_Exams_TierKeepsContent</c>.
/// The check below reports it on the rail; it does not refuse what the trigger already refuses.</item>
/// </list>
/// </para>
/// </summary>
public sealed partial class AdminExamService
{
    /// <summary>
    /// The six invariants, each with the deficits it found. Fails closed: a check that cannot run
    /// (no section, no clearance record) is a deficit, never a pass.
    /// </summary>
    private async Task<(List<ExamAuditIssueDto> Issues, List<ExamInvariantDto> Invariants)> EvaluateInvariantsAsync(
        Exam exam,
        List<Section> sections,
        CancellationToken cancellationToken)
    {
        var issues = new List<ExamAuditIssueDto>();

        // One read of everything the six need. Sections are certificate-scoped (a section is not owned
        // by an exam), so which of them this paper serves is decided by whether it holds anything for
        // THIS paper.
        var skillRows = await db.Skills.AsNoTracking()
            .Where(s => s.CertId == exam.CertId)
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);

        var bindings = await db.ExamItems.AsNoTracking()
            .Where(b => b.ExamId == exam.ExamId)
            .Join(db.Items.AsNoTracking(), b => b.ItemId, i => i.ItemId, (b, i) => new { Binding = b, Item = i })
            .ToListAsync(cancellationToken);

        var legacyCounts = await db.Questions.AsNoTracking()
            .Where(q => q.ExamId == exam.ExamId)
            .GroupBy(q => q.SectionId)
            .Select(g => new { SectionId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var legacyBySection = legacyCounts.ToDictionary(x => x.SectionId, x => x.Count);

        var sectionsBySkill = sections.GroupBy(s => s.SkillId).ToDictionary(g => g.Key, g => g.ToList());

        // ---- INV-1: every enabled skill has a section, every section holds what it declares ----------
        var inv1 = new List<ExamAuditIssueDto>();

        // What a section HOLDS is the number of positions the sitting renders, and that is the legacy
        // `Questions` count - not the union with `ExamItems`. AttemptEngine.PlaceBindings places each
        // binding on the legacy question at its position, and EnsureBindingsRender refuses an attempt
        // when a binding has no position to render into. So a binding supplies the CONTENT for a
        // rendered position; it does not add one. Counting the union would report 2 for a one-question
        // section that carries a binding, which is a number no learner would ever see.
        //
        // A binding with no rendered position is therefore its own deficit, named separately below:
        // it is stock on the paper that no sitting can reach.
        foreach (var skill in skillRows)
        {
            if (!sectionsBySkill.TryGetValue(skill.SkillId, out var skillSections) || skillSections.Count == 0)
            {
                var msg = $"INV-1: the enabled skill [{skill.Code}] '{skill.Name}' has no section, so the paper cannot serve it.";
                inv1.Add(new ExamAuditIssueDto(
                    ExamInvariants.SkillCoverage, "error", msg,
                    ElementTarget: $"rail-skill-group-{skill.SkillId}"));
                continue;
            }

            foreach (var sec in skillSections)
            {
                var declared = sec.ExpectedQuestionCount;
                var rendered = legacyBySection.GetValueOrDefault(sec.SectionId);

                if (rendered != declared)
                {
                    var msg = rendered < declared
                        ? $"INV-1: section [{sec.Code}] '{sec.Name}' declares {declared} item(s) but holds {rendered} ({declared - rendered} short)."
                        : $"INV-1: section [{sec.Code}] '{sec.Name}' declares {declared} item(s) but holds {rendered} ({rendered - declared} over its blueprint).";
                    inv1.Add(new ExamAuditIssueDto(
                        ExamInvariants.SkillCoverage, "error", msg,
                        SectionId: sec.SectionId, SectionCode: sec.Code,
                        ElementTarget: $"section-link-{sec.SectionId}"));
                }

                // A binding beyond the rendered positions is stock the sitting cannot reach, because
                // EnsureBindingsRender refuses the attempt rather than serving a paper with a hole.
                foreach (var orphan in bindings.Where(b =>
                             b.Binding.SectionId == sec.SectionId && b.Binding.DisplayOrder > rendered))
                {
                    var msg = $"INV-1: section [{sec.Code}] binds item {orphan.Item.ItemId} at position {orphan.Binding.DisplayOrder}, but the section renders only {rendered} position(s). A binding with no position behind it is stock the sitting cannot reach.";
                    inv1.Add(new ExamAuditIssueDto(
                        ExamInvariants.SkillCoverage, "error", msg,
                        SectionId: sec.SectionId, SectionCode: sec.Code,
                        ElementTarget: $"assembly-position-{sec.SectionId}-{orphan.Binding.DisplayOrder}"));
                }
            }
        }

        // ---- INV-2: every bound stimulus carries its complete child group -----------------------------
        var inv2 = new List<ExamAuditIssueDto>();

        var stimulusIds = bindings
            .Where(b => b.Item.StimulusId is not null)
            .Select(b => b.Item.StimulusId!.Value)
            .Distinct()
            .ToList();

        foreach (var stimulusId in stimulusIds)
        {
            // The gates' own arithmetic, called and never copied: PoolGateFour assembles the arguments
            // from the pool (siblings share a StimulusId; the newest version's span is the one served)
            // and GateFour owns the IoU, the 8.0 s gap and the ordering.
            var refusals = await PoolGateFour.ReviewBundleAsync(db, stimulusId, cancellationToken);
            foreach (var refusal in refusals)
            {
                var msg = $"INV-2: stimulus {stimulusId} does not carry a complete child group - {refusal.Message}";
                inv2.Add(new ExamAuditIssueDto(
                    ExamInvariants.StimulusGroup, "error", msg,
                    ElementTarget: $"stimulus-group-{stimulusId}"));
            }
        }

        // ---- INV-3: every bound item is active and cleared ---------------------------------------------
        var inv3 = new List<ExamAuditIssueDto>();

        // The clearance record. `clear` is the verb both promotion paths write (#205 ClearForServiceAsync
        // and #206 DecideAsync), so one item cleared is one row naming it. `adjudicate` is the
        // quarantine verb and is deliberately not a clearance.
        var clearedItemIds = (await db.ReviewSampleDecisions.AsNoTracking()
                .Where(d => d.Decision == "clear")
                .Select(d => d.ItemId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        // The approved strata, keyed by the batch-cell an item belongs to. ImportLog is the only record
        // of which item a record became (#205 added ItemId for exactly that); Items.BatchTag is the
        // FILE's own tag, so two uploads of one file collide on it and a file declaring none matches
        // nothing - the same reason trg_ReviewSampleItems_ItemIsInItsBatch reads ImportLog.
        var batchesByItem = (await db.ImportLogs.AsNoTracking()
                .Where(l => l.ItemId != null)
                .Select(l => new { ItemId = l.ItemId!.Value, l.BatchId })
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.ItemId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.BatchId).Distinct().ToList());

        var strataByItem = await (
                from log in db.ImportLogs.AsNoTracking()
                where log.ItemId != null
                join stratum in db.ReviewStrata.AsNoTracking() on log.BatchId equals stratum.BatchId
                join item in db.Items.AsNoTracking() on log.ItemId equals item.ItemId
                where stratum.TopicId == item.TopicId && stratum.CefrLevel == item.CefrLevel
                select new { ItemId = log.ItemId!.Value, stratum.Status, stratum.BatchId })
            .ToListAsync(cancellationToken);

        var settlementByItem = strataByItem
            .GroupBy(s => s.ItemId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var binding in bindings)
        {
            var item = binding.Item;
            var target = $"assembly-position-{binding.Binding.SectionId}-{binding.Binding.DisplayOrder}";

            if (!string.Equals(item.LifecycleStatus, "active", StringComparison.Ordinal))
            {
                var msg = $"INV-3: item {item.ItemId} in section [{binding.Binding.SectionId}] is '{item.LifecycleStatus}', not 'active'. Only an active item is served to a learner, so binding it would put a hole on the paper.";
                inv3.Add(new ExamAuditIssueDto(
                    ExamInvariants.ItemClearance, "error", msg,
                    SectionId: binding.Binding.SectionId, QuestionId: null, ElementTarget: target));
                continue;
            }

            // THE NO-STRATUM CASE, decided explicitly rather than left to an `All` over an empty set.
            //
            // A stratum is a cell of a BATCH. An item that arrived in no batch (hand-authored through
            // AdminItemService, or seeded directly) therefore has no stratum at all - and "no stratum"
            // is not "cleared". A check written as `settlements.All(s => s.Status == "approved")` returns
            // true over an empty collection, so the naive version passes every hand-authored item by
            // never asking about any of them. That is the defect the ticket predicts, and this is where
            // it is refused.
            //
            // Two clearance shapes count, and they are the two the lifecycle actually writes:
            //   (a) its batch-cell stratum is 'approved' - #206's promotion of the whole cell, or
            //   (b) a 'clear' decision names the item - #205's one-at-a-time clearance, which is the
            //       path a reviewer uses before any stratum exists.
            // An item with neither is refused, and the sentence says which of the two it lacks. An item
            // fed by no batch can satisfy neither, so the direct-authoring route has no publish path
            // until its stock goes through a batch - reported, not worked around here.
            var settlements = settlementByItem.GetValueOrDefault(item.ItemId);
            var batches = batchesByItem.GetValueOrDefault(item.ItemId);
            var approvedByStratum = settlements is not null
                && settlements.Any(s => string.Equals(s.Status, "approved", StringComparison.Ordinal));
            var clearedByName = clearedItemIds.Contains(item.ItemId);

            // A batched item is judged by the audit that drew it. A batch with NO stratum covering the
            // item's cell is a third case and a real hole: the stock arrived, and nobody ever drew a
            // sample of it, so `All(...)` over the empty set would call it cleared.
            if (batches is not null && settlements is null && !clearedByName)
            {
                var msg = $"INV-3: item {item.ItemId} arrived in batch {batches[0]} but no stratum covers its cell (topic {item.TopicId}, {item.CefrLevel.Code}), and no clearance names it. The stock arrived and nobody audited it.";
                inv3.Add(new ExamAuditIssueDto(
                    ExamInvariants.ItemClearance, "error", msg,
                    SectionId: binding.Binding.SectionId, ElementTarget: target));
                continue;
            }

            if (!approvedByStratum && !clearedByName)
            {
                var detail = settlements is null
                    ? "it arrived in no batch, so no stratum ever drew it"
                    : $"its stratum is {string.Join(", ", settlements.Select(s => $"batch {s.BatchId}: {s.Status}"))}";

                var msg = $"INV-3: item {item.ItemId} has not cleared a human audit - {detail}, and no clearance names it. A stratum that is pending or quarantined has not been decided, and an unbatched item was never drawn.";
                inv3.Add(new ExamAuditIssueDto(
                    ExamInvariants.ItemClearance, "error", msg,
                    SectionId: binding.Binding.SectionId, ElementTarget: target));
            }
        }

        // ---- INV-4: every enabled productive skill has a locked rubric, every criterion three anchors --
        var inv4 = new List<ExamAuditIssueDto>();

        var rubricLocks = await db.GradingRubricLocks.AsNoTracking()
            .Select(l => new { l.SkillId, l.LadderOutcome })
            .ToListAsync(cancellationToken);
        var lockBySkill = rubricLocks.ToDictionary(l => l.SkillId);

        foreach (var skill in skillRows.Where(s => s.Modality is "speaking" or "writing"))
        {
            // Only a skill the paper actually enables. A certificate that offers speaking but whose
            // paper examines reading alone does not need a speaking rubric to publish.
            if (!sectionsBySkill.ContainsKey(skill.SkillId))
            {
                continue;
            }

            if (!lockBySkill.TryGetValue(skill.SkillId, out var rubricLock))
            {
                var msg = $"INV-4: the enabled productive skill [{skill.Code}] '{skill.Name}' has no locked rubric, so a free response in it cannot be graded against a standard.";
                inv4.Add(new ExamAuditIssueDto(
                    ExamInvariants.RubricReady, "error", msg,
                    ElementTarget: $"rubric-lock-{skill.SkillId}"));
                continue;
            }

            // A lock whose ladder never ran is the state #208 closed at the lock door. A row written
            // before that ticket can still be read, so the gate names it instead of trusting it.
            if (!string.Equals(rubricLock.LadderOutcome, RubricLadderOutcomes.Accepted, StringComparison.Ordinal))
            {
                var msg = $"INV-4: the rubric for [{skill.Code}] '{skill.Name}' is locked without a passed ladder (outcome '{rubricLock.LadderOutcome ?? "not run"}'), so its bands were never shown to separate.";
                inv4.Add(new ExamAuditIssueDto(
                    ExamInvariants.RubricReady, "error", msg,
                    ElementTarget: $"rubric-lock-{skill.SkillId}"));
            }
        }

        // The anchors, for the criteria a bound productive task is graded against. Read per skill, not
        // per item: one rubric serves every task in the skill.
        var productiveSkillIds = bindings
            .Select(b => sections.FirstOrDefault(s => s.SectionId == b.Binding.SectionId))
            .Where(s => s is not null && s.Modality is "speaking" or "writing")
            .Select(s => s!.SkillId)
            .Distinct()
            .ToList();

        if (productiveSkillIds.Count > 0)
        {
            var criteria = await db.GradingCriteria.AsNoTracking()
                .Where(c => productiveSkillIds.Contains(c.SkillId))
                .Select(c => new { c.CriterionId, c.SkillId, c.Code })
                .ToListAsync(cancellationToken);
            var criterionIds = criteria.Select(c => c.CriterionId).ToList();

            var anchorBands = await db.GradingCriterionAnchors.AsNoTracking()
                .Where(a => criterionIds.Contains(a.CriterionId))
                .Select(a => new { a.CriterionId, a.Band })
                .ToListAsync(cancellationToken);

            var bandsByCriterion = anchorBands
                .GroupBy(a => a.CriterionId)
                .ToDictionary(g => g.Key, g => g.Select(a => (int)a.Band).ToHashSet());

            foreach (var skillId in productiveSkillIds)
            {
                var skill = skillRows.Single(s => s.SkillId == skillId);
                var skillCriteria = criteria.Where(c => c.SkillId == skillId).ToList();

                if (skillCriteria.Count == 0)
                {
                    var msg = $"INV-4: the enabled productive skill [{skill.Code}] has no grading criteria, so no bound task in it carries the three anchors.";
                    inv4.Add(new ExamAuditIssueDto(
                        ExamInvariants.RubricReady, "error", msg,
                        ElementTarget: $"rubric-lock-{skillId}"));
                    continue;
                }

                foreach (var criterion in skillCriteria)
                {
                    var bands = bandsByCriterion.GetValueOrDefault(criterion.CriterionId) ?? [];
                    var missing = new[] { 2, 3, 4 }.Where(b => !bands.Contains(b)).ToList();
                    if (missing.Count == 0)
                    {
                        continue;
                    }

                    var msg = $"INV-4: criterion '{criterion.Code}' of [{skill.Code}] is missing its anchor band(s) {string.Join(", ", missing)}. Every criterion carries three benchmark responses, at bands 2, 3 and 4.";
                    inv4.Add(new ExamAuditIssueDto(
                        ExamInvariants.RubricReady, "error", msg,
                        ElementTarget: $"rubric-lock-{skillId}"));
                }
            }
        }

        // ---- INV-5: section timers sum to the declared duration; the projection is monotonic ----------
        var inv5 = new List<ExamAuditIssueDto>();

        // "Section timers" is answerable two ways and the wrong one is silent. A Section's
        // TimerPolicyJson is a MODE (SECTION_SHARED, ITEM_FIXED), not a number of minutes, so no sum of
        // section rows can equal a duration. The only per-section durations in the schema are
        // Skills.DurationMinutes, and the sitting allocates its phases from exactly those
        // (AttemptEngine.BuildAttemptPhases). So the sum is over the skills this paper actually serves -
        // which is also what makes the check mean something for a two-skill paper drawn from a
        // four-skill certificate.
        var servedSkillIds = sections
            .Where(s => legacyBySection.GetValueOrDefault(s.SectionId) > 0
                || bindings.Any(b => b.Binding.SectionId == s.SectionId))
            .Select(s => s.SkillId)
            .Distinct()
            .ToList();

        if (servedSkillIds.Count > 0)
        {
            var servedSkills = skillRows.Where(s => servedSkillIds.Contains(s.SkillId)).ToList();

            var missingDuration = servedSkills.Where(s => s.DurationMinutes is null or <= 0).ToList();
            foreach (var skill in missingDuration)
            {
                var msg = $"INV-5: the served skill [{skill.Code}] '{skill.Name}' declares no duration, so the paper's timers cannot sum to anything.";
                inv5.Add(new ExamAuditIssueDto(
                    ExamInvariants.TimersAndProjection, "error", msg,
                    ElementTarget: $"rail-skill-group-{skill.SkillId}"));
            }

            if (missingDuration.Count == 0)
            {
                var sum = servedSkills.Sum(s => s.DurationMinutes!.Value);
                if (sum != exam.DurationMinutes)
                {
                    var names = string.Join(" + ", servedSkills.Select(s => $"{s.Code} {s.DurationMinutes}"));
                    var msg = $"INV-5: the served skills sum to {sum} minute(s) ({names}) but the paper declares {exam.DurationMinutes}. The sitting allocates its phases from these durations, so a learner would be cut off at a different point than the paper promises.";
                    inv5.Add(new ExamAuditIssueDto(
                        ExamInvariants.TimersAndProjection, "error", msg,
                        ElementTarget: "exam-duration"));
                }
            }
        }

        // The cut-score projection's monotonicity. The OVERLAP half is trg_CefrCutScores_NoOverlap's
        // and is not repeated. What a trigger cannot say is "the order of the bands rises with the
        // level", because that is a statement about a set the trigger never sees together - it sees one
        // version's rows at a time and is scoped to (cert, version, skill).
        if (servedSkillIds.Count > 0)
        {
            var latestVersion = await db.CefrCutScoreVersions.AsNoTracking()
                .Where(v => v.CertId == exam.CertId)
                .OrderByDescending(v => v.TableVersion)
                .Select(v => (int?)v.TableVersion)
                .FirstOrDefaultAsync(cancellationToken);

            if (latestVersion is int version)
            {
                var cuts = await db.CefrCutScores.AsNoTracking()
                    .Where(c => c.CertId == exam.CertId
                        && c.TableVersion == version
                        && servedSkillIds.Contains(c.SkillId)
                        && c.RowKind != "display_ceiling")
                    .Select(c => new { c.SkillId, c.CefrLevel, c.MinScaled, c.MaxScaled })
                    .ToListAsync(cancellationToken);

                foreach (var group in cuts.GroupBy(c => c.SkillId))
                {
                    var skill = skillRows.Single(s => s.SkillId == group.Key);

                    // A cut row that starts below the previous row's top means one score has two
                    // levels, which is the statement ADM-12's second half makes. Rows are ordered by the
                    // level ladder, not by their numbers: ordering by the numbers would make the
                    // comparison true by construction and catch nothing.
                    var ordered = group
                        .Select(c => new
                        {
                            Level = CefrLevel.TryParse(c.CefrLevel, out var level) ? level : CefrLevel.PreA1,
                            c.CefrLevel,
                            c.MinScaled,
                            c.MaxScaled,
                        })
                        .OrderBy(c => c.Level.Rank)
                        .ToList();

                    for (var index = 1; index < ordered.Count; index++)
                    {
                        var lower = ordered[index - 1];
                        var higher = ordered[index];

                        if (lower.MaxScaled >= higher.MinScaled)
                        {
                            var msg = $"INV-5: the cut-score projection for [{skill.Code}] is not monotonic - {higher.CefrLevel} starts at {higher.MinScaled} while {lower.CefrLevel} already reaches {lower.MaxScaled}. A score in that range would carry two levels.";
                            inv5.Add(new ExamAuditIssueDto(
                                ExamInvariants.TimersAndProjection, "error", msg,
                                ElementTarget: $"cut-scores-{skill.SkillId}"));
                        }
                    }
                }
            }
        }

        // ---- INV-6: a production exam binds no demo item ----------------------------------------------
        // The two triggers own this rule (trg_ExamItems_ProductionBindsProduction refuses the binding,
        // trg_Exams_TierKeepsContent refuses promoting a paper that already binds demo stock). The gate
        // reports it because the rail must show all six; it adds no second guard (ADR-0012).
        var inv6 = new List<ExamAuditIssueDto>();

        if (string.Equals(exam.ContentTier, ContentTiers.Production, StringComparison.Ordinal))
        {
            foreach (var binding in bindings.Where(b =>
                         string.Equals(b.Item.ContentTier, ContentTiers.Demo, StringComparison.Ordinal)))
            {
                var msg = $"INV-6: this production paper binds demo-tier item {binding.Item.ItemId} at position {binding.Binding.DisplayOrder}. Promote the item through review, or make the paper demo.";
                inv6.Add(new ExamAuditIssueDto(
                    ExamInvariants.TierIsolation, "error", msg,
                    SectionId: binding.Binding.SectionId,
                    ElementTarget: $"assembly-position-{binding.Binding.SectionId}-{binding.Binding.DisplayOrder}"));
            }
        }

        issues.AddRange(inv1);
        issues.AddRange(inv2);
        issues.AddRange(inv3);
        issues.AddRange(inv4);
        issues.AddRange(inv5);
        issues.AddRange(inv6);

        var invariants = new List<ExamInvariantDto>
        {
            Row(ExamInvariants.SkillCoverage,
                "Every enabled skill has a section; every section holds exactly what it declares", inv1),
            Row(ExamInvariants.StimulusGroup,
                "Every bound stimulus carries its complete child group", inv2),
            Row(ExamInvariants.ItemClearance,
                "Every bound item is active and cleared by a human audit", inv3),
            Row(ExamInvariants.RubricReady,
                "Every enabled productive skill has a locked rubric; every criterion its three anchors", inv4),
            Row(ExamInvariants.TimersAndProjection,
                "Section timers sum to the declared duration; the projection is monotonic", inv5),
            Row(ExamInvariants.TierIsolation,
                "A production paper binds no demo item", inv6),
        };

        return (issues, invariants);
    }

    private static ExamInvariantDto Row(string code, string title, List<ExamAuditIssueDto> deficits) =>
        new(code, title, deficits.Count == 0, deficits.Count, deficits.FirstOrDefault()?.ElementTarget);
}
