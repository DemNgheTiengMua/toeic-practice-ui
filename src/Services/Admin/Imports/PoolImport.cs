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
/// #189: the import paths write the pool.
/// <para>
/// One place owns the three decisions every import makes about a pool write: which canonical topic a
/// legacy category code means, which of D0.4's fourteen item types the content actually is, and how a
/// version's four options and key are read off a row authored for the legacy shape. Both import routes
/// and both exports call it, so a file cannot round-trip differently through one door than another.
/// </para>
/// <para>
/// Nothing here names an exam: an imported item is pool stock the moment it is created. Putting it on
/// the paper being imported into is a separate row (<c>ExamItems</c>), written by the importer after
/// the item exists - never an ownership column.
/// </para>
/// </summary>
internal static class PoolImport
{
    /// <summary>
    /// An imported item is demo and draft, the schema's own defaults. Import must not mint production
    /// stock: INV-6 keeps it out of a paid paper until a review gate promotes it, and a flag a caller
    /// could set is how unreviewed content goes on sale.
    /// <para>
    /// #205: <see cref="LifecycleStatus"/> stays <c>draft</c> - "not yet judged". The ingestion seam,
    /// which has run all four gates before it writes, passes <see cref="ValidatedLifecycleStatus"/>
    /// instead: that record <em>was</em> judged, and calling it a draft would be the false statement.
    /// Neither is <c>active</c>; selection filters on that, so a gate pass is not a publication.
    /// </para>
    /// </summary>
    internal const string ContentTier = "demo";
    internal const string LifecycleStatus = "active";

    /// <summary>
    /// #205: the staged state - the four gates judged this record, and the audit has not cleared it.
    /// CNT-42's <c>draft -> validated -> active</c>, where this is the middle one.
    /// </summary>
    internal const string ValidatedLifecycleStatus = "validated";

    /// <summary>
    /// The legacy category codes, mapped to the topic that measures the same construct.
    /// <para>
    /// Keyed by modality as well as code, because the topics are: <c>ielts_detail</c> is factual
    /// retrieval whether its section is reading or listening, and those are different topics
    /// (<c>R-DET</c>, <c>L-DET</c>). A code with no row here is refused with the canonical list rather
    /// than guessed at, and every value is one of the twenty -
    /// <c>trg_Topics_ClosedVocabulary</c> would refuse anything else, so a typo in this table is a
    /// failing test rather than a silent twenty-first topic.
    /// </para>
    /// <para>
    /// Why map them at all when D3 replaces the categories: a legacy manifest's category code is the
    /// only topic-like label it carries, and CNT-05 calls those codes "arbitrary surface labels". The
    /// table below is a reading of what each one measured, not a claim the category still exists.
    /// </para>
    /// </summary>
    private static readonly (string Modality, string LegacyCode, string TopicCode)[] LegacyCategoryTopics =
    [
        // Reading grammar: six labels for one construct - morphosyntactic structure and lexical
        // precision - so they land on R-GRAM together.
        ("reading", "grammar_tense", "R-GRAM"),
        ("reading", "grammar_prep", "R-GRAM"),
        ("reading", "grammar_pos", "R-GRAM"),
        ("reading", "grammar_rel", "R-GRAM"),
        ("reading", "grammar_conj", "R-GRAM"),
        ("reading", "reading_vocab", "R-GRAM"),
        ("reading", "vstep_grammar", "R-GRAM"),
        ("reading", "vstep_vocab", "R-GRAM"),
        // A reading inference bucket is bridging inference, not detail retrieval.
        ("reading", "reading_inference", "R-INF"),
        ("reading", "ielts_inference", "R-INF"),
        ("reading", "ielts_detail", "R-DET"),
        ("reading", "vstep_detail", "R-DET"),
        ("reading", "ielts_main_idea", "R-GIST"),
        // Listening: a photograph item asks what is happening in the picture - explicit detail, the
        // same construct as a spoken detail question.
        ("listening", "listening_photo", "L-DET"),
        ("listening", "listening_detail", "L-DET"),
        ("listening", "ielts_detail", "L-DET"),
        ("listening", "ielts_inference", "L-INF"),
    ];

