using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #204 / S-07: the productive-task and visual-stimulus studio.
/// <para>
/// <b>One ingestion seam, as D17 and #203 require.</b> The studio builds one bundle and hands it to
/// <see cref="PoolIngestionGates.JudgeBundleAsync"/> - the same call <see cref="PoolIngestionService"/>
/// and <see cref="StimulusStudioService"/> make - then writes the accepted bundle with
/// <see cref="PoolIngestionWriter"/>, the same writer. Nothing here re-implements a gate and there is
/// no second writer. What this class adds is the two rules no gate states, because they are about the
/// <em>shape of a productive task</em> rather than about an item: D0.4's per-type bounds, and
/// <see cref="BusinessObjects.ProductiveTaskEnvelope"/>.
/// </para>
/// <para>
/// <b>Two skills, not four.</b> A shared passage or audio serves reading and listening and is
/// <see cref="StimulusStudioService"/>'s; a speaking or writing prompt is one task a rubric scores and
/// is this screen's. Offering both on one screen would pre-create child slots a productive task cannot
/// hold.
/// </para>
/// <para>
/// <b>What "for placement" changes, and why it is shown before the save.</b> A placement speaking task
/// is capped at 60 seconds for every band (D0.2, D0.7) and a placement C1/C2 writing task is scoped
/// below the practice envelope so two of them fit the 22-minute budget. Both figures are on
/// <see cref="ProductiveTaskAnalysisDto"/> and not only in a refusal, because a cap discovered at save
/// time is a refusal the author meets after doing the work.
/// </para>
/// </summary>
public sealed class ProductiveStudioService(
    CompanionDbContext db,
    TimeProvider clock,
    PoolIngestionGates gates) : IProductiveStudioService
{
    /// <summary>
    /// The refusals that mean "this check has not run" rather than "this content is defective", on the
    /// same rule as the stimulus studio: they come from the seam because it has no model to run them
    /// with, and the screen names them. A check that did not run must never read as a pass (D17).
    /// </summary>
    private static readonly HashSet<string> ChecksThatDidNotRun = new(StringComparer.Ordinal)
    {
        GateThreeRules.DistinctFamilies,
        GateThreeRules.UnusableDistribution,
        GateThreeRules.KeyConfidence,
        GateThreeRules.DistractorCeiling,
        GateThreeRules.Dissent,
        GateThreeRules.WorldKnowledge,
        GateFourRules.ClassifierUnavailable,
        GateFourRules.ClassifierNotIndependent,
        GateTwoRules.NotProfiled,
    };

    public static string GateOf(string rule) => rule switch
    {
        _ when rule.StartsWith("gate1.", StringComparison.Ordinal) => "gate one",
        _ when rule.StartsWith("gate2.", StringComparison.Ordinal) => "gate two",
        _ when rule.StartsWith("gate3.", StringComparison.Ordinal) => "gate three",
        _ when rule.StartsWith("gate4.", StringComparison.Ordinal) => "gate four",
        _ => "studio rules (D0.4)",
    };

    // ---- the pickers -------------------------------------------------------------------------

    public async Task<ProductiveStudioOptionsDto> GetOptionsAsync(
        string modality,
        CancellationToken cancellationToken = default)
    {
        var skill = RequireStudioSkill(modality);

        var topics = await db.Topics.AsNoTracking()
            .Where(t => t.Modality == skill)
            .OrderBy(t => t.Code)
            .Select(t => new ProductiveStudioTopicDto(t.TopicId, t.Code, t.Name))
            .ToListAsync(cancellationToken);

        var types = ProductiveTaskEnvelope.TypesFor(skill)
            .Select(code => ProductiveTaskEnvelope.Bounds(skill, code)!)
            .Select(bounds => new ProductiveTypeOptionDto(
                bounds.ItemType,
                bounds.Visual.ToString().ToLowerInvariant(),
                bounds.MinimumPrepSeconds,
                bounds.MaximumPrepSeconds,
                bounds.MinimumResponseSeconds,
                bounds.MaximumResponseSeconds,
                bounds.MinimumWords,
                bounds.MaximumWords))
            .ToList();

        return new ProductiveStudioOptionsDto(
            topics,
            CefrLevel.AuthoredLevels.Select(level => level.Code).ToList(),
            types);
    }

    // ---- the live analysis -------------------------------------------------------------------

    public async Task<ProductiveTaskAnalysisDto> AnalyseAsync(
        ProductiveTaskDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var skill = RequireStudioSkill(request.Modality);
        RequireLevel(request.Level, out var level);

        var shapes = Shapes(request.SubTasks);
        var refusals = new List<ProductiveRefusalDto>();

        // The studio's own shape rules first, so a task whose parts are the wrong count or carry no
        // range is not then questioned about a window on a part that should not exist.
        foreach (var refusal in ProductiveTaskEnvelope.Review(
                     skill, level, request.PlacementEligible, request.PromptCopy,
                     request.ImagePath, request.ImageAlt, shapes))
        {
            refusals.Add(ToDto(refusal.Rule, refusal.Message));
        }

        var unrun = new List<ProductiveRefusalDto>();
        if (shapes.Count > 0)
        {
            var verdict = await gates.JudgeBundleAsync(
                skill,
                BuildBundle(request, skill, shapes),
                await TopicCatalog.LoadAsync(db, cancellationToken),
                cancellationToken);

            // A refusal is the author's to fix; a check that could not run is the deployment's. Splitting
            // them is what stops the screen saying "cannot save" about a task the save accepts.
            foreach (var record in verdict.Records)
            {
                foreach (var refusal in record.Refusals)
                {
                    var target = ChecksThatDidNotRun.Contains(refusal.Rule) ? unrun : refusals;
                    target.Add(ToDto(refusal.Rule, $"part {record.Line}: {refusal.Message}"));
                }
            }
        }

        // #258: gate three has no pure half. With no verifier models it never runs, and saying so is the
        // only honest report - the same sentence the stimulus studio gives, because an author who saw
        // "nothing unrun" here and "gate three did not run" there would conclude the task was certified.
        if (gates.HasNoVerifiers)
        {
            unrun.Add(ToDto(
                "gate3.not_run",
                "the blind dual-model key verification and the rubric ablation were not run: no verifier "
                + "models are configured, so nothing has certified this task (CNT-37, D17)."));
        }

        // The cap is computed on the draft, not at extraction, and it is returned whether or not the
        // task is currently legal - a refusal the author has to reverse-engineer from a message is the
        // defect this field exists to prevent. The practice figure is computed WITHOUT the placement
        // flag, so the screen can show both and the author can see what placement takes away.
        return new ProductiveTaskAnalysisDto(
            refusals,
            unrun,
            request.PlacementEligible && string.Equals(skill, "speaking", StringComparison.Ordinal)
                ? ProductiveTaskEnvelope.PlacementRecordingCapSeconds
                : null,
            Ranges(skill, level, request.PlacementEligible, shapes)
                .Select((range, index) => new ProductiveWordRangeDto(
                    index + 1, range.Floor, range.Ceiling,
                    request.PlacementEligible && level >= CefrLevel.C1))
                .ToList(),
            Ranges(skill, level, placementEligible: false, shapes)
                .Select((range, index) => new ProductiveWordRangeDto(
                    index + 1, range.Floor, range.Ceiling, false))
                .ToList());
    }

    private static IReadOnlyList<(int Floor, int Ceiling)> Ranges(
        string skill,
        CefrLevel level,
        bool placementEligible,
        IReadOnlyList<ProductiveSubTaskShape> shapes) =>
        shapes
            .Select((shape, index) =>
                RangeOf(skill, level, placementEligible, shape, index + 1, shapes.Count))
            .ToList();

    // ---- the save ----------------------------------------------------------------------------

    public async Task<ProductiveTaskBundleDto> SaveAsync(
        ProductiveTaskDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var skill = RequireStudioSkill(request.Modality);
        RequireLevel(request.Level, out var level);

        if (string.IsNullOrWhiteSpace(request.TopicCode))
        {
            throw Refused([$"[{ProductiveTaskRules.Context}] "
                + "the task names no topic. Topic and level come from the studio's context, and a task "
                + "without them lands in no cell: nothing can count it, serve it or report mastery of it."]);
        }

        var shapes = Shapes(request.SubTasks);

        // The shape rules, then the seam. A refusal here writes nothing at all, and the reason names
        // the rule or the gate - never "invalid".
        var shape = ProductiveTaskEnvelope.Review(
            skill, level, request.PlacementEligible, request.PromptCopy,
            request.ImagePath, request.ImageAlt, shapes);
        if (shape.Count > 0)
        {
            throw Refused(shape.Select(refusal => $"[{refusal.Rule}] {refusal.Message}").ToList());
        }

        var verdict = await gates.JudgeBundleAsync(
            skill,
            BuildBundle(request, skill, shapes),
            await TopicCatalog.LoadAsync(db, cancellationToken),
            cancellationToken);

        // Everything the seam refused, except the checks that could not run: a draft save is not a
        // certification, and those are reported in the analysis by name.
        var blocking = verdict.Records
            .SelectMany(record => record.Refusals.Select(refusal => (record.Line, Refusal: refusal)))
            .Where(entry => !ChecksThatDidNotRun.Contains(entry.Refusal.Rule))
            .ToList();

        if (blocking.Count > 0)
        {
            throw Refused(blocking
                .Select(entry => $"[{entry.Refusal.Rule}] part {entry.Line}: {entry.Refusal.Message}")
                .ToList());
        }

        // One save, one transaction: a task with a prompt and no parts, or a part pointing at a
        // stimulus that rolled back, is a half-committed task.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var written = PoolIngestionWriter.WriteBundle(db, verdict, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var topicCodes = await TopicCodesAsync(written.Items.Select(item => item.TopicId), cancellationToken);

        return new ProductiveTaskBundleDto(
            written.Items[0].StimulusId ?? 0,
            request.Level,
            written.Items
                .Zip(written.Versions, (item, version) => ToDto(
                    item, version, topicCodes.GetValueOrDefault(item.TopicId, string.Empty)))
                .ToList());
    }

    // ---- helpers -----------------------------------------------------------------------------

    /// <summary>
    /// The draft as the upload format would have carried it. The ids are synthesised because this path
    /// has no component id, and CNT-03's prefix rule is about the tier a record declares rather than
    /// about who named it; the studio's stock is demo, so the prefix says demo.
    /// <para>
    /// One group, one stimulus, N parts. The prompt copy is the group's text so gate two can say what
    /// it is not profiling, and each part's windows travel on its own record - never on the prompt -
    /// because a two-part task carries a range and a budget per part.
    /// </para>
    /// </summary>
    private static SkillContentPoolBundle BuildBundle(
        ProductiveTaskDraftRequest request,
        string skill,
        IReadOnlyList<ProductiveSubTaskShape> shapes)
    {
        var group = new SkillContentPoolGroup
        {
            StimulusId = $"stim_demo_{Guid.NewGuid():N}",
            ContentTier = "demo",
            StimulusLevel = request.Level,
            DomainContext = request.DomainContext,
            Content = new SkillContentPoolContent
            {
                TextOrTranscript = request.PromptCopy,
                ImageUri = request.ImagePath,
                ImageAlt = request.ImageAlt,
            },
        };

        var records = new List<SkillContentPoolRecord>(shapes.Count);
        for (var index = 0; index < shapes.Count; index++)
        {
            var shape = shapes[index];
            var item = new SkillContentPoolItem
            {
                ItemId = $"item_demo_{Guid.NewGuid():N}",
                ContentTier = "demo",
                TopicId = request.TopicCode,
                ItemLevel = request.Level,
                ItemType = shape.ItemType,
                // The part's own instruction is its stem: a two-part task's parts are answered one at
                // a time, so each says what it wants.
                Stem = shape.Prompt,
                Explanation = shape.Explanation,
                // No options and no key: the productive shape (CNT-06).
                Options = [],
                CorrectOption = null,
                PlacementEligible = request.PlacementEligible,
                EvidenceSpan = null,
                MinWords = shape.MinWords,
                MaxWords = shape.MaxWords,
                PrepSeconds = shape.PrepSeconds,
                ResponseSeconds = shape.ResponseSeconds,
            };

            // The line is the part's position: the seam reports per record by line, and the studio's
            // records are one part each in order.
            records.Add(new SkillContentPoolRecord(index + 1, item));
        }

        return new SkillContentPoolBundle(1, group, records);
    }

    /// <summary>
    /// The permitted word range of one part, or the type's own where the task is not placement-scoped.
    /// Public because the screen renders the same figure the save refuses on.
    /// </summary>
    public static (int Floor, int Ceiling) RangeOf(
        string skill,
        CefrLevel level,
        bool placementEligible,
        ProductiveSubTaskShape shape,
        int position,
        int count)
    {
        var bounds = ProductiveTaskEnvelope.Bounds(skill, shape.ItemType);
        if (bounds is null || bounds.MaximumWords is null)
        {
            return (0, 0);
        }

        var (floor, ceiling, _) = ProductiveTaskEnvelope.Range(bounds, level, placementEligible, position, count);
        return (floor, ceiling);
    }

    private static IReadOnlyList<ProductiveSubTaskShape> Shapes(IReadOnlyList<Contracts.ProductiveSubTaskInput>? subTasks) =>
        (subTasks ?? [])
            .Select(subTask => new ProductiveSubTaskShape(
                subTask.ItemType,
                subTask.Prompt,
                subTask.MinWords,
                subTask.MaxWords,
                subTask.PrepSeconds,
                subTask.ResponseSeconds,
                subTask.Explanation))
            .ToList();

    private async Task<Dictionary<int, string>> TopicCodesAsync(
        IEnumerable<int> topicIds, CancellationToken cancellationToken)
    {
        var ids = topicIds.Distinct().ToList();
        return await db.Topics.AsNoTracking()
            .Where(topic => ids.Contains(topic.TopicId))
            .Select(topic => new { topic.TopicId, topic.Code })
            .ToDictionaryAsync(row => row.TopicId, row => row.Code, cancellationToken);
    }

    private static string RequireStudioSkill(ModalityDto modality) => RequireStudioSkill(modality.ToCode());

    private static string RequireStudioSkill(string modality)
    {
        var code = modality.Trim().ToLowerInvariant();
        if (!ProductiveTaskEnvelope.IsProductiveSkill(code))
        {
            throw new AdminItemException(
                ApiError.Codes.Validation,
                $"'{modality}' is not a skill the productive studio authors. A speaking or writing prompt "
                + "is one task a rubric scores; a shared passage or audio serves reading and listening and "
                + "is authored in the stimulus studio.");
        }

        return code;
    }

    private static void RequireLevel(string? code, out CefrLevel level)
    {
        if (!CefrLevel.TryParseAuthored(code, out level))
        {
            throw new AdminItemException(
                ApiError.Codes.Validation,
                $"'{code}' is not an authored CEFR level. Content is authored at A1-C2.");
        }
    }

    private static ProductiveRefusalDto ToDto(string rule, string message) => new(rule, GateOf(rule), message);

    private static AdminItemException Refused(IReadOnlyList<string> reasons) =>
        new(ApiError.Codes.Validation, "The task was refused, so nothing was written: " + string.Join(" ", reasons));

    private static PoolItemDto ToDto(Item item, ItemVersion version, string topicCode) =>
        new(
            item.ItemId,
            version.ItemVersionId,
            version.VersionNumber,
            1,
            EnumCodes.Parse<ModalityDto>(item.Modality),
            item.TopicId,
            topicCode,
            item.CefrLevel.Code,
            item.ItemType,
            item.ContentTier,
            item.LifecycleStatus,
            item.PlacementEligible,
            item.StimulusId,
            version.Stem,
            version.OptionA,
            version.OptionB,
            version.OptionC,
            version.OptionD,
            version.KeyLabel,
            version.Explanation,
            version.EvidenceStart,
            version.EvidenceEnd,
            version.EvidenceUnit,
            version.EvidenceQuote,
            version.ReferenceText,
            version.ReferenceKind is null ? null : EnumCodes.Parse<ReferenceKindDto>(version.ReferenceKind),
            version.CreatedAt,
            version.RationaleA,
            version.RationaleB,
            version.RationaleC,
            version.RationaleD);
}
