using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Contracts;

/// <summary>
/// #220 / D19: the vocabulary an AI grader uses to name a located writing error. It is <em>not</em> an
/// enum: the words are the prompt's own list (<c>ClaudeAiGrader</c> asks for
/// <c>grammar|vocabulary|spelling|punctuation|cohesion|task</c>) plus the grader's <c>other</c>
/// fallback, so the set is authored in the prompt rather than in a type. That is exactly why it needs a
/// registry: nothing in the compiler can notice a new category, and the page used to print it raw.
/// <para>
/// It follows <see cref="ModalityLabels"/> exactly - exhaustive over the prompt's vocabulary, and a
/// fallback that logs rather than putting <c>cohesion</c> in a table cell.
/// </para>
/// </summary>
public static class WritingErrorCategoryLabels
{
    /// <summary>What a category the registry cannot place reads as. Never the code, never lowercase.</summary>
    public const string Unknown = "Error not classified";

    private static readonly Dictionary<string, string> ByCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["grammar"] = "Grammar",
        ["vocabulary"] = "Vocabulary",
        ["spelling"] = "Spelling",
        ["punctuation"] = "Punctuation",
        ["cohesion"] = "Cohesion",
        ["task"] = "Task response",
        ["other"] = "Other",
    };

    /// <summary>The codes this registry labels, for the drift test against the grader's prompt.</summary>
    public static IReadOnlyCollection<string> KnownCodes => ByCode.Keys;

    public static string LabelForCode(string? code, ILogger logger)
    {
        if (code is not null && ByCode.TryGetValue(code, out var label))
        {
            return label;
        }

        logger.LogError("WritingErrorCategoryLabels has no label for error category {Category}; rendered the fallback",
            code is null ? "<null>" : code);
        return Unknown;
    }
}