    private static readonly Dictionary<(string Modality, string Code), string> TopicsByLegacyCode =
        LegacyCategoryTopics.ToDictionary(
            row => (row.Modality, row.LegacyCode),
            row => row.TopicCode,
            new ModalityCodeComparer());

    /// <summary>
    /// D0.4's (modality, item type) matrix, mirrored from <c>CK_Items_Modality_Type</c>. The
    /// constraint remains the authority; this only turns a wrong pair into a sentence.
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> ItemTypesByModality = new(StringComparer.Ordinal)
    {
        ["listening"] = new(StringComparer.Ordinal) { "short_exchange", "extended_dialogue", "monologue_talk" },
        ["reading"] = new(StringComparer.Ordinal) { "sentence_cloze", "passage_comprehension", "text_insertion_cohesion" },
        ["speaking"] = new(StringComparer.Ordinal) { "descriptive_monologue", "situational_negotiation", "argumentative_monologue", "visual_data_synthesis" },
        ["writing"] = new(StringComparer.Ordinal) { "transactional_correspondence", "visual_data_synthesis", "persuasive_essay", "analytical_evaluation_essay" },
    };

    /// <summary>
    /// The topic an item of this type measures when the file names none. D0.4 already binds each type
    /// to a topic set, so this is the most literal member of that set - never an invented topic. A
    /// <c>passage_comprehension</c> with no label is read as detail retrieval because that is the
    /// least interpretive thing a passage question can ask.
    /// </summary>
    internal static string DefaultTopicFor(string modality, string itemType) => (modality, itemType) switch
    {
        ("reading", "sentence_cloze") => "R-GRAM",
        ("reading", "text_insertion_cohesion") => "R-COH",
        ("reading", _) => "R-DET",
        ("listening", "short_exchange") => "L-FUNC",
        ("listening", _) => "L-DET",
        ("speaking", "descriptive_monologue") => "S-DESC",
        ("speaking", "situational_negotiation") => "S-INT",
        ("speaking", "argumentative_monologue") => "S-ARG",
        ("speaking", "visual_data_synthesis") => "S-SYN",
        ("writing", "transactional_correspondence") => "W-CORR",
        ("writing", "visual_data_synthesis") => "W-DATA",
        ("writing", "persuasive_essay") => "W-ARG",
        ("writing", _) => "W-DISC",
        _ => string.Empty,
    };

    /// <summary>
    /// #250: the render format a modality implies when the caller names none. A productive item's only
    /// legal widget is a free response (CK_Items_Modality_RenderFormat), and an objective item's
    /// common one is the four-option radio group. Naming it here rather than defaulting to 'mcq' in
    /// the column is what stops a writing task being written as a radio group by omission.
    /// </summary>
    internal static string DefaultRenderFormat(string modality) =>
        modality is "speaking" or "writing" ? "free_response" : "mcq";

    /// <summary>
    /// D0.4's fourteen types, derived where the content determines the type and required where it
    /// does not. A reading group with no passage is D0.4's standalone <c>sentence_cloze</c>; one with a
    /// passage is <c>passage_comprehension</c>. A listening stimulus's type turns on its speaker and
    /// turn counts, which no manifest carries, and a productive task's type is a choice rather than a
    /// reading of its text - so those must be named.
    /// </summary>
    internal static bool TryResolveItemType(        string modality,
        string? declared,
        bool hasPassage,
        out string itemType,
        out string reason)
    {
        itemType = string.Empty;
        reason = string.Empty;

        if (!string.IsNullOrWhiteSpace(declared))
        {
            itemType = declared.Trim();
            return true;
        }

        if (modality == "reading")
        {
            itemType = hasPassage ? "passage_comprehension" : "sentence_cloze";
            return true;
        }

        reason = modality switch
        {
            "listening" =>
                "itemType is required for a listening item: its type follows the number of speakers and turns, which the file does not carry. Name short_exchange, extended_dialogue or monologue_talk.",
            "speaking" =>
                "itemType is required for a speaking task: its type is a choice, not a reading of its prompt. Name descriptive_monologue, situational_negotiation, argumentative_monologue or visual_data_synthesis.",
            "writing" =>
                "itemType is required for a writing task: its type is a choice, not a reading of its prompt. Name transactional_correspondence, visual_data_synthesis, persuasive_essay or analytical_evaluation_essay.",
            _ => $"'{modality}' is not a skill, so no pool item can be built.",
        };
        return false;
    }

