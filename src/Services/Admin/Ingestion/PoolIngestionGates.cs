using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;

namespace Services;

/// <summary>#198: one record, resolved to the rows it would become.</summary>
internal sealed record ResolvedChild(
    string RecordId,
    PoolContent Content,
    string Stem,
    string? Explanation,
    CefrLevel Level,
    int TopicId,
    string TopicCode,
    int SpanStart,
    int SpanEnd,
    string SpanUnit,
    string AuthorFamily,
    int Ordinal);

/// <summary>#198: one bundle's verdict and, when accepted, the rows it writes.</summary>
internal sealed record BundleVerdict(
    SkillContentPoolBundle Bundle,
    IReadOnlyList<IngestionRecordResult> Records,
    IReadOnlyList<ResolvedChild> Children,
    PoolImport.StimulusInput? Stimulus,
    CefrLevel? StimulusLevel)
{
    /// <summary>A bundle is written whole or not at all: gate four judges its children's spans as a set.</summary>
    public bool Accepted => Records.Count > 0 && Records.All(record => record.Accepted);
}

/// <summary>
/// #198: the four gates, called once, against records that are not rows yet.
/// <para>
/// Every method here is a <em>call</em>, never a re-derivation: <see cref="GateOne.Review"/>,
/// <see cref="PoolGateTwo.ReviewContentAsync"/>, <see cref="GateThreeVerifier"/> (which wraps
/// <see cref="GateThree.Review"/>) and <see cref="GateFour"/> own their arithmetic. This class supplies
/// only the context a bundle record does not carry - the cell gate one balances within, the stimulus
/// text gate two profiles, the model families gate three compares, and the siblings gate four measures
/// for overlap.
/// </para>
/// <para>
/// <b>The models are injected, and absent means refuse.</b> Gate three needs two heterogeneous
/// verifiers and gate four an independent classifier. Both default to explicitly unavailable
/// implementations, so an unwired deployment refuses every record instead of certifying content nobody
/// checked: a check that did not run cannot pass.
/// </para>
/// </summary>
public sealed class PoolIngestionGates(
    CompanionDbContext db,
    IVerifierModel? verifierOne = null,
    IVerifierModel? verifierTwo = null,
    IVerifierModel? ablationSolver = null,
    ILevelClassifier? classifier = null,
    PoolGateTwo.IGateTwoTranscriber? transcriber = null)
{
    /// <summary>The author family a record declares when its verification audit names none.</summary>
    public const string UnnamedAuthorFamily = "unattributed";

    /// <summary>
    /// Whether this deployment wired gate three at all. Gate three is the only gate with no pure half,
    /// so a caller that reports its state has to ask rather than infer it from refusals: with no models
    /// the gate refuses every record, and a refuSAL is not the same fact as "the check never ran".
    /// </summary>
    public bool HasNoVerifiers => verifierOne is null || verifierTwo is null;

    /// <summary>
    /// Judges one bundle and returns one result per child, in file order. Gate two runs once for the
    /// stimulus; gates one and three run per child; gate four runs per child for the level claim and
    /// once over the bundle for span independence.
    /// </summary>
    internal async Task<BundleVerdict> JudgeBundleAsync(
        string skill,
        SkillContentPoolBundle bundle,
        TopicCatalog topics,
        CancellationToken cancellationToken)
    {
        var group = bundle.Group;
        var refusals = new Dictionary<int, List<IngestionRefusal>>();
        foreach (var record in bundle.Items)
        {
            refusals[record.Line] = [];
        }

        // ---- CNT-03: the tier in the id is the tier on the record ----
        var (groupTier, groupRefusal) = CheckTierPrefix(group.StimulusId, "stim", group.ContentTier, "stimulus_id");
        if (groupRefusal is not null)
        {
            AddToAll(refusals, groupRefusal);
        }

        foreach (var record in bundle.Items)
        {
            var (itemTier, itemRefusal) = CheckTierPrefix(
                record.Item.ItemId, "item", record.Item.ContentTier, "item_id");
            if (itemRefusal is not null)
            {
                refusals[record.Line].Add(itemRefusal);
            }

            // A child is its stimulus's tier: one passage cannot be demo for one question and
            // production for another, and there is no column that could hold both.
            if (groupTier is not null && itemTier is not null
                && !string.Equals(groupTier, itemTier, StringComparison.Ordinal))
            {
                refusals[record.Line].Add(new IngestionRefusal(
                    IngestionRules.TierPrefix,
                    $"the record declares content_tier '{itemTier}' while its stimulus '{group.StimulusId}' "
                    + $"declares '{groupTier}'. A child is its parent's tier (CNT-03, INV-6)."));
            }
        }

        // ---- the stimulus's own band: gates two and four are both measured against it ----
        if (!CefrLevel.TryParseAuthored(group.StimulusLevel, out var stimulusLevel))
        {
            return RefuseBundle(
                bundle,
                refusals,
                IngestionRules.RecordShape,
                $"stimulus_level '{group.StimulusLevel}' is not an authored level (A1-C2). The stimulus's own "
                + "level is what its children's envelope (D0.3) and its readability bound (D0.2) are measured "
                + "against, so no band means no judgement.");
        }

        var passage = string.Equals(skill, "reading", StringComparison.Ordinal)
            ? group.Content?.TextOrTranscript
            : null;
        var transcript = string.Equals(skill, "listening", StringComparison.Ordinal)
            ? group.Content?.TextOrTranscript
            : null;
        var audioPath = group.Content?.AudioAsset?.AudioUri;

        // ---- gate two, once per stimulus: it profiles the passage or transcript, not a child's stem ----
        var gateTwo = await PoolGateTwo.ReviewContentAsync(
            skill,
            GroupItemType(skill),
            stimulusLevel,
            passage,
            transcript,
            promptCopy: null,
            audioPath,
            transcriber,
            cancellationToken);

        AddToAll(refusals, gateTwo.Refusals.Select(r => new IngestionRefusal(r.Rule, r.Message)));

        // ---- resolve each child once; a shape failure belongs to that record ----
        var children = new Dictionary<int, ResolvedChild>();
        for (var index = 0; index < bundle.Items.Count; index++)
        {
            var record = bundle.Items[index];
            if (TryResolveChild(skill, group, record.Item, topics, index + 1, out var child, out var reason))
            {
                children[record.Line] = child;
            }
            else
            {
                refusals[record.Line].Add(new IngestionRefusal(IngestionRules.RecordShape, reason));
            }
        }

        // ---- gate one per child, against the cell it joins ----
        var cells = new Dictionary<(int TopicId, int LevelRank), GateOneCell>();
        foreach (var record in bundle.Items)
        {
            if (!children.TryGetValue(record.Line, out var child))
            {
                continue;
            }

            var cellKey = (child.TopicId, child.Level.Rank);
            if (!cells.TryGetValue(cellKey, out var cell))
            {
                cell = await GateContext.CellAsync(db, child.TopicId, child.Level, exceptItemId: null, cancellationToken);
                cells[cellKey] = cell;
            }

            var verdict = GateOne.Review(
                new GateOneItem(
                    child.Stem,
                    passage ?? transcript,
                    child.Content.OptionA,
                    child.Content.OptionB,
                    child.Content.OptionC,
                    child.Content.OptionD,
                    child.Content.KeyLabel,
                    child.Explanation,
                    child.Content.RationaleA,
                    child.Content.RationaleB,
                    child.Content.RationaleC,
                    child.Content.RationaleD),
                cell);

            refusals[record.Line].AddRange(verdict.Refusals.Select(r => new IngestionRefusal(r.Rule, r.Message)));

            // The bundle's own siblings join one cell, so each child moves the distribution the next
            // is judged against. Counting only stored rows would let four A keys pass a cell that
            // holds one - the same defect the cell rule exists to stop, one save earlier.
            if (child.Content.KeyLabel is { Length: 1 } key)
            {
                cells[cellKey] = cell.Add(key[0]);
            }
        }

        // ---- gate three per child: two blind solves plus the ablation ----
        var gateThree = new GateThreeVerifier(
            verifierOne ?? Unavailable("verifier 1"),
            verifierTwo ?? Unavailable("verifier 2"),
            ablationSolver);

        foreach (var record in bundle.Items)
        {
            if (!children.TryGetValue(record.Line, out var child))
            {
                continue;
            }

            var options = new List<GateThreeOption>();
            AddOption(options, "A", child.Content.OptionA);
            AddOption(options, "B", child.Content.OptionB);
            AddOption(options, "C", child.Content.OptionC);
            AddOption(options, "D", child.Content.OptionD);

            var verdict = await gateThree.ReviewAsync(
                child.AuthorFamily,
                new GateThreeSolveRequest(child.Stem, passage ?? transcript, options),
                child.Content.KeyLabel ?? string.Empty,
                cancellationToken);

            refusals[record.Line].AddRange(verdict.Refusals.Select(r => new IngestionRefusal(r.Rule, r.Message)));
        }

        // ---- gate four: the level claim per child, then span independence over the bundle ----
        foreach (var record in bundle.Items)
        {
            if (!children.TryGetValue(record.Line, out var child))
            {
                continue;
            }

            var verdict = await GateFourIngestion.ReviewLevelAsync(
                child.AuthorFamily,
                child.Level,
                child.Stem,
                classifier,
                cancellationToken);

            refusals[record.Line].AddRange(verdict.Refusals.Select(r => new IngestionRefusal(r.Rule, r.Message)));
        }

        AddToAll(refusals, SpanRefusals(skill, children.Values, passage));

        // A bundle is written whole or not at all, so a record of a refused bundle may not be reported
        // accepted: it would name a row the pool does not hold. Each child of the bundle is told, and
        // the ones that broke no rule are told that the bundle is why.
        if (refusals.Values.Any(list => list.Count > 0))
        {
            var bundleLine = bundle.Line;
            foreach (var record in bundle.Items)
            {
                if (refusals[record.Line].Count == 0)
                {
                    refusals[record.Line].Add(new IngestionRefusal(
                        IngestionRules.RecordShape,
                        $"the stimulus group opening at line {bundleLine} was refused, so this record was not "
                        + "written either: a stimulus and its children enter the pool together or not at all."));
                }
            }
        }

        var records = bundle.Items
            .Select(record => new IngestionRecordResult(
                record.Line,
                record.Item.ItemId ?? string.Empty,
                refusals[record.Line].Count == 0,
                refusals[record.Line]))
            .ToList();

        var stimulus = new PoolImport.StimulusInput(
            group.StimulusLevel,
            Passage: passage,
            AudioPath: audioPath,
            Transcript: transcript,
            ImagePath: group.Content?.ImageUri,
            ImageAlt: group.Content?.ImageAlt,
            DomainContext: group.DomainContext,
            PromptCopy: null);

        return new BundleVerdict(
            bundle,
            records,
            children.Values.OrderBy(c => c.Ordinal).ToList(),
            stimulus,
            stimulusLevel);
    }

    /// <summary>
    /// CNT-10 and CNT-12 over the bundle's children. A listening bundle is judged on time, a reading
    /// one on overlap against the passage's length; the choice is the skill's, because a second-based
    /// span has no "whole passage" quantity and a character span has no audio order.
    /// <para>
    /// <b>The children are passed in their served order, never sorted into evidence order.</b> CNT-10
    /// is a rule about the order the items are <em>served</em> in, so sorting by
    /// <c>SpanStart</c> before judging rewrites the input the rule exists to check - and
    /// <see cref="GateFour.ReviewAudio"/>'s <c>AudioOrder</c> refusal can then never fire from any
    /// caller. It did not fire: #203 found the sort here, which is why the studio's chronological-order
    /// requirement had no enforcement behind it.
    /// </para>
    /// </summary>
    private static IEnumerable<IngestionRefusal> SpanRefusals(
        string skill,
        IEnumerable<ResolvedChild> children,
        string? passage)
    {
        if (string.Equals(skill, "listening", StringComparison.Ordinal))
        {
            var ordered = children
                .OrderBy(c => c.Ordinal)
                .Select((c, index) => new GateFourAudioChild(index + 1, c.TopicCode, c.SpanStart, c.SpanEnd, c.SpanUnit))
                .ToList();

            return GateFour.ReviewAudio(ordered).Select(r => new IngestionRefusal(r.Rule, r.Message));
        }

        var spans = children
            .OrderBy(c => c.Ordinal)
            .Select(c => new GateFourSpan(c.Ordinal, c.TopicCode, c.SpanStart, c.SpanEnd, c.SpanUnit))
            .ToList();

        return GateFour.ReviewSpans(spans, passage?.Length)
            .Select(r => new IngestionRefusal(r.Rule, r.Message));
    }

    /// <summary>
    /// A refusal about the stimulus or the bundle rather than about one child. It reaches every child's
    /// line, because an author with one passage to fix must not be told "the bundle failed" with no
    /// record to act on.
    /// </summary>
    private static void AddToAll(Dictionary<int, List<IngestionRefusal>> refusals, IngestionRefusal refusal)
    {
        foreach (var line in refusals.Keys.ToList())
        {
            refusals[line].Add(new IngestionRefusal(refusal.Rule, $"stimulus or bundle: {refusal.Message}"));
        }
    }

    private static void AddToAll(Dictionary<int, List<IngestionRefusal>> refusals, IEnumerable<IngestionRefusal> bundleRefusals)
    {
        foreach (var refusal in bundleRefusals)
        {
            AddToAll(refusals, refusal);
        }
    }

    /// <summary>Every record refused with one reason, for a failure that makes the bundle unjudgeable.</summary>
    private static BundleVerdict RefuseBundle(
        SkillContentPoolBundle bundle,
        Dictionary<int, List<IngestionRefusal>> refusals,
        string rule,
        string message)
    {
        var records = new List<IngestionRecordResult>(bundle.Items.Count);
        foreach (var record in bundle.Items)
        {
            refusals[record.Line].Add(new IngestionRefusal(rule, message));
            records.Add(new IngestionRecordResult(
                record.Line,
                record.Item.ItemId ?? string.Empty,
                false,
                refusals[record.Line]));
        }

        return new BundleVerdict(bundle, records, [], null, null);
    }

    /// <summary>
    /// CNT-03's machine-checked prefix: <c>stim_demo_…</c>, <c>stim_prod_…</c>, <c>item_demo_…</c>,
    /// <c>item_prod_…</c>. The tier in the id and the tier in <c>content_tier</c> are two statements of
    /// one fact, and a record that disagrees with itself is refused rather than trusted to either.
    /// <para>
    /// <b>The input this guard must not pass.</b> An id of <c>item_demo_x</c> with
    /// <c>content_tier: production</c> looks fine to a reader that trusts the column, and the item then
    /// reaches a paid paper under a name that says demo - the exact breakage INV-6 exists to stop. The
    /// prefix is read from the id, not derived from the column, so the two can disagree and be caught.
    /// </para>
    /// </summary>
    private static (string? Tier, IngestionRefusal? Refusal) CheckTierPrefix(
        string? id,
        string kind,
        string? declaredTier,
        string fieldName)
    {
        var tier = declaredTier?.Trim().ToLowerInvariant();
        if (tier is not ("demo" or "production"))
        {
            return (null, new IngestionRefusal(
                IngestionRules.RecordShape,
                $"{fieldName} '{id ?? "(absent)"}' declares content_tier '{declaredTier}'; the pool holds "
                + "'demo' or 'production' and nothing else, because the tier is what INV-6 reads."));
        }

        var expected = tier == "production" ? "prod" : "demo";
        var pattern = $"^{kind}_{expected}_[a-z0-9_]{{8,32}}$";
        if (id is null || !Regex.IsMatch(id.Trim(), pattern, RegexOptions.CultureInvariant))

        if (id is null || !Regex.IsMatch(id.Trim(), pattern, RegexOptions.CultureInvariant))
        {
            return (tier, new IngestionRefusal(
                IngestionRules.TierPrefix,
                $"{fieldName} '{id ?? "(absent)"}' does not carry its tier's prefix. A record declaring "
                + $"content_tier '{tier}' is named {kind}_{expected}_ followed by 8-32 lowercase "
                + "alphanumerics; the prefix is machine-checked so an id can never say one tier while the "
                + "column says another (CNT-03, INV-6)."));
        }

        return (tier, null);
    }

    /// <summary>
    /// The record as the pool would hold it, or the reason it cannot be held. Nothing here is a gate:
    /// it is the shape <c>Items</c> and <c>ItemVersions</c> require, so a bookkeeping failure is a
    /// sentence rather than a constraint error that would undo the whole save.
    /// </summary>
    internal static bool TryResolveChild(
        string skill,
        SkillContentPoolGroup group,
        SkillContentPoolItem item,
        TopicCatalog topics,
        int ordinal,
        out ResolvedChild child,
        out string reason)
    {
        child = null!;

        if (string.IsNullOrWhiteSpace(item.Stem))
        {
            reason = "stem is required: an item with no question is a row nothing can render.";
            return false;
        }

        if (!CefrLevel.TryParseAuthored(item.ItemLevel, out var level))
        {
            reason = $"item_level '{item.ItemLevel}' is not an authored level (A1-C2). There is no "
                + "difficulty-to-level mapping (D1).";
            return false;
        }

        if (!TryResolveItemType(skill, item, out var itemType, out reason))
        {
            return false;
        }

        var correct = item.CorrectOption?.Trim();
        var options = new List<(string?, string?, bool, string?)>();
        foreach (var option in item.Options ?? [])
        {
            var isCorrect = option.Key is not null
                && string.Equals(option.Key.Trim(), correct, StringComparison.OrdinalIgnoreCase);
            options.Add((option.Key, option.Text, isCorrect, option.DistractorRationale));
        }

        var listening = string.Equals(skill, "listening", StringComparison.Ordinal);
        var spanStart = listening ? item.EvidenceSpan?.StartSec : item.EvidenceSpan?.CharStart;
        var spanEnd = listening ? item.EvidenceSpan?.EndSec : item.EvidenceSpan?.CharEnd;
        var spanUnit = listening ? "sec" : "char";

        // CNT-11: every child of a stimulus stores a span, because that is what makes review able to
        // jump to the evidence and what gate four's overlap rule is computed on. An absent span is not
        // a zero-length one, and gate four would report it as a malformed span rather than as a
        // missing one - so the record is refused here with the rule it actually broke.
        if (spanStart is null || spanEnd is null)
        {
            reason =
                $"the record stores no evidence span. CNT-11 requires every child of a stimulus to carry "
                + $"({(listening ? "start_sec, end_sec" : "char_start, char_end")}), because review jumps to "
                + "the evidence and gate four measures sibling overlap on it. An absent span is not a "
                + "zero-length one.";
            return false;
        }

        if (!PoolImport.TryBuildContent(
                topics,
                skill,
                itemType,
                hasPassage: !string.IsNullOrWhiteSpace(group.Content?.TextOrTranscript),
                topicCode: item.TopicId,
                legacyCategoryCode: null,
                levelCode: level.Code,
                placementEligible: item.PlacementEligible ?? false,
                options,
                spanStart,
                spanEnd,
                spanUnit,
                item.EvidenceSpan?.VerbatimQuote,
                out var content,
                out reason))
        {
            return false;
        }

        child = new ResolvedChild(
            item.ItemId ?? string.Empty,
            content!,
            item.Stem.Trim(),
            item.Explanation,
            level,
            content!.TopicId,
            content.TopicCode,
            spanStart.Value,
            spanEnd.Value,
            spanUnit,
            AuthorFamily(item),
            ordinal);
        return true;
    }

    /// <summary>
    /// D0.4's type, spelled either as its own code or in the format's compound form
    /// (<c>reading_passage_comprehension_mcq</c>). <see cref="PoolImport.TryResolveItemType"/> owns what
    /// a declared type means; the compound spelling is unwrapped here rather than guessed at there,
    /// because the legacy manifest never writes one.
    /// </summary>
    private static bool TryResolveItemType(
        string skill,
        SkillContentPoolItem item,
        out string itemType,
        out string reason)
    {
        var declared = item.ItemType ?? item.QuestionType;
        if (string.IsNullOrWhiteSpace(declared))
        {
            itemType = string.Empty;
            reason =
                "item_type is required: D0.4's fourteen types validate and render differently, so the "
                + "pool cannot derive one from the text for a listening item or a productive task.";
            return false;
        }

        var code = declared.Trim().ToLowerInvariant();
        if (code.StartsWith($"{skill}_", StringComparison.Ordinal))
        {
            code = code[skill.Length..].TrimStart('_');
        }

        if (code.EndsWith("_mcq", StringComparison.Ordinal))
        {
            code = code[..^4];
        }

        itemType = code;
        reason = code.Length == 0 ? "item_type is empty." : string.Empty;
        return code.Length > 0;
    }

    private static string AuthorFamily(SkillContentPoolItem item) =>
        string.IsNullOrWhiteSpace(item.VerificationAudit?.AuthorModel)
            ? UnnamedAuthorFamily
            : item.VerificationAudit!.AuthorModel!.Trim().ToLowerInvariant();

    /// <summary>
    /// The type gate two profiles a stimulus with: its only distinction is <c>sentence_cloze</c> against
    /// everything else, and a stimulus-level reading item that is not a standalone cloze is a passage.
    /// </summary>
    private static string GroupItemType(string skill) =>
        string.Equals(skill, "listening", StringComparison.Ordinal) ? "extended_dialogue" : "passage_comprehension";

    private static void AddOption(List<GateThreeOption> options, string label, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            options.Add(new GateThreeOption(label, text));
        }
    }

    /// <summary>
    /// A verifier that refuses instead of answering: it returns no distribution at all, which
    /// <see cref="GateThree"/> refuses as <c>UnusableDistribution</c>. A dedicated type rather than a
    /// scripted <see cref="FakeVerifierModel"/>, because that fake's unscripted fallback is an even
    /// split - also a refusal, but for a different reason, and "no verifier is configured" is the one
    /// fact the author has to act on.
    /// </summary>
    private sealed class UnavailableVerifier(string name) : IVerifierModel
    {
        public Task<GateThreeSolve> SolveAsync(
            GateThreeSolveRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GateThreeSolve(
                name,
                name,
                new Dictionary<string, decimal>(StringComparer.Ordinal)));
    }

    private static IVerifierModel Unavailable(string name) => new UnavailableVerifier(name);
}
