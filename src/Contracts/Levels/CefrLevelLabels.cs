using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Contracts;

/// <summary>
/// D19 / #182: the only place a level code becomes text a learner reads. It sits beside
/// <see cref="ScoreDisplay"/> because both are the one formatting seam for their quantity, and it sits
/// in Contracts because that is the layer WebMVC can see.
/// <para>
/// The mapping is exhaustive over the level ladder; <c>CefrLevelTests</c> holds it against
/// <c>BusinessObjects.CefrLevel</c>, so a level added to the type without a label fails the suite
/// instead of reaching a page as its own code. An unmapped code is not the learner's error: it renders
/// as <see cref="Unknown"/> and is logged, because "below_A1" in a badge is the defect this replaces.
/// </para>
/// </summary>
public static class CefrLevelLabels
{
    /// <summary>What a code the registry cannot place reads as. No raw code, never an underscore.</summary>
    public const string Unknown = "Level not reported";

    private static readonly Dictionary<string, string> ByCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pre_a1"] = "Below A1",
        ["A1"] = "A1",
        ["A2"] = "A2",
        ["B1"] = "B1",
        ["B2"] = "B2",
        ["C1"] = "C1",
        ["C2"] = "C2",
    };

    /// <summary>The codes this registry labels, in ladder order, for the drift test against the type.</summary>
    public static IReadOnlyCollection<string> KnownCodes => ByCode.Keys;

    public static string LabelForCode(string? code, ILogger logger)
    {
        if (code is not null && ByCode.TryGetValue(code, out var label))
        {
            return label;
        }

        logger.LogWarning("CefrLevelLabels has no label for level code {CefrLevelCode}; rendered the fallback",
            code is null ? "<null>" : code);
        return Unknown;
    }
}