    /// <summary>
    /// CNT-06: a pool item is four options with one key, or a productive task with none. The legacy
    /// shapes the importer still accepts - two or three options, multi-select, inline completion,
    /// dropdown, drag-drop - have no pool form, because a second option count would move the 25%
    /// guessing floor that stage-2 sizing and the mastery threshold are derived from. Those return
    /// false with a reason, and the legacy import continues: this is the expand half, not a
    /// replacement.
    /// </summary>
    internal static bool TryBuildOptions(
        string modality,
        IReadOnlyList<(string? Label, string? Text, bool IsCorrect, string? Rationale)> options,
        out string? optionA,
        out string? optionB,
        out string? optionC,
        out string? optionD,
        out string? keyLabel,
        out string? rationaleA,
        out string? rationaleB,
        out string? rationaleC,
        out string? rationaleD,
        out string reason)
    {
        optionA = optionB = optionC = optionD = keyLabel = null;
        rationaleA = rationaleB = rationaleC = rationaleD = null;
        reason = string.Empty;

        if (options.Count == 0)
        {
            if (modality is "speaking" or "writing")
            {
                return true; // the productive shape: no options, no key
            }

            reason = $"a {modality} item has no productive shape, so it needs four options and one key (CNT-06).";
            return false;
        }

        if (options.Count != 4)
        {
            reason =
                $"the pool holds four-option single-best-answer items, so a question with {options.Count} options has no pool form (CNT-06).";
            return false;
        }

        var byLabel = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var rationales = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (label, text, _, rationale) in options)
        {
            var clean = label?.Trim().ToUpperInvariant();
            if (clean is not ("A" or "B" or "C" or "D"))
            {
                reason = $"a pool item's options are labelled A, B, C and D; '{label}' is not.";
                return false;
            }

            if (!byLabel.TryAdd(clean, text?.Trim()))
            {
                reason = $"option label '{clean}' is used twice.";
                return false;
            }

            rationales[clean] = Normalise(rationale);
        }

        if (byLabel.Count != 4 || byLabel.Values.Any(string.IsNullOrWhiteSpace))
        {
            reason = "a pool item needs all four options A-D, each with text.";
            return false;
        }

        var keys = options.Where(o => o.IsCorrect).Select(o => o.Label!.Trim().ToUpperInvariant()).ToList();
        // #250: a multi_select marks two or more options correct. Requiring exactly one made the
        // paper's own multi_select items unrepresentable, which is why the (topic, level) cell could
        // hold them and the pool could not. The key is a SET; a single label is a set of one.
        if (keys.Count == 0)
        {
            reason = "a pool item with options marks none of them correct, so it has no key.";
            return false;
        }

        if (keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != keys.Count)
        {
            reason = $"option labels are used twice in this item's key: {string.Join(",", keys)}.";
            return false;
        }

