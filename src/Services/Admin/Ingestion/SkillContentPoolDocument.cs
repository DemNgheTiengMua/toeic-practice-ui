using System.Collections.Generic;
using System.Text.Json;

namespace Services;

/// <summary>
/// #198: the upload format, as <c>UnifiedSkillContentPool</c> 2.0.0 describes it (SRS CNT-03, D0.4).
/// <para>
/// Only the members this seam <em>reads</em> are declared, and the rest of the document is left alone
/// rather than refused: a real 2.0.0 file carries <c>metrics</c>, <c>time_limit_sec</c> and
/// <c>verification_audit.verifier_agreement</c>, and none of those may be believed. Gate two measures
/// the text itself and gate three runs its own verifiers, so a file's own claim that it passed is not
/// evidence - the seam recomputes, which is the whole point of running the gates at ingestion.
/// </para>
/// <para>
/// <b>Why the version is checked before anything else.</b> A half-applied unknown format is worse than
/// a refused one: the author would have to work out which of 300 records landed before deciding
/// whether to correct the file. See <see cref="PoolIngestionService"/>.
/// </para>
/// </summary>
public sealed class SkillContentPoolDocument
{
    /// <summary>"2.0.0". Any other value refuses the file.</summary>
    public string? SchemaVersion { get; set; }

    /// <summary>listening, reading, speaking or writing - and the file is one of them.</summary>
    public string? Skill { get; set; }

    /// <summary>The shared passages or audio scripts, each with its children.</summary>
    public List<SkillContentPoolGroup>? StimulusGroups { get; set; }

    /// <summary>
    /// Speaking and writing stock. Declared so a file that carries some is told so rather than having
    /// it silently dropped: this seam ingests objective items, and a productive task needs a rubric
    /// ladder the file does not carry (D17 gate four).
    /// </summary>
    public List<SkillContentPoolTask>? ProductiveTasks { get; set; }

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        // The format is snake_case (`stimulus_id`, `content_tier`, `correct_option`), and Web defaults
        // map PascalCase properties to camelCase - which silently reads every record as empty.
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}

/// <summary>One stimulus and the children that share it.</summary>
public sealed class SkillContentPoolGroup
{
    /// <summary><c>stim_demo_…</c> or <c>stim_prod_…</c>; the tier in the prefix is machine-checked.</summary>
    public string? StimulusId { get; set; }

    public string? ContentTier { get; set; }

    /// <summary>The stimulus's own level (D0.3's L_s).</summary>
    public string? StimulusLevel { get; set; }

    public string? DomainContext { get; set; }

    public string? BatchTag { get; set; }

    public SkillContentPoolContent? Content { get; set; }

    public List<SkillContentPoolItem>? Items { get; set; }
}

/// <summary>
/// A stimulus's text, its audio, or its image. <see cref="TextOrTranscript"/> is the passage for a
/// reading group and the transcript for a listening one - the same field, because D0.2 profiles
/// exactly one authored text either way.
/// </summary>
public sealed class SkillContentPoolContent
{
    public string? TextOrTranscript { get; set; }

    public SkillContentPoolAudio? AudioAsset { get; set; }

    public string? ImageUri { get; set; }

    public string? ImageAlt { get; set; }
}

public sealed class SkillContentPoolAudio
{
    public string? AudioUri { get; set; }
}

/// <summary>One objective item: four options, one key, an explanation and a rationale per option.</summary>
public sealed class SkillContentPoolItem
{
    /// <summary><c>item_demo_…</c> or <c>item_prod_…</c>.</summary>
    public string? ItemId { get; set; }

    public string? ContentTier { get; set; }

    /// <summary>One of the twenty canonical topic codes.</summary>
    public string? TopicId { get; set; }

    /// <summary>D0.3's L_i, which may differ from the stimulus's own level.</summary>
    public string? ItemLevel { get; set; }

    /// <summary>
    /// D0.4's type, either as its own code (<c>passage_comprehension</c>) or in the format's compound
    /// spelling (<c>reading_passage_comprehension_mcq</c>).
    /// </summary>
    public string? ItemType { get; set; }

    public string? QuestionType { get; set; }

    public string? Stem { get; set; }

    public List<SkillContentPoolOption>? Options { get; set; }

    public string? CorrectOption { get; set; }

    public string? Explanation { get; set; }

    public bool? PlacementEligible { get; set; }

    public SkillContentPoolSpan? EvidenceSpan { get; set; }

    /// <summary>
    /// The model family that wrote the item. Gate three's three-family rule needs it, and the file is
    /// the only place it can come from - a gate that named the author itself could not check it.
    /// </summary>
    public SkillContentPoolAudit? VerificationAudit { get; set; }
}

public sealed class SkillContentPoolOption
{
    public string? Key { get; set; }

    public string? Text { get; set; }

    /// <summary>The per-option rationale gate one refuses an item without (CNT-35).</summary>
    public string? DistractorRationale { get; set; }
}

public sealed class SkillContentPoolSpan
{
    public int? CharStart { get; set; }

    public int? CharEnd { get; set; }

    public int? StartSec { get; set; }

    public int? EndSec { get; set; }

    public string? VerbatimQuote { get; set; }
}

/// <summary>
/// The item's own account of who wrote it. Only <see cref="AuthorModel"/> is read: the family has to
/// be declared by someone, and <c>verifier_agreement</c> is precisely the claim the seam must not
/// trust - it runs the verifiers instead.
/// </summary>
public sealed class SkillContentPoolAudit
{
    public string? AuthorModel { get; set; }
}

/// <summary>A productive task, declared only so a file carrying one is refused by name.</summary>
public sealed class SkillContentPoolTask
{
    public string? TaskId { get; set; }
}
