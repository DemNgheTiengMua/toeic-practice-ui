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
/// #188: authoring writes the pool.
/// <para>
/// An item is created with no exam reference - <see cref="Item"/> has no column to put one in, which
/// is the point of #184 - so there is no "for which paper" anywhere on this path.
/// </para>
/// <para>
/// Every scored change is copy-on-write (CNT-43). <c>trg_ItemVersions_ScoredFieldsImmutable</c>
/// refuses an in-place move of any scored column, so an edit that touches one inserts a new version;
/// only <c>Explanation</c> is written in place, because it is the one column the trigger leaves
/// alone.
/// </para>
/// <para>
/// This service writes no legacy row. <c>Questions</c>/<c>QuestionOptions</c> keep being written by
/// the exam editor, which #231 did not remove; both shapes therefore stay readable.
/// </para>
/// </summary>
public sealed class AdminItemService(CompanionDbContext db, TimeProvider clock) : IAdminItemService
{
    /// <summary>
    /// D0.4's (modality, item type) matrix, mirrored from <c>CK_Items_Modality_Type</c> so a wrong
    /// pair is a sentence rather than a 500 from the database. The constraint remains the authority;
    /// this is only the earlier message.
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> TypesByModality = new(StringComparer.Ordinal)
    {
        ["listening"] = new(StringComparer.Ordinal) { "short_exchange", "extended_dialogue", "monologue_talk" },
        ["reading"] = new(StringComparer.Ordinal) { "sentence_cloze", "passage_comprehension", "text_insertion_cohesion" },
        ["speaking"] = new(StringComparer.Ordinal) { "descriptive_monologue", "situational_negotiation", "argumentative_monologue", "visual_data_synthesis" },
        ["writing"] = new(StringComparer.Ordinal) { "transactional_correspondence", "visual_data_synthesis", "persuasive_essay", "analytical_evaluation_essay" },
    };

    public async Task<PoolItemDto> CreateAsync(CreatePoolItemRequest request, CancellationToken cancellationToken = default)
    {
        var modality = request.Modality.ToCode();
        ValidateItemShape(modality, request.ItemType);
        var level = RequireAuthoredLevel(request.CefrLevelCode);
        ValidateVersionShape(
            request.Stem, request.OptionA, request.OptionB, request.OptionC, request.OptionD, request.KeyLabel,
            request.EvidenceStart, request.EvidenceEnd, request.EvidenceUnit, request.ReferenceText, request.ReferenceKind);

        // The composite key pins the item's modality to its topic's own. A listening item on a
        // reading topic is the defect it exists to stop, so it is refused here with a sentence the
        // author can act on instead of a foreign-key error.
        var topic = await db.Topics
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TopicId == request.TopicId, cancellationToken)
            ?? throw new PoolTopicNotFoundException(request.TopicId);

        if (!string.Equals(topic.Modality, modality, StringComparison.Ordinal))
        {
            throw new AdminItemException(
                ApiError.Codes.Validation,
                $"Topic '{topic.Code}' is a {topic.Modality} topic; a {modality} item cannot sit on it. Pick a topic of the same skill.");
        }

        var stimulus = request.Stimulus is { } stimulusRequest ? BuildStimulus(stimulusRequest) : null;

        if (request.Stimulus is not null && request.StimulusId is not null)
        {
            throw new AdminItemException(
                ApiError.Codes.Validation,
                "Name either a new stimulus or an existing one, not both: a child belongs to one passage.");
        }

        // One read: this row proves the stimulus exists and carries the text gate one's lemma rule
        // compares against, so the gate is handed that text rather than querying for it a second time.
        Stimulus? existingStimulus = null;
        if (request.StimulusId is int existingStimulusId)
        {
            existingStimulus = await GateContext.FindStimulusAsync(db, existingStimulusId, cancellationToken)
                ?? throw new AdminItemException(ApiError.Codes.Validation, $"Stimulus {existingStimulusId} not found.");
        }

        // D17: the gates run server-side, so an item cannot be persisted with no explanation, no
        // rationale or options that give the key away. Gate one is the one that needs no model and no
        // provider call, and it is the gate the review's example failed; gates two to four need a
        // profiler, verifiers and a classifier, and run at the ingestion seam (PoolIngestionService)
        // where those are composed.
        await RequireGateOneAsync(
            GateItem(request, GateContext.TextOf(stimulus) ?? GateContext.TextOf(existingStimulus)),
            request.TopicId,
            level,
            exceptItemId: null,
            "The item was refused by gate one (D17), so nothing was saved: ",
            cancellationToken);