        optionA = byLabel["A"];
        optionB = byLabel["B"];
        optionC = byLabel["C"];
        optionD = byLabel["D"];
        // Canonical, so 'C,A' and 'A,C' are the same answer and a re-import is not a change.
        keyLabel = BusinessObjects.KeyLabelSet.From(keys);
        rationaleA = rationales["A"];
        rationaleB = rationales["B"];
        rationaleC = rationales["C"];
        rationaleD = rationales["D"];
        return true;
    }

    /// <summary>
    /// Everything a pool write needs, or the reason it cannot be made. The reasons are returned rather
    /// than thrown because a legacy shape that has no pool form is not a broken file - it is a file
    /// whose content the pool cannot hold yet, and the batch records it as a warning and carries on.
    /// <para>
    /// #204: <paramref name="subTask"/> is the productive task's own output demand and windows. It is
    /// a single object rather than four more parameters because it is one fact - "this part is N-M
    /// words, prepared for P seconds and answered in R" - and it is null for every objective item,
    /// which is the shape the 2.0.0 upload format writes.
    /// </para>
    /// </summary>
    internal static bool TryBuildContent(
        TopicCatalog topics,
        string modality,
        string? declaredItemType,
        bool hasPassage,
        string? topicCode,
        string? legacyCategoryCode,
        string? levelCode,
        bool placementEligible,
        IReadOnlyList<(string? Label, string? Text, bool IsCorrect, string? Rationale)> options,
        int? evidenceStart,
        int? evidenceEnd,
        string? evidenceUnit,
        string? evidenceQuote,
        out PoolContent? content,
        out string reason,
        ProductiveOutput? subTask = null,
        string? renderFormat = null,
        // #266: a hint is readable content and the other two are how a blank is judged. They were
        // carried only by the legacy question row, so an import that stopped writing that row lost
        // them silently.
        string? hint = null,
        string? acceptedAnswersJson = null,
        string? requiredKeywords = null,
        int? maxSelections = null,
        bool allowNumber = true)
    {
        content = null;

        if (!TryBuildOptions(modality, options, out var a, out var b, out var c, out var d, out var key,
                out var rationaleA, out var rationaleB, out var rationaleC, out var rationaleD, out reason))
        {
            return false;
        }

        if (!CefrLevel.TryParseAuthored(levelCode, out var level))
        {
            reason =
                $"cefrLevel '{levelCode}' is not an authored level (A1-C2). The pool is levelled by CEFR only; there is no difficulty-to-level mapping (D1).";
            return false;
        }

        if (!TryResolveItemType(modality, declaredItemType, hasPassage, out var itemType, out reason))
        {
            return false;
        }

        if (!ItemTypesByModality.TryGetValue(modality, out var allowedTypes) || !allowedTypes.Contains(itemType))
        {
            // CK_Items_Modality_Type. Naming a pool type that belongs to another skill is the same
            // defect as a listening item on a reading topic, one level up.
            reason =
                $"'{itemType}' is not a {modality} item type. A {modality} item is one of: " +
                (allowedTypes is null ? "(no types)" : string.Join(", ", allowedTypes)) + ".";
            return false;
        }

        string? resolvedTopicCode;
        if (!string.IsNullOrWhiteSpace(topicCode))
        {
            resolvedTopicCode = topicCode.Trim();
            if (!topics.TryFind(modality, resolvedTopicCode, out _))
            {
                reason =
                    $"topicCode '{resolvedTopicCode}' is not a {modality} topic. The vocabulary is a closed set of twenty: {topics.Canonical(modality)}.";
                return false;
            }
        }
        else if (!string.IsNullOrWhiteSpace(legacyCategoryCode))
        {
            if (!TopicsByLegacyCode.TryGetValue((modality, legacyCategoryCode.Trim()), out resolvedTopicCode!))
            {
                reason =
                    $"categoryCode '{legacyCategoryCode}' maps to no {modality} topic. The legacy categories are replaced (D3), so name topicCode instead - one of: {topics.Canonical(modality)}.";
                return false;
            }
        }
        else
        {
            resolvedTopicCode = DefaultTopicFor(modality, itemType);
        }

        if (!topics.TryFind(modality, resolvedTopicCode, out var topicId))
        {
            reason = $"topicCode '{resolvedTopicCode}' is not a {modality} topic.";
            return false;
        }

        var hasUnit = !string.IsNullOrWhiteSpace(evidenceUnit);
        if ((evidenceStart is null) != (evidenceEnd is null) || hasUnit != (evidenceStart is not null))
        {
            reason = "an evidence span needs start, end and its unit together ('char' for text, 'sec' for audio).";
            return false;
        }

        if (hasUnit && Normalise(evidenceUnit) is not ("char" or "sec"))
        {
            // CK_ItemVersions_EvidenceUnit. Caught here so an author's typo is a sentence rather than
            // a constraint error that rolls the whole batch back.
            reason = $"evidenceUnit '{evidenceUnit}' is not a unit the pool knows; use 'char' for text or 'sec' for audio.";
            return false;
        }

        if (evidenceStart is int start && evidenceEnd is int end && (start < 0 || end <= start))
        {
            reason = "an evidence span's end must be after its start.";
            return false;
        }

        content = new PoolContent(
            modality,
            topicId,
            resolvedTopicCode,
            level,
            itemType,
            placementEligible,
            // #250: which widget this item renders as. The caller may know it exactly (the exam
            // importer reads the legacy QuestionType, which IS the render format); otherwise it is
            // derived from the modality, which is the only value a productive item can legally take.
            renderFormat ?? DefaultRenderFormat(modality),
            a, b, c, d, key,
            rationaleA, rationaleB, rationaleC, rationaleD,
            evidenceStart,
            evidenceEnd,
            Normalise(evidenceUnit),
            Normalise(evidenceQuote),
            subTask?.MinWords,
            subTask?.MaxWords,
            subTask?.PrepSeconds,
            subTask?.ResponseSeconds,
            Normalise(hint),
            Normalise(acceptedAnswersJson),
            Normalise(requiredKeywords),
            maxSelections,
            allowNumber);
        return true;
    }

    /// <summary>
    /// #204: a productive part's output demand and its two timed windows. All four are optional
    /// individually - a speaking part declares no word range, a writing part no preparation - and
    /// <see cref="BusinessObjects.ProductiveTaskEnvelope"/> is what judges whether the set is legal.
    /// </summary>
    internal sealed record ProductiveOutput(
        int? MinWords = null,
        int? MaxWords = null,
        int? PrepSeconds = null,
        int? ResponseSeconds = null);

    /// <summary>
    /// #266: a question the pool cannot express. Thrown rather than returned as a note, because a pack
    /// that imported "most of" its questions would look successful and leave a paper short by an amount
    /// only a log knows. The message names the question and the reason, so the author fixes the pack.
    /// </summary>
    internal sealed class RefusedException(string message) : Exception(message);

    /// <summary>
    /// The stimulus a group's children share: one row per group, never one per child. A passage
    /// duplicated across its children is the shape D0.3's envelope and the shared-audio volume both
    /// assume did not happen.
    /// </summary>
    internal static Stimulus BuildStimulus(StimulusInput input, CefrLevel level, int displayOrder) =>
        new()
        {
            CefrLevel = level,
            Passage = Normalise(input.Passage),
            AudioPath = Normalise(input.AudioPath),
            Transcript = Normalise(input.Transcript),
            ImagePath = Normalise(input.ImagePath),
            ImageAlt = Normalise(input.ImageAlt),
            DomainContext = string.IsNullOrWhiteSpace(input.DomainContext) ? "general" : input.DomainContext.Trim(),
            PromptCopy = Normalise(input.PromptCopy),
            PrepSeconds = input.PrepSeconds,
            ResponseSeconds = input.ResponseSeconds,
            DisplayOrder = displayOrder,
        };

    /// <summary>The passage, audio or prompt a group's children share. One instance per group.</summary>
    internal sealed record StimulusInput(
        string? LevelCode,
        string? Passage = null,
        string? AudioPath = null,
        string? Transcript = null,
        string? ImagePath = null,
        string? ImageAlt = null,
        string? DomainContext = null,
        string? PromptCopy = null,
        int? PrepSeconds = null,
        int? ResponseSeconds = null)
    {
        /// <summary>
        /// A stimulus is written only when the group carries something: an empty one is noise the pool
        /// would carry forever. NULL on the item is legal - "authored before its stimulus is attached".
        /// </summary>
        public bool CarriesContent =>
            !string.IsNullOrWhiteSpace(Passage)
            || !string.IsNullOrWhiteSpace(AudioPath)
            || !string.IsNullOrWhiteSpace(Transcript)
            || !string.IsNullOrWhiteSpace(ImagePath)
            || !string.IsNullOrWhiteSpace(PromptCopy);
    }

    /// <summary>
    /// One pool item and its first version, built but not saved. The caller adds both and saves once,
    /// so a batch that fails halfway leaves no item without a version.
    /// </summary>
    internal static (Item Item, ItemVersion Version) BuildItem(
        int topicId,
        PoolContent content,
        string stem,
        string? explanation,
        string? referenceText,
        string? referenceKind,
        Stimulus? stimulus,
        string? batchTag,
        DateTimeOffset now,
        string? lifecycleStatus = null,
        string? targetBands = null)
    {
        var item = new Item
        {
            // The navigation, not the id: one stimulus instance shared by every child of its group is
            // one row, and EF writes it before the children because the FK needs its id.
            Stimulus = stimulus,
            Modality = content.Modality,
            TopicId = topicId,
            CefrLevel = content.Level,
            ItemType = content.ItemType,
            RenderFormat = content.RenderFormat,
            PlacementEligible = content.PlacementEligible,
            ContentTier = ContentTier,
            LifecycleStatus = lifecycleStatus ?? LifecycleStatus,
            TargetBands = TargetBandsNormalizer.Normalize(targetBands),
            BatchTag = batchTag is { Length: > 60 } ? batchTag[..60] : batchTag,
            CreatedAt = now,
        };

        var version = new ItemVersion
        {
            Item = item,
            VersionNumber = 1,
            CreatedAt = now,
            Stem = stem.Trim(),
            OptionA = content.OptionA,
            OptionB = content.OptionB,
            OptionC = content.OptionC,
            OptionD = content.OptionD,
            KeyLabel = content.KeyLabel,
            Explanation = Normalise(explanation),
            RationaleA = content.RationaleA,
            RationaleB = content.RationaleB,
            RationaleC = content.RationaleC,
            RationaleD = content.RationaleD,
            EvidenceStart = content.EvidenceStart,
            EvidenceEnd = content.EvidenceEnd,
            EvidenceUnit = content.EvidenceUnit,
            EvidenceQuote = content.EvidenceQuote,
            ReferenceText = Normalise(referenceText),
            ReferenceKind = Normalise(referenceKind),
            // #204: the productive part's own demand and windows.
            MinWords = content.MinWords,
            MaxWords = content.MaxWords,
            PrepSeconds = content.PrepSeconds,
            ResponseSeconds = content.ResponseSeconds,
            Hint = content.Hint,
            AcceptedAnswersJson = content.AcceptedAnswersJson,
            RequiredKeywords = content.RequiredKeywords,
            MaxSelections = content.MaxSelections,
            AllowNumber = content.AllowNumber,
        };

        return (item, version);
    }

    /// <summary>
    /// The pool content of every position the paper has bound, keyed by (section code, 1-based position
    /// within that section) - the join <c>AttemptEngine</c> and the preview already use, because a
    /// binding names no <c>QuestionId</c> and position is the only thing the two shapes share.
    /// <para>
    /// An empty result is a legacy paper: one with no binding writes its export exactly as before.
    /// </para>
    /// </summary>
    internal static async Task<Dictionary<(string SectionCode, int Position), PoolQuestionAnnotation>> LoadPaperAnnotationsAsync(
        CompanionDbContext db,
        int examId,
        CancellationToken cancellationToken)
    {
        var annotations = new Dictionary<(string, int), PoolQuestionAnnotation>();

        var bindings = await db.ExamItems.AsNoTracking()
            .Where(b => b.ExamId == examId)
            .ToListAsync(cancellationToken);
        if (bindings.Count == 0)
        {
            return annotations;
        }

        var versionIds = bindings.Select(b => b.ItemVersionId).Distinct().ToList();
        var versions = (await db.ItemVersions.AsNoTracking()
                .Where(v => versionIds.Contains(v.ItemVersionId))
                .ToListAsync(cancellationToken))
            .ToDictionary(v => v.ItemVersionId);

        var itemIds = bindings.Select(b => b.ItemId).Distinct().ToList();
        var items = (await db.Items.AsNoTracking()
                .Where(i => itemIds.Contains(i.ItemId))
                .Select(i => new
                {
                    i.ItemId,
                    i.ItemType,
                    i.RenderFormat,
                    i.CefrLevel,
                    i.PlacementEligible,
                    i.StimulusId,
                    i.TopicId,
                })
                .ToListAsync(cancellationToken))
            .ToDictionary(i => i.ItemId);

        var topicIds = items.Values.Select(i => i.TopicId).Distinct().ToList();
        var topicCodes = (await db.Topics.AsNoTracking()
                .Where(t => topicIds.Contains(t.TopicId))
                .Select(t => new { t.TopicId, t.Code })
                .ToListAsync(cancellationToken))
            .ToDictionary(t => t.TopicId, t => t.Code);

        var sectionIds = bindings.Select(b => b.SectionId).Distinct().ToList();
        var sectionCodes = (await db.Sections.AsNoTracking()
                .Where(s => sectionIds.Contains(s.SectionId))
                .Select(s => new { s.SectionId, s.Code })
                .ToListAsync(cancellationToken))
            .ToDictionary(s => s.SectionId, s => s.Code);

        var stimulusIds = items.Values
            .Where(i => i.StimulusId.HasValue)
            .Select(i => i.StimulusId!.Value)
            .Distinct()
            .ToList();
        var stimuli = (await db.Stimuli.AsNoTracking()
                .Where(s => stimulusIds.Contains(s.StimulusId))
                .ToListAsync(cancellationToken))
            .ToDictionary(s => s.StimulusId);

        foreach (var binding in bindings)
        {
            if (!versions.TryGetValue(binding.ItemVersionId, out var version)
                || !items.TryGetValue(binding.ItemId, out var item)
                || !sectionCodes.TryGetValue(binding.SectionId, out var sectionCode)
                || !topicCodes.TryGetValue(item.TopicId, out var topicCode))
            {
                continue;
            }

            var stimulus = item.StimulusId is int stimulusId && stimuli.TryGetValue(stimulusId, out var row)
                ? new PoolStimulusAnnotation(
                    row.CefrLevel.Code,
                    row.Passage,
                    row.AudioPath,
                    row.Transcript,
                    row.ImagePath,
                    row.ImageAlt,
                    row.DomainContext,
                    row.PromptCopy,
                    row.PrepSeconds,
                    row.ResponseSeconds)
                : null;

            annotations[(sectionCode, binding.DisplayOrder)] = new PoolQuestionAnnotation(
                topicCode,
                item.ItemType,
                item.CefrLevel.Code,
                item.PlacementEligible,
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
                version.ReferenceKind,
                stimulus,
                item.RenderFormat);
        }

        return annotations;
    }

    internal static string? Normalise(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Topic identity is (modality, code). Built once per import from the twenty rows.</summary>
    private sealed class ModalityCodeComparer : IEqualityComparer<(string Modality, string Code)>
    {
        public bool Equals((string Modality, string Code) x, (string Modality, string Code) y) =>
            string.Equals(x.Modality, y.Modality, StringComparison.Ordinal)
            && string.Equals(x.Code, y.Code, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Modality, string Code) obj) =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.Modality),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Code));
    }
}

