using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class AdminExamService
{
    /// <summary>How many candidate versions one section offers on the screen, and no more.</summary>
    private const int AssemblyCandidatePageSize = 40;

    /// <summary>
    /// #225 / S-09: the assembly workbench read. For each section of the certificate: what it declares,
    /// what is bound, and how much <b>servable</b> stock the pool can supply.
    /// <para>
    /// <b>The supply count is the point of the screen, and its definition is narrow on purpose.</b> An
    /// item counts only when it is <c>active</c>, because that is the one lifecycle state a learner is
    /// served (CNT-42; every pool selector filters it), and only when its tier is one this paper may
    /// draw. INV-6 is asymmetric — <c>trg_ExamItems_ProductionBindsProduction</c> refuses a production
    /// exam drawing demo stock, and lets a demo exam draw either tier — so the tier filter is
    /// <see cref="DrawableTiers"/> rather than a bare equality. Filtering a demo paper to demo stock
    /// alone would understate what it can actually fill.
    /// </para>
    /// <para>
    /// <b>Supply is per section, and two sections of one skill share it.</b> That is the pool's depth
    /// for the cell, not an allocation, and deduplicating across sections would invent the allocation
    /// policy the certificate's shape has not stated.
    /// </para>
    /// <para>
    /// <b>The cell is the section's skill, not the certificate.</b> Items carry no <c>CertId</c> since
    /// #184: the warehouse is certificate-agnostic and a certificate declares which skills it examines.
    /// <c>Sections</c> carries no topic or level either, so the section's own skill is the widest cell
    /// the schema can state — a narrower one would be invented here rather than read.
    /// </para>
    /// <para>
    /// Nothing is written and nothing is cached. Every figure is read at call time, because retiring or
    /// promoting a single item moves the answer.
    /// </para>
    /// </summary>
    public async Task<ExamAssemblyDto> GetExamAssemblyAsync(int examId, CancellationToken cancellationToken = default)
    {
        var exam = await db.Exams.AsNoTracking()
            .Include(e => e.Cert)
            .FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken)
            ?? throw new ExamNotFoundException(examId);

        var sections = await db.Sections.AsNoTracking()
            .Where(s => s.CertId == exam.CertId)
            .OrderBy(s => s.DisplayOrder)
            .ThenBy(s => s.SectionId)
            .ToListAsync(cancellationToken);

        var modalities = sections.Select(s => s.Modality).Distinct(StringComparer.Ordinal).ToList();
        var drawableTiers = DrawableTiers(exam.ContentTier);

        // One read of the stock and one of its versions: a per-section query would repeat the same
        // scan once per section that shares a skill.
        var stock = await db.Items.AsNoTracking()
            .Where(i => modalities.Contains(i.Modality)
                        && i.LifecycleStatus == AssemblyServableLifecycle
                        && drawableTiers.Contains(i.ContentTier))
            .Select(i => new
            {
                i.ItemId,
                i.Modality,
                topicCode = i.Topic.Code,
                i.CefrLevel,
                i.ItemType,
                i.ContentTier,
            })
            .ToListAsync(cancellationToken);

        var itemIds = stock.Select(i => i.ItemId).ToList();
        var versions = itemIds.Count == 0
            ? new List<NewestVersionRow>()
            : await db.ItemVersions.AsNoTracking()
                .Where(v => itemIds.Contains(v.ItemId))
                .Select(v => new NewestVersionRow(v.ItemId, v.ItemVersionId, v.VersionNumber, v.Stem))
                .ToListAsync(cancellationToken);

        // #254: "newest" is the authored order, stated once in GateContext. A candidate list that
        // offered the highest ItemVersionId would offer a different row than the sitting serves.
        var newestByItem = GateContext.NewestPerItem(versions, v => v.ItemId, v => v.VersionNumber)
            .ToDictionary(v => v.ItemId);

        var bindings = await (
            from b in db.ExamItems.AsNoTracking()
            where b.ExamId == examId
            join v in db.ItemVersions.AsNoTracking() on b.ItemVersionId equals v.ItemVersionId
            join i in db.Items.AsNoTracking() on b.ItemId equals i.ItemId
            join t in db.Topics.AsNoTracking() on i.TopicId equals t.TopicId
            select new BindingRow(b.SectionId, b.DisplayOrder, b.UnitNumber, b.ItemVersionId, i.CefrLevel, t.Code))
            .ToListAsync(cancellationToken);

        var finishedAttempts = await db.ExamAttempts.AsNoTracking()
            .CountAsync(a => a.ExamId == examId && (a.Status == "submitted" || a.Status == "graded"),
                cancellationToken);

        var sectionDtos = new List<ExamAssemblySectionDto>(sections.Count);
        foreach (var section in sections)
        {
            var sectionStock = stock
                .Where(i => string.Equals(i.Modality, section.Modality, StringComparison.Ordinal))
                .ToList();

            var sectionBindings = bindings
                .Where(b => b.SectionId == section.SectionId)
                .OrderBy(b => b.DisplayOrder)
                .ToList();

            // The declared shape first, then any binding placed beyond it: a row that exists is shown,
            // or the positions on screen would not add up to the bound total beside them.
            var positions = new List<ExamAssemblyPositionDto>(section.ExpectedQuestionCount);
            for (var order = 1; order <= section.ExpectedQuestionCount; order++)
            {
                positions.Add(Position(order, declared: true, sectionBindings));
            }

            foreach (var extra in sectionBindings.Where(b => b.DisplayOrder > section.ExpectedQuestionCount))
            {
                positions.Add(Position(extra.DisplayOrder, declared: false, sectionBindings));
            }

            var candidates = sectionStock
                .Where(i => newestByItem.ContainsKey(i.ItemId))
                .OrderBy(i => i.topicCode, StringComparer.Ordinal)
                .ThenBy(i => i.CefrLevel)
                .ThenBy(i => i.ItemId)
                .ToList();

            sectionDtos.Add(new ExamAssemblySectionDto(
                section.SectionId,
                section.Code,
                section.Name,
                EnumCodes.Parse<ModalityDto>(section.Modality),
                section.DisplayOrder,
                section.ExpectedQuestionCount,
                sectionBindings.Count,
                // Supply is the versions actually offered below it, not a separately counted set: a
                // figure that counted items while the list skipped one would promise a position the
                // screen cannot bind.
                candidates.Count,
                Math.Max(0, section.ExpectedQuestionCount - candidates.Count),
                positions,
                candidates
                    .Take(AssemblyCandidatePageSize)
                    .Select(i =>
                    {
                        var version = newestByItem[i.ItemId];
                        return new ExamAssemblyCandidateDto(
                            i.ItemId,
                            version.ItemVersionId,
                            version.VersionNumber,
                            i.topicCode,
                            i.CefrLevel.Code,
                            i.ItemType,
                            i.ContentTier,
                            version.Stem);
                    })
                    .ToList(),
                candidates.Count));
        }

        return new ExamAssemblyDto(
            exam.ExamId,
            exam.Name,
            exam.Cert.Code,
            exam.ContentTier,
            exam.DurationMinutes,
            finishedAttempts,
            sectionDtos);
    }

    /// <summary>
    /// The tiers a paper of this tier may bind. INV-6 is one rule, not two: a production paper draws
    /// only production stock, and a demo paper draws either — demo stock is what a demo paper is for,
    /// and promoting stock must not strand a working paper.
    /// </summary>
    private static string[] DrawableTiers(string examTier) =>
        string.Equals(examTier, ContentTiers.Production, StringComparison.Ordinal)
            ? [ContentTiers.Production]
            : [ContentTiers.Production, ContentTiers.Demo];

    /// <summary>The one lifecycle state a learner is served (CNT-42). Named once, and read once.</summary>
    private const string AssemblyServableLifecycle = "active";

    private sealed record NewestVersionRow(int ItemId, int ItemVersionId, int VersionNumber, string Stem);

    private sealed record BindingRow(
        int SectionId, int DisplayOrder, int UnitNumber, int ItemVersionId, CefrLevel CefrLevel, string TopicCode);

    private static ExamAssemblyPositionDto Position(
        int order, bool declared, List<BindingRow> sectionBindings)
    {
        var bound = sectionBindings.FirstOrDefault(b => b.DisplayOrder == order);
        return new ExamAssemblyPositionDto(
            order,
            declared,
            bound?.ItemVersionId,
            bound?.TopicCode,
            bound?.CefrLevel.Code,
            bound?.UnitNumber);
    }
}
