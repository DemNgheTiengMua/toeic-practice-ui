using System.Text.Json;
using System.Text.Json.Serialization;

namespace Services;

public sealed class ImportManifestQuestion
{
    public string? Stem { get; set; }

    /// <summary>
    /// #229 / D1: dormant. Every import path reads <see cref="CefrLevel"/> instead, and a file that
    /// still carries this is accepted so a manifest written before #229 keeps importing - the number is
    /// read, not interpreted, because there is no difficulty-to-level mapping.
    /// </summary>
    public int? DifficultyLevel { get; set; }
    public string? QuestionType { get; set; }
    public string? CategoryCode { get; set; }
    public string? Explanation { get; set; }
    public string? Hint { get; set; }

    /// <summary>
    /// #189 / #229: the level the item is authored at. Required: an item with no level is the defect
    /// this ticket removes, and there is no default - D1 forbids mapping the five-point difficulty onto
    /// a CEFR level, and a silent A1 is how 212 items ended up at the wrong band.
    /// </summary>
    public string? CefrLevel { get; set; }

    /// <summary>
    /// #189: a canonical topic code (R-DET, L-GIST, …). When absent the legacy
    /// <see cref="CategoryCode"/> is mapped, and when that is absent either, the item type's own
    /// topic is used. An invented code is refused rather than clamped.
    /// </summary>
    public string? TopicCode { get; set; }

    /// <summary>
    /// #189: one of D0.4's fourteen types. Derived for a reading question (a passage means
    /// <c>passage_comprehension</c>, its absence <c>sentence_cloze</c>); required for the other three
    /// skills, whose types the text alone cannot determine.
    /// </summary>
    public string? ItemType { get; set; }

    /// <summary>#189: whether placement may draw this item. Absent means no.</summary>
    public bool? PlacementEligible { get; set; }

    /// <summary>#189: CNT-11's evidence span - seconds for audio, characters for text.</summary>
    public int? EvidenceStart { get; set; }

    public int? EvidenceEnd { get; set; }

    /// <summary><c>char</c> or <c>sec</c>.</summary>
    public string? EvidenceUnit { get; set; }

    public string? EvidenceQuote { get; set; }

    /// <summary>
    /// #152: the words a Read Aloud question asks the learner to say, or a description of the picture
    /// a Describe-a-Picture question shows. <see cref="ReferenceKind"/> says which.
    /// </summary>
    public string? SampleAnswerOrImageDesc { get; set; }

    /// <summary>"script" or "image_desc"; absent means the question is graded as free speech.</summary>
    public string? ReferenceKind { get; set; }

    public int? PrepSeconds { get; set; }
    public int? ResponseSeconds { get; set; }
    public int? MinWords { get; set; }
    public int? MaxWords { get; set; }
    public int? DisplayOrder { get; set; }
    public int? MaxSelections { get; set; }
    public bool? AllowNumber { get; set; }

    [JsonConverter(typeof(StringOrStringListConverter))]
    public List<string>? AcceptedAnswers { get; set; }

    public string? AcceptedAnswersJson { get; set; }

    [JsonConverter(typeof(StringOrStringListConverter))]
    public List<string>? RequiredKeywords { get; set; }

    public List<ImportManifestOption>? Options { get; set; }
    public List<ImportManifestCriterion>? Criteria { get; set; }
}