/// <summary>#189: the twenty canonical topics, loaded once per import.</summary>
internal sealed class TopicCatalog
{
    private readonly List<(int TopicId, string Modality, string Code)> _topics;
    private readonly Dictionary<(string Modality, string Code), int> _byModalityAndCode;

    private TopicCatalog(List<(int TopicId, string Modality, string Code)> topics)
    {
        _topics = topics;
        _byModalityAndCode = topics.ToDictionary(
            t => (t.Modality, t.Code),
            t => t.TopicId,
            new KeyComparer());
    }

    public static async Task<TopicCatalog> LoadAsync(CompanionDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.Topics.AsNoTracking()
            .Select(t => new { t.TopicId, t.Modality, t.Code })
            .ToListAsync(cancellationToken);
        return new TopicCatalog(rows.Select(t => (t.TopicId, t.Modality, t.Code)).ToList());
    }

    public bool TryFind(string modality, string code, out int topicId) =>
        _byModalityAndCode.TryGetValue((modality, code), out topicId);

    /// <summary>Every canonical code of one modality, for a refusal that names the alternatives.</summary>
    public string Canonical(string modality) =>
        string.Join(", ", _topics
            .Where(t => string.Equals(t.Modality, modality, StringComparison.Ordinal))
            .Select(t => t.Code)
            .Order(StringComparer.Ordinal));

