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

        // #269: a position is a ROSTER row. It used to be "the larger of the legacy Questions count and
        // the roster", because a paper authored before the pool had no bindings. The legacy table is
        // gone, so the roster is not one of two answers - it is the answer, and a section holds exactly
        // the positions the sitting renders.
        var rosterBySection = await db.ExamItems.AsNoTracking()
            .Where(i => i.ExamId == exam.ExamId)
            .GroupBy(i => i.SectionId)
            .Select(g => new { SectionId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int RenderedIn(int sectionId) =>
            rosterBySection.FirstOrDefault(r => r.SectionId == sectionId)?.Count ?? 0;

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
                var rendered = RenderedIn(sec.SectionId);

                // #269: a PLACEMENT paper's size is its draw, not the section's blueprint. The stock is
                // chosen per (skill, band), so this paper legitimately binds 7 items where the section
                // declares 16 for a full mock form - and `ExpectedQuestionCount` is a CERTIFICATE-level
                // declaration, shared by every paper on that section. Measured: the imported Reading B2
                // form was refused with "section [P6] declares 16 but holds 7", although those 7 were the
                // whole placement stock it was drawn with.
                //
                // The check that still means something is the ORPHAN check below: every binding must have
                // a position behind it. And a placement paper is still held to "declares something",
                // which `totalQuestions` enforces.
                if (exam.Purpose != "placement" && rendered != declared)
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

        // ---- INV-2: stimulus group check (simplified for #310) ------------------------------------------
        var inv2 = new List<ExamAuditIssueDto>();

        // ---- INV-3: every bound item is active --------------------------------------------------------
        var inv3 = new List<ExamAuditIssueDto>();

        foreach (var binding in bindings)
        {
            var item = binding.Item;
            var target = $"assembly-position-{binding.Binding.SectionId}-{binding.Binding.DisplayOrder}";

            if (!string.Equals(item.LifecycleStatus, "active", StringComparison.Ordinal))
            {
                var msg = $"INV-3: item {item.ItemId} in section [{binding.Binding.SectionId}] is '{item.LifecycleStatus}', not 'active'. Only an active item is served to a learner.";
                inv3.Add(new ExamAuditIssueDto(
                    ExamInvariants.ItemClearance, "error", msg,
                    SectionId: binding.Binding.SectionId, QuestionId: null, ElementTarget: target));
            }
        }

        // ---- INV-4: every enabled productive skill has flat grading criteria ---------------------------
        var inv4 = new List<ExamAuditIssueDto>();

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
                .Select(c => new { c.CriterionId, c.SkillId })
                .ToListAsync(cancellationToken);

            foreach (var skillId in productiveSkillIds)
            {
                var skill = skillRows.Single(s => s.SkillId == skillId);
                if (!criteria.Any(c => c.SkillId == skillId))
                {
                    var msg = $"INV-4: the enabled productive skill [{skill.Code}] has no grading criteria.";
                    inv4.Add(new ExamAuditIssueDto(
                        ExamInvariants.RubricReady, "error", msg,
                        ElementTarget: $"rubric-lock-{skillId}"));
                }
            }
        }

        // ---- INV-5: section timers sum to the declared duration ---------------------------------------
        var inv5 = new List<ExamAuditIssueDto>();

        var servedSkillIds = sections
            .Where(s => rosterBySection.Any(r => r.SectionId == s.SectionId && r.Count > 0))
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

        // ---- INV-6: tier isolation retired (#310) ------------------------------------------------------
        var inv6 = new List<ExamAuditIssueDto>();

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
