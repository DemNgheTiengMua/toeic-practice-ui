using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Contracts;

/// <summary>
/// #220 / D19: the one seam a code crosses before it becomes text a human reads.
/// <para>
/// <b>What "rendered" means here.</b> The rendered set is <em>every enum declared in the
/// <c>Contracts</c> assembly</em>, and it is derived by reflection in <c>DisplayLabelTests</c> rather
/// than listed by hand. The boundary is the layer, not a judgement call: WebMVC cannot reference
/// <c>BusinessObjects</c>, so a domain enum can only reach a page after being mirrored here. Every
/// code a page can possibly print therefore has a wire mirror in this assembly, and registering the
/// whole assembly's enums is the only definition that cannot fall behind a new DTO.
/// </para>
/// <para>
/// <b>An unregistered code never reaches the page as itself.</b> It renders
/// <see cref="Unknown"/> and is logged at <see cref="LogLevel.Error"/>, because a code is a defect in
/// our own mapping - not something the reader can act on. This is deliberately stricter than
/// <see cref="CefrLevelLabels"/> and <see cref="ModalityLabels"/>, which log at warning: a level code
/// can arrive from an old placement row, while an enum value can only arrive from our own build.
/// </para>
/// <para>
/// <b>One writer per family.</b> Skill words stay in <see cref="ModalityLabels"/> and level words in
/// <see cref="CefrLevelLabels"/>; this type delegates rather than keeping a second copy, because a
/// fact with two writers eventually disagrees with itself (ADR-0012).
/// </para>
/// </summary>
public static class DisplayLabels
{
    /// <summary>What a code no family can place reads as. Never a code, never an underscore.</summary>
    public const string Unknown = "Not reported";

    private static readonly IReadOnlyDictionary<Type, LabelFamily> Families = BuildFamilies();

    /// <summary>The enum types this registry covers, for the exhaustiveness test.</summary>
    public static IReadOnlyCollection<Type> RegisteredTypes => [.. Families.Keys];

    /// <summary>Whether a type has a family. The exhaustiveness test needs this direction too.</summary>
    public static bool IsRegistered(Type enumType) => Families.ContainsKey(enumType);

    /// <summary>The labels a family holds, keyed by wire code, for the drift test.</summary>
    public static IReadOnlyDictionary<string, string> LabelsFor(Type enumType) =>
        Families.TryGetValue(enumType, out var family)
            ? family.ByCode
            : throw new ArgumentOutOfRangeException(nameof(enumType), enumType,
                "This enum has no label family. Add it to CatalogLabels, LearnerLabels or OperationsLabels.");

    /// <summary>What a family renders when it cannot place a code. Each names its own column.</summary>
    public static string FallbackFor(Type enumType) =>
        Families.TryGetValue(enumType, out var family) ? family.Unknown : Unknown;

    /// <summary>A typed value, for a DTO property: the key is the wire code the value serialises as.</summary>
    public static string LabelFor<TEnum>(TEnum value, ILogger logger) where TEnum : struct, Enum =>
        LabelForCode<TEnum>(value.ToWireCode(), logger);

    /// <summary>
    /// A code that arrived as a string - the audit, ingestion and mentor payloads carry their status
    /// as text. A null, blank or unknown code renders the family's fallback and is logged.
    /// </summary>
    public static string LabelForCode<TEnum>(string? code, ILogger logger) where TEnum : struct, Enum
    {
        if (!Families.TryGetValue(typeof(TEnum), out var family))
        {
            logger.LogError("DisplayLabels has no family for {EnumType}; rendered the fallback",
                typeof(TEnum).Name);
            return Unknown;
        }

        if (family.TryGet(code, out var label))
        {
            return label;
        }

        logger.LogError("DisplayLabels has no label for {EnumType} code {Code}; rendered the fallback",
            typeof(TEnum).Name, string.IsNullOrWhiteSpace(code) ? "<null>" : code);
        return family.Unknown;
    }

    private static IReadOnlyDictionary<Type, LabelFamily> BuildFamilies()
    {
        var families = new Dictionary<Type, LabelFamily>();
        CatalogLabels.AddTo(families);
        LearnerLabels.AddTo(families);
        OperationsLabels.AddTo(families);
        // The skill registry predates this one and keeps ownership of its four words.
        LabelFamily.Add<ModalityDto>(families, ModalityLabels.Unknown,
            [.. ModalityLabels.Mapping.Select(pair => (pair.Key, pair.Value))]);
        return families;
    }
}

/// <summary>One enum's words: what an unplaceable code reads as, and the code-to-label map.</summary>
internal sealed class LabelFamily
{
    public LabelFamily(string unknown, IReadOnlyList<(string Code, string Label)> entries)
    {
        Unknown = unknown;
        var byCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, label) in entries)
        {
            byCode[code] = label;
        }

        ByCode = byCode;
    }

    public string Unknown { get; }

    public IReadOnlyDictionary<string, string> ByCode { get; }

    public bool TryGet(string? code, out string label)
    {
        if (!string.IsNullOrWhiteSpace(code) && ByCode.TryGetValue(code, out var found))
        {
            label = found;
            return true;
        }

        label = Unknown;
        return false;
    }

    /// <summary>Registers one enum. A repeated code overwrites, so the test sees the final word.</summary>
    public static void Add<TEnum>(IDictionary<Type, LabelFamily> families, string unknown,
        params (string Code, string Label)[] entries) where TEnum : struct, Enum =>
        families[typeof(TEnum)] = new LabelFamily(unknown, entries);
}