    private sealed class KeyComparer : IEqualityComparer<(string Modality, string Code)>
    {
        public bool Equals((string Modality, string Code) x, (string Modality, string Code) y) =>
            string.Equals(x.Modality, y.Modality, StringComparison.Ordinal)
            && string.Equals(x.Code, y.Code, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Modality, string Code) obj) =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.Modality),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Code));
    }
}

/// <summary>#189: the pool content one imported question becomes.</summary>
internal sealed record PoolContent(
    string Modality,
    int TopicId,
    string TopicCode,
    CefrLevel Level,
    string ItemType,
    bool PlacementEligible,
    /// <summary>
    /// #250: the widget the sitting draws. A second axis beside <see cref="ItemType"/>: the importer
    /// knows it from the file's own question type, and everything else derives it from the modality.
    /// </summary>
    string RenderFormat,
    string? OptionA,
    string? OptionB,
    string? OptionC,
    string? OptionD,
    string? KeyLabel,
    string? RationaleA,
    string? RationaleB,
    string? RationaleC,
    string? RationaleD,
    int? EvidenceStart,
    int? EvidenceEnd,
    string? EvidenceUnit,
    string? EvidenceQuote,
    // #204: a productive task's output demand and its two timed windows. All null for an objective
    // item, and all four travel into ItemVersions through BuildItem below.
    int? MinWords = null,
    int? MaxWords = null,
    int? PrepSeconds = null,
    int? ResponseSeconds = null,
    // #266: a hint is content the learner can reveal, and the other two are how a blank is judged.
    // They lived only on the legacy question row, so an imported pack lost them the moment the importer
    // stopped writing that row.
    string? Hint = null,
    string? AcceptedAnswersJson = null,
    string? RequiredKeywords = null,
    int? MaxSelections = null,
    bool AllowNumber = true)
{
    /// <summary>#204: the productive shape - no key, no options.</summary>
    public bool IsProductiveTask => KeyLabel is null && OptionA is null;
}