        var item = new Item
        {
            Stimulus = stimulus,
            StimulusId = request.StimulusId,
            Modality = modality,
            TopicId = topic.TopicId,
            CefrLevel = level,
            ItemType = request.ItemType.Trim(),
            PlacementEligible = request.PlacementEligible,
            // Demo and draft are the schema defaults on purpose: an unreviewed item must not reach a
            // paid paper (INV-6), so neither is a request field an author can flip.
            ContentTier = "demo",
            LifecycleStatus = "draft",
        };

        var version = new ItemVersion
        {
            Item = item,
            VersionNumber = 1,
            CreatedAt = clock.GetUtcNow(),
            Stem = request.Stem.Trim(),
            OptionA = Normalize(request.OptionA),
            OptionB = Normalize(request.OptionB),
            OptionC = Normalize(request.OptionC),
            OptionD = Normalize(request.OptionD),
            KeyLabel = NormalizeKey(request.KeyLabel),
            Explanation = Normalize(request.Explanation),
            RationaleA = Normalize(request.RationaleA),
            RationaleB = Normalize(request.RationaleB),
            RationaleC = Normalize(request.RationaleC),
            RationaleD = Normalize(request.RationaleD),
            EvidenceStart = request.EvidenceStart,
            EvidenceEnd = request.EvidenceEnd,
            EvidenceUnit = Normalize(request.EvidenceUnit),
            EvidenceQuote = Normalize(request.EvidenceQuote),
            ReferenceText = Normalize(request.ReferenceText),
            ReferenceKind = request.ReferenceKind?.ToCode(),
        };

