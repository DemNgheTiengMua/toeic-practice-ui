using System.Text.Json;
using System.Text.Json.Serialization;

namespace Services;

public sealed class ImportManifestQuestion
{
    public string? Stem { get; set; }
    public int? DifficultyLevel { get; set; }
    public string? QuestionType { get; set; }
    public string? CategoryCode { get; set; }
    public string? Explanation { get; set; }
    public string? Hint { get; set; }

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