/// <summary>#189: what a bound paper position carries from the pool, for the exporter to write back.</summary>
internal sealed record PoolQuestionAnnotation(
    string TopicCode,
    string ItemType,
    string CefrLevel,
    bool PlacementEligible,
    string Stem,
    string? OptionA,
    string? OptionB,
    string? OptionC,
    string? OptionD,
    string? KeyLabel,
    string? Explanation,
    int? EvidenceStart,
    int? EvidenceEnd,
    string? EvidenceUnit,
    string? EvidenceQuote,
    string? ReferenceText,
    string? ReferenceKind,
    PoolStimulusAnnotation? Stimulus,
    /// <summary>
    /// #250: the seven-way render format the item was written as. The legacy <c>QuestionType</c> IS
    /// the render format, so exporting without it lost the distinction: a multi_select came back as
    /// an mcq, its two answers collapsed to a refusal, and the roundtrip test caught it as a failed
    /// batch. Defaulted so every existing construction stays valid.
    /// </summary>
    string RenderFormat = "mcq")
{
    /// <summary>CNT-06: no options means a productive task, which the legacy shape calls free_response.</summary>
    public bool IsProductive => OptionA is null;
}

/// <summary>#189: the shared passage, audio or prompt a bound version hangs off.</summary>
internal sealed record PoolStimulusAnnotation(
    string CefrLevel,
    string? Passage,
    string? AudioPath,
    string? Transcript,
    string? ImagePath,
    string? ImageAlt,
    string? DomainContext,
    string? PromptCopy,
    int? PrepSeconds,
    int? ResponseSeconds);
