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
/// #203 / S-05: the stimulus-and-children studio.
/// <para>
/// <b>One ingestion seam, as D17 requires.</b> The studio builds one bundle and hands it to
/// <see cref="PoolIngestionGates.JudgeBundleAsync"/> - the same call <see cref="PoolIngestionService"/>
/// makes - then writes the accepted bundle with <see cref="PoolIngestionWriter"/>, the same writer.
/// Nothing here re-implements a gate: gates one, two and four and the tier/record rules all answer
/// from the seam. What this class adds is the two rules no gate states, because they are about the
/// <em>shape</em> of a bundle rather than about an item: D0.3's ±1 envelope and D0.4's child count.
/// </para>
/// <para>
/// <b>Why a bundle is saved as a draft even though gates three and four's classifier did not run.</b>
/// Those gates need verifier models and a classifier; an unwired deployment has neither, and the
/// ingestion seam correctly refuses a bundle it could not certify. The studio's save is not a
/// certification - it writes <c>draft</c> and <c>demo</c> (CNT-42, INV-6), and nothing on this path can
/// reach <c>validated</c>. So a check that did not run is <em>named</em> in the analysis and does not
/// block a draft save, while every check that did run refuses: gate one's item hygiene, gate two's
/// linguistic profile, gate four's span independence and audio order. A bundle that fails one of those
/// cannot be saved at all, draft or not.
/// </para>
/// </summary>
public sealed class StimulusStudioService(
    CompanionDbContext db,
    TimeProvider clock,
    PoolIngestionGates gates) : IStimulusStudioService
{
    /// <summary>
    /// The refusals that mean "this check has not run" rather than "this content is defective". They
    /// come from the seam because it has no model to run them with, and they are reported to the author
    /// by name: a check that did not run must never read as a pass (D17).
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
        GateTwoRules.TranscriptUnavailable,
    };

    public static string GateOf(string rule) => rule switch
    {
        _ when rule.StartsWith("gate1.", StringComparison.Ordinal) => "gate one",
        _ when rule.StartsWith("gate2.", StringComparison.Ordinal) => "gate two",
        _ when rule.StartsWith("gate3.", StringComparison.Ordinal) => "gate three",
        _ when rule.StartsWith("gate4.", StringComparison.Ordinal) => "gate four",
        _ => "studio rules (D0.3)",
    };

    // ---- the pickers -------------------------------------------------------------------------

    public async Task<StimulusStudioOptionsDto> GetOptionsAsync(
        string modality,
        CancellationToken cancellationToken = default)
    {
        RequireStudioSkill(modality);

        var topics = await db.Topics.AsNoTracking()
            .Where(t => t.Modality == modality)
            .OrderBy(t => t.Code)
            .Select(t => new StimulusStudioTopicDto(t.TopicId, t.Code, t.Name))
            .ToListAsync(cancellationToken);

        var quotas = new List<StimulusStudioQuotaDto>();
        foreach (var level in CefrLevel.AuthoredLevels)
        {
            foreach (var type in StudioTypes(modality))
            {
                var quota = StimulusEnvelope.Quote(modality, level, type);
                quotas.Add(new StimulusStudioQuotaDto(
                    level.Code,
                    type,
                    quota.Locked,
                    quota.Minimum,
                    quota.Maximum,
                    quota.AllowedChildLevels.Select(child => child.Code).ToList(),
                    StimulusEnvelope.MinimumAtLevel(quota.Locked)));
            }
        }

        return new StimulusStudioOptionsDto(
            topics,
            CefrLevel.AuthoredLevels.Select(level => level.Code).ToList(),
            StudioTypes(modality).ToList(),
            quotas,
            StimulusEnvelope.SpanUnitFor(modality));
    }

    // ---- the live analysis -------------------------------------------------------------------

    public async Task<StimulusDraftAnalysisDto> AnalyseAsync(
        StimulusDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var modality = RequireStudioSkill(request.Modality);
        RequireLevel(request.StimulusLevel, out var stimulusLevel);
        var children = request.Children ?? [];
        var unit = StimulusEnvelope.SpanUnitFor(modality);
        var text = string.Equals(modality, "listening", StringComparison.Ordinal)
            ? request.Transcript
            : request.Passage;

        var refusals = new List<StimulusRefusalDto>();

        // D0.3 and D0.4: shape first, because a bundle of the wrong size is not one the seam can judge
        // as a bundle - four children on a sentence-cloze stimulus would report a span overlap for two
        // items that should not exist.
        var levels = new List<CefrLevel>(children.Count);
        foreach (var child in children)
        {
            if (CefrLevel.TryParseAuthored(child.CefrLevel, out var level))
            {
                levels.Add(level);
            }
            else
            {
                // A draft being analysed is mid-edit: an unparseable level is a sentence to show, not an
                // exception. The save path refuses it outright.
                refusals.Add(ToDto(
                    StimulusStudioRules.ChildLevel,
                    $"child {child.Position}: '{child.CefrLevel}' is not an authored CEFR level. A child is authored at A1-C2 (D0.3)."));
                levels.Add(CefrLevel.PreA1);
            }
        }

        foreach (var refusal in StimulusEnvelope.Review(
                     modality, stimulusLevel, request.ItemType, levels, unit))
        {
            refusals.Add(ToDto(refusal.Rule, refusal.Message));
        }

        var verdict = await gates.JudgeBundleAsync(
            modality,
            BuildBundle(request, modality, children, unit),
            await TopicCatalog.LoadAsync(db, cancellationToken),
            cancellationToken);

        // A refusal is the author's to fix; a check that could not run is the deployment's, and does not
        // block a draft save. Splitting them is what stops the screen saying "cannot save" about a
        // bundle the save accepts.
        var unrun = new List<StimulusRefusalDto>();
        foreach (var record in verdict.Records)
        {
            foreach (var refusal in record.Refusals)
            {
                var target = ChecksThatDidNotRun.Contains(refusal.Rule) ? unrun : refusals;
                target.Add(ToDto(refusal.Rule, $"child {record.Line}: {refusal.Message}"));
            }
        }

        // Gate three has no pure half: with no verifier models it never runs, and saying so is the only
        // honest report. The save writes a draft, which is not a certification (CNT-37, D17).
        if (gates.HasNoVerifiers)
        {
            unrun.Add(ToDto(
                "gate3.not_run",
                "the blind dual-model key verification and the stimulus ablation were not run: no verifier "
                + "models are configured, so nothing has certified this bundle's keys (CNT-37, D17)."));
        }

        var gauges = LevelGaugeReader.Read(modality, stimulusLevel, text);

        return new StimulusDraftAnalysisDto(
            refusals,
            unrun,
            gauges.Gauges.Select(gauge => new LevelGaugeDto(gauge.Label, gauge.Value, gauge.Target, gauge.Inside)).ToList(),
            gauges.Verdict,
            Overlaps(children, unit),
            Gaps(children, unit),
            gauges.Fits?.Code,
            stimulusLevel.Code);
    }

    // ---- the save ----------------------------------------------------------------------------

    public async Task<StimulusBundleDto> SaveAsync(
        StimulusDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var modality = RequireStudioSkill(request.Modality);
        RequireLevel(request.StimulusLevel, out var stimulusLevel);

        if (string.IsNullOrWhiteSpace(request.Passage)
            && string.IsNullOrWhiteSpace(request.Transcript)
            && string.IsNullOrWhiteSpace(request.AudioPath))
        {
            throw Refused([$"[{StimulusStudioRules.StimulusText}] "
                + "A stimulus needs a passage, a transcript or an audio file. An empty one is a row the pool "
                + "would carry forever and no child could cite."]);
        }

        var children = request.Children ?? [];
        var unit = StimulusEnvelope.SpanUnitFor(modality);

        // The shape rules, then the seam. A refusal here writes nothing at all, and the reason names the
        // rule or the gate - never "invalid".
        var shape = StimulusEnvelope.Review(
            modality, stimulusLevel, request.ItemType,
            children.Select(child => ParseLevel(child.CefrLevel)).ToList(), unit);
        if (shape.Count > 0)
        {
            throw Refused(shape.Select(refusal => $"[{refusal.Rule}] {refusal.Message}").ToList());
        }

        var verdict = await gates.JudgeBundleAsync(
            modality,
            BuildBundle(request, modality, children, unit),
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
                .Select(entry => $"[{entry.Refusal.Rule}] child {entry.Line}: {entry.Refusal.Message}")
                .ToList());
        }

        // One save, one transaction: a stimulus with no children, or a child pointing at a stimulus that
        // rolled back, is a half-committed bundle.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var written = PoolIngestionWriter.WriteBundle(db, verdict, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var topicCodes = await TopicCodesAsync(written.Items.Select(item => item.TopicId), cancellationToken);

        return new StimulusBundleDto(
            written.Items[0].StimulusId ?? 0,
            request.StimulusLevel,
            written.Items
                .Zip(written.Versions, (item, version) => ToDto(item, version, topicCodes.GetValueOrDefault(item.TopicId, string.Empty)))
                .ToList());
    }

    // ---- helpers -----------------------------------------------------------------------------

    /// <summary>
    /// D0.4's types per skill. Mirrors <c>CK_Items_Modality_Type</c>; the constraint stays the
    /// authority, and this is only the earlier list so a picker cannot offer a type the database
    /// refuses.
    /// </summary>
    public static IReadOnlyList<string> StudioTypes(string modality) =>
        string.Equals(modality, "listening", StringComparison.Ordinal)
            ? ["short_exchange", "extended_dialogue", "monologue_talk"]
            : ["sentence_cloze", "passage_comprehension", "text_insertion_cohesion"];

    /// <summary>
    /// The draft as the upload format would have carried it. The ids are synthesised because this path
    /// has no component id, and CNT-03's prefix rule is about the tier a record declares rather than
    /// about who named it; the studio's stock is demo, so the prefix says demo.
    /// </summary>
    private static SkillContentPoolBundle BuildBundle(
        StimulusDraftRequest request,
        string modality,
        IReadOnlyList<StimulusDraftChild> children,
        string unit)
    {
        var listening = string.Equals(modality, "listening", StringComparison.Ordinal);
        var stimulusId = $"stim_demo_{Guid.NewGuid():N}";

        var group = new SkillContentPoolGroup
        {
            StimulusId = stimulusId,
            ContentTier = "demo",
            StimulusLevel = request.StimulusLevel,
            DomainContext = request.DomainContext,
            Content = new SkillContentPoolContent
            {
                TextOrTranscript = listening ? request.Transcript : request.Passage,
                AudioAsset = listening && !string.IsNullOrWhiteSpace(request.AudioPath)
                    ? new SkillContentPoolAudio { AudioUri = request.AudioPath }
                    : null,
            },
        };

        var records = new List<SkillContentPoolRecord>(children.Count);
        foreach (var child in children.OrderBy(c => c.Position))
        {
            var options = new List<SkillContentPoolOption>();
            foreach (var (label, text, rationale) in new[]
                     {
                         ("A", child.OptionA, child.RationaleA),
                         ("B", child.OptionB, child.RationaleB),
                         ("C", child.OptionC, child.RationaleC),
                         ("D", child.OptionD, child.RationaleD),
                     })
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    options.Add(new SkillContentPoolOption { Key = label, Text = text, DistractorRationale = rationale });
                }
            }

            var item = new SkillContentPoolItem
            {
                ItemId = $"item_demo_{Guid.NewGuid():N}",
                ContentTier = "demo",
                TopicId = child.TopicCode,
                ItemLevel = child.CefrLevel,
                ItemType = request.ItemType,
                Stem = child.Stem,
                Options = options,
                CorrectOption = child.KeyLabel,
                Explanation = child.Explanation,
                PlacementEligible = child.PlacementEligible,
                EvidenceSpan = new SkillContentPoolSpan
                {
                    CharStart = listening ? null : child.EvidenceStart,
                    CharEnd = listening ? null : child.EvidenceEnd,
                    StartSec = listening ? child.EvidenceStart : null,
                    EndSec = listening ? child.EvidenceEnd : null,
                    VerbatimQuote = child.EvidenceQuote,
                },
            };

            // The line is the child's position: the seam reports per record by line, and the studio's
            // records are one child each in order.
            records.Add(new SkillContentPoolRecord(child.Position, item));
        }

        return new SkillContentPoolBundle(children.Count == 0 ? 1 : children.Min(c => c.Position), group, records);
    }

    /// <summary>
    /// CNT-12's figures for the indicator. <see cref="GateFour.SpanOverlap"/> is the same arithmetic the
    /// gate refuses on, so the number the author watches and the number that blocks the save are one.
    /// </summary>
    private static IReadOnlyList<StimulusOverlapDto> Overlaps(
        IReadOnlyList<StimulusDraftChild> children,
        string unit)
    {
        if (string.Equals(unit, "sec", StringComparison.Ordinal))
        {
            return [];
        }

        var overlaps = new List<StimulusOverlapDto>();
        for (var first = 0; first < children.Count; first++)
        {
            for (var second = first + 1; second < children.Count; second++)
            {
                var a = Span(children[first], unit);
                var b = Span(children[second], unit);
                var overlap = GateFour.SpanOverlap(a, b);
                if (overlap is null)
                {
                    continue;
                }

                overlaps.Add(new StimulusOverlapDto(
                    children[first].Position,
                    children[second].Position,
                    overlap.Value,
                    overlap.Value >= GateFour.MaximumSpanOverlap));
            }
        }

        return overlaps;
    }

    /// <summary>CNT-10's gap between adjacent audio children, in the order they are served.</summary>
    private static IReadOnlyList<StimulusGapDto> Gaps(IReadOnlyList<StimulusDraftChild> children, string unit)
    {
        if (!string.Equals(unit, "sec", StringComparison.Ordinal) || children.Count < 2)
        {
            return [];
        }

        var ordered = children.OrderBy(child => child.Position).ToList();
        var gaps = new List<StimulusGapDto>(ordered.Count - 1);
        for (var index = 0; index < ordered.Count - 1; index++)
        {
            var earlier = new GateFourAudioChild(index + 1, string.Empty, ordered[index].EvidenceStart ?? 0, ordered[index].EvidenceEnd ?? 0, unit);
            var later = new GateFourAudioChild(index + 2, string.Empty, ordered[index + 1].EvidenceStart ?? 0, ordered[index + 1].EvidenceEnd ?? 0, unit);
            var seconds = GateFour.GapSeconds(earlier, later);

            gaps.Add(new StimulusGapDto(
                ordered[index].Position,
                ordered[index + 1].Position,
                seconds,
                seconds < GateFour.MinimumAudioGapSeconds));
        }

        return gaps;
    }

    private static GateFourSpan Span(StimulusDraftChild child, string unit) =>
        new(child.Position, child.TopicCode, child.EvidenceStart ?? 0, child.EvidenceEnd ?? 0, unit);

    private async Task<Dictionary<int, string>> TopicCodesAsync(IEnumerable<int> topicIds, CancellationToken cancellationToken)
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
        if (!StimulusEnvelope.IsStudioSkill(code))
        {
            throw new AdminItemException(
                ApiError.Codes.Validation,
                $"'{modality}' is not a skill the stimulus studio authors. A shared passage or audio serves reading "
                + "and listening; a speaking or writing prompt is one task and is authored in the productive-task studio.");
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

    private static CefrLevel ParseLevel(string? code) =>
        CefrLevel.TryParseAuthored(code, out var level)
            ? level
            : throw new AdminItemException(
                ApiError.Codes.Validation,
                $"'{code}' is not an authored CEFR level. A child is authored at A1-C2 (D0.3).");

    private static StimulusRefusalDto ToDto(string rule, string message) => new(rule, GateOf(rule), message);

    private static AdminItemException Refused(IReadOnlyList<string> reasons) =>
        new(ApiError.Codes.Validation, "The bundle was refused, so nothing was written: " + string.Join(" ", reasons));

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