        db.Items.Add(item);
        db.ItemVersions.Add(version);
        // One save: an item with no version is a row the pool cannot serve, so the two are written
        // together or not at all.
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(item, version, versionCount: 1, topic.Code);
    }

    public async Task<PoolItemDto> UpdateAsync(int itemId, UpdatePoolItemRequest request, CancellationToken cancellationToken = default)
    {
        ValidateVersionShape(
            request.Stem, request.OptionA, request.OptionB, request.OptionC, request.OptionD, request.KeyLabel,
            request.EvidenceStart, request.EvidenceEnd, request.EvidenceUnit, request.ReferenceText, request.ReferenceKind);

        var item = await db.Items
            .Include(i => i.Topic)
            .FirstOrDefaultAsync(i => i.ItemId == itemId, cancellationToken)
            ?? throw new ItemNotFoundException(itemId);

        // D17: an edit that forks a version is the same door as a create, one save later. A gate that
        // only guarded creation would let an author write a version with no rationale by editing one
        // away, which is the bypass #198's review found wearing a different route. The cell excludes
        // this item, because the balance rule is about the distribution the item makes rather than the
        // one it found.
        await RequireGateOneAsync(
            GateItem(
                request,
                GateContext.TextOf(await GateContext.FindStimulusAsync(db, item.StimulusId, cancellationToken))),
            item.TopicId,
            item.CefrLevel,
            exceptItemId: item.ItemId,
            "The edit was refused by gate one (D17), so nothing was written: ",
            cancellationToken);

        var versions = await db.ItemVersions
            .Where(v => v.ItemId == itemId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(cancellationToken);

        var current = versions.FirstOrDefault()
            ?? throw new AdminItemException(ApiError.Codes.Validation, $"Item {itemId} has no version to edit.", 409);

        // CNT-43: a scored change forks a version; a cosmetic one does not. Comparing the incoming
        // scored columns against the stored ones makes that decision structural rather than a flag
        // the caller sets - a caller who could say "this is cosmetic" could rewrite history.
        if (ChangesScoredContent(current, request))
        {
            var next = new ItemVersion
            {
                ItemId = item.ItemId,
                VersionNumber = current.VersionNumber + 1,
                CreatedAt = clock.GetUtcNow(),
                Stem = request.Stem.Trim(),
                OptionA = Normalize(request.OptionA),
                OptionB = Normalize(request.OptionB),
                OptionC = Normalize(request.OptionC),
                OptionD = Normalize(request.OptionD),
                KeyLabel = NormalizeKey(request.KeyLabel),
                Explanation = Normalize(request.Explanation),
                RationaleA = Normalize(request.RationaleA),
                RationaleB = Normalize(request.RationaleB),
                RationaleC = Normalize(request.RationaleC),
                RationaleD = Normalize(request.RationaleD),
                EvidenceStart = request.EvidenceStart,
                EvidenceEnd = request.EvidenceEnd,
                EvidenceUnit = Normalize(request.EvidenceUnit),
                EvidenceQuote = Normalize(request.EvidenceQuote),
                ReferenceText = Normalize(request.ReferenceText),
                ReferenceKind = request.ReferenceKind?.ToCode(),
            };
            db.ItemVersions.Add(next);
            await db.SaveChangesAsync(cancellationToken);

            return ToDto(item, next, versions.Count + 1, item.Topic.Code);
        }

        // Only the explanation (or nothing) moved: the one in-place edit the trigger allows.
        current.Explanation = Normalize(request.Explanation);
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(item, current, versions.Count, item.Topic.Code);
    }

    /// <summary>
    /// D17 at both authoring doors: gate one, against the cell the item is joining.
    /// <para>
    /// The review that opened #198 proved this path could persist an item with three options, no
    /// explanation and no rationale - every rule gate one states, and no caller of it. A refusal is an
    /// <see cref="AdminItemException"/> rather than a returned verdict, because this path already
    /// answers with one and a caller that ignored a verdict would be the same bypass again.
    /// </para>
    /// <para>
    /// <paramref name="refusalPrefix"/> is the one thing the two doors say differently: a save that
    /// wrote nothing and an edit that forked nothing are two sentences. Everything else - the cell,
    /// the item, the gate, the exception type and the rule list - is shared, so an edit cannot come to
    /// judge an item differently from a create.
    /// </para>
    /// </summary>
    private async Task RequireGateOneAsync(
        GateOneItem gateItem,
        int topicId,
        CefrLevel level,
        int? exceptItemId,
        string refusalPrefix,
        CancellationToken cancellationToken)
    {
        // excludeItemId is what makes the balance rule answerable at each door: a create has nothing to
        // exclude yet, and an edit must exclude itself or its own key would vouch for its own balance.
        var cell = await GateContext.CellAsync(db, topicId, level, exceptItemId, cancellationToken);

        var verdict = GateOne.Review(gateItem, cell);
        if (verdict.Accepted)
        {
            return;
        }

        throw new AdminItemException(
            ApiError.Codes.Validation,
            refusalPrefix + string.Join(" ", verdict.Refusals.Select(r => $"[{r.Rule}] {r.Message}")));
    }

    /// <summary>
    /// The create request as the gate sees it. <paramref name="stimulusText"/> is the passage,
    /// transcript or prompt copy of the stimulus the item is authored with or joins - gate one's lemma
    /// rule compares the stem and key against the text the learner will read, and a rule that skipped
    /// it whenever the passage came from a second row would judge nothing on a shared passage.
    /// </summary>
    private static GateOneItem GateItem(CreatePoolItemRequest request, string? stimulusText) =>
        new(
            request.Stem,
            stimulusText,
            Normalize(request.OptionA), Normalize(request.OptionB),
            Normalize(request.OptionC), Normalize(request.OptionD),
            NormalizeKey(request.KeyLabel),
            Normalize(request.Explanation),
            Normalize(request.RationaleA), Normalize(request.RationaleB),
            Normalize(request.RationaleC), Normalize(request.RationaleD));

    /// <summary>
    /// The edit request as the gate sees it. The stimulus is not editable through this request, so its
    /// text is read from the item rather than from the edit.
    /// </summary>
    private static GateOneItem GateItem(UpdatePoolItemRequest request, string? stimulusText) =>
        new(
            request.Stem,
            stimulusText,
            Normalize(request.OptionA), Normalize(request.OptionB),
            Normalize(request.OptionC), Normalize(request.OptionD),
            NormalizeKey(request.KeyLabel),
            Normalize(request.Explanation),
            Normalize(request.RationaleA), Normalize(request.RationaleB),
            Normalize(request.RationaleC), Normalize(request.RationaleD));

    private static Stimulus BuildStimulus(CreatePoolStimulusRequest request)
    {
        var level = RequireAuthoredLevel(request.CefrLevelCode);
        var imagePath = Normalize(request.ImagePath);
        var imageAlt = Normalize(request.ImageAlt);

        if (imagePath is not null && imageAlt is null)
        {
            throw new AdminItemException(
                ApiError.Codes.Validation,
                "A stimulus image needs alt text (UI-5): an undescribed image is inaccessible.");
        }

        return new Stimulus
        {
            CefrLevel = level,
            Passage = Normalize(request.Passage),
            AudioPath = Normalize(request.AudioPath),
            Transcript = Normalize(request.Transcript),
            DomainContext = string.IsNullOrWhiteSpace(request.DomainContext) ? "general" : request.DomainContext.Trim(),
            PromptCopy = Normalize(request.PromptCopy),
            ImagePath = imagePath,
            ImageAlt = imageAlt,
            PrepSeconds = request.PrepSeconds,
            ResponseSeconds = request.ResponseSeconds,
        };
    }

    /// <summary>
    /// True when any scored column differs. Explanation is excluded here and written by the caller,
    /// and #194's rationales are scored with the option they explain - see <see cref="ItemVersion"/>.
    /// because it is the column whose change must <em>not</em> fork a version.
    /// </summary>
    private static bool ChangesScoredContent(ItemVersion current, UpdatePoolItemRequest request) =>
        !string.Equals(current.Stem, request.Stem?.Trim(), StringComparison.Ordinal)
        || !Same(current.OptionA, request.OptionA)
        || !Same(current.OptionB, request.OptionB)
        || !Same(current.OptionC, request.OptionC)
        || !Same(current.OptionD, request.OptionD)
        || !Same(current.RationaleA, request.RationaleA)
        || !Same(current.RationaleB, request.RationaleB)
        || !Same(current.RationaleC, request.RationaleC)
        || !Same(current.RationaleD, request.RationaleD)
        || !Same(current.KeyLabel, NormalizeKey(request.KeyLabel))
        || current.EvidenceStart != request.EvidenceStart
        || current.EvidenceEnd != request.EvidenceEnd
        || !Same(current.EvidenceUnit, request.EvidenceUnit)
        || !Same(current.EvidenceQuote, request.EvidenceQuote)
        || !Same(current.ReferenceText, request.ReferenceText)
        || !Same(current.ReferenceKind, request.ReferenceKind?.ToCode());

    private static bool Same(string? stored, string? incoming) =>
        string.Equals(Normalize(stored), Normalize(incoming), StringComparison.Ordinal);

    /// <summary>
    /// Mirrors <c>CK_ItemVersions_OptionsComplete</c>, the key and evidence CHECKs, and the reference
    /// pair. The schema stays the authority; failing here only replaces a 500 with a sentence.
    /// </summary>
    private static void ValidateVersionShape(
        string stem,
        string? optionA,
        string? optionB,
        string? optionC,
        string? optionD,
        string? keyLabel,
        int? evidenceStart,
        int? evidenceEnd,
        string? evidenceUnit,
        string? referenceText,
        ReferenceKindDto? referenceKind)
    {
        if (string.IsNullOrWhiteSpace(stem))
        {
            throw new AdminItemException(ApiError.Codes.Validation, "An item version needs a stem.");
        }

        var options = new[] { optionA, optionB, optionC, optionD }.Select(Normalize).ToList();
        var filled = options.Count(option => option is not null);
        var key = NormalizeKey(keyLabel);

        if ((filled != 0 && filled != 4) || (filled == 4) != (key is not null))
        {
            throw new AdminItemException(
                ApiError.Codes.Validation,
                "A version is either four options with a key, or no options and no key (CNT-06). A half-filled item measures nothing.");
        }

        if (key is not null && key is not ("A" or "B" or "C" or "D"))
        {
            throw new AdminItemException(ApiError.Codes.Validation, "The key must be one of A, B, C or D.");
        }

        var hasUnit = Normalize(evidenceUnit) is not null;
        if ((evidenceStart is null) != (evidenceEnd is null) || hasUnit != (evidenceStart is not null))
        {
            throw new AdminItemException(
                ApiError.Codes.Validation,
                "An evidence span needs start, end and its unit together ('char' for text, 'sec' for audio).");
        }

        if (evidenceStart is int start && evidenceEnd is int end && (start < 0 || end <= start))
        {
            throw new AdminItemException(ApiError.Codes.Validation, "An evidence span's end must be after its start.");
        }

        if ((Normalize(referenceText) is null) != (referenceKind is null))
        {
            throw new AdminItemException(
                ApiError.Codes.Validation,
                "A spoken reference needs both the text and what it is ('script' or 'image_desc'). Supply both or neither.");
        }
    }

    private static void ValidateItemShape(string modality, string itemType)
    {
        if (!TypesByModality.TryGetValue(modality, out var allowed))
        {
            throw new AdminItemException(ApiError.Codes.Validation, $"'{modality}' is not a skill.");
        }

        if (!allowed.Contains(itemType?.Trim() ?? string.Empty))
        {
            throw new AdminItemException(
                ApiError.Codes.Validation,
                $"'{itemType}' is not one of the {modality} item types. A {modality} item is one of: {string.Join(", ", allowed)}.");
        }
    }

    private static CefrLevel RequireAuthoredLevel(string? code) =>
        CefrLevel.TryParseAuthored(code, out var level)
            ? level
            : throw new AdminItemException(
                ApiError.Codes.Validation,
                $"'{code}' is not an authored CEFR level. Content is authored at A1-C2; the pre-A1 floor is reportable only.");

    private static string? NormalizeKey(string? keyLabel) =>
        string.IsNullOrWhiteSpace(keyLabel) ? null : keyLabel.Trim().ToUpperInvariant();

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static PoolItemDto ToDto(Item item, ItemVersion version, int versionCount, string topicCode) =>
        new(
            item.ItemId,
            version.ItemVersionId,
            version.VersionNumber,
            versionCount,
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
