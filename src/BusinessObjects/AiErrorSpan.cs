using System.Collections.Generic;
using System.Text.Json;

namespace BusinessObjects;

/// <summary>
/// E2.1 (#151): one located error, as the grader reported it. The snippet is quoted from the
/// candidate's own text so the page can show original → corrected; a grader that cannot quote it is
/// talking about a sentence that is not there.
/// </summary>
public sealed record AiErrorSpan(
    string OriginalSnippet,
    string CorrectedSnippet,
    string Category,
    string ExplanationVi)
{
    /// <summary>The column shape: camelCase, one list, so a later consumer reads the same names the prompt asked for.</summary>
    private static readonly JsonSerializerOptions Storage = new(JsonSerializerDefaults.Web);

    /// <summary>Empty feedback is stored as NULL rather than an empty array: the column means "nothing to fix".</summary>
    public static string? Serialize(IReadOnlyList<AiErrorSpan> spans) =>
        spans.Count == 0 ? null : JsonSerializer.Serialize(spans, Storage);

    public static IReadOnlyList<AiErrorSpan> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<AiErrorSpan>>(json, Storage) ?? [];
        }
        catch (JsonException)
        {
            // A hand-edited or truncated row shows no spans instead of taking the page down.
            return [];
        }
    }
}
