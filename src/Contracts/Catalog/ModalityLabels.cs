using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Contracts;

/// <summary>
/// D19 / #202: the only place a skill code becomes text a page reads. It sits beside
/// <see cref="CefrLevelLabels"/> and follows it exactly — an exhaustive mapping over the four
/// modalities, and a fallback that logs instead of putting <c>listening</c> in a heading.
/// <para>
/// The mapping is held against <see cref="ModalityDto"/> by a test, so a fifth modality added to the
/// type fails the suite rather than reaching a page as its own code.
/// </para>
/// </summary>
public static class ModalityLabels
{
    /// <summary>What a code the registry cannot place reads as. No raw code, never a lowercase one.</summary>
    public const string Unknown = "Skill not reported";

    private static readonly Dictionary<string, string> ByCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["listening"] = "Listening",
        ["reading"] = "Reading",
        ["speaking"] = "Speaking",
        ["writing"] = "Writing",
    };

    /// <summary>The codes this registry labels, for the drift test against the enum.</summary>
    public static IReadOnlyCollection<string> KnownCodes => ByCode.Keys;

    /// <summary>
    /// #220: the same words, for <see cref="DisplayLabels"/> to delegate to. Exposed rather than copied:
    /// two maps of skill names would eventually disagree, and this type stays the only writer of them.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Mapping => ByCode;

    public static string LabelForCode(string? code, ILogger logger)
    {
        if (code is not null && ByCode.TryGetValue(code, out var label))
        {
            return label;
        }

        logger.LogWarning("ModalityLabels has no label for skill code {Modality}; rendered the fallback",
            code is null ? "<null>" : code);
        return Unknown;
    }
}
