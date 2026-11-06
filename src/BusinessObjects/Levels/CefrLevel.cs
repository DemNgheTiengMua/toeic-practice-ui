using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BusinessObjects;

/// <summary>
/// #182: the one level scale, and the only one. A level is A1 to C2, plus a floor that sorts below A1
/// and exists so a result can say "below A1" honestly instead of inventing A1 or reporting nothing.
/// <para>
/// The floor is reportable, never authored: <see cref="AuthoredLevels"/> is what an item, a lesson or
/// a path step may carry, and <see cref="TryParseAuthored"/> refuses the floor the way the type itself
/// refuses everything else. That distinction lives here, in the type, because a comment cannot fail.
/// </para>
/// <para>
/// A certificate band is a scored range the certificate defines (TOEIC 785-940, VSTEP 6.0-6.5). It is
/// a different quantity from an ability level; there is no conversion, and none may be added.
/// </para>
/// </summary>
[JsonConverter(typeof(CefrLevelJsonConverter))]
public readonly record struct CefrLevel : IComparable<CefrLevel>
{
    // Stored codes, by rank. The floor is rank 0 so the struct's default value IS the floor: an
    // unset level reports "below A1" rather than claiming A1 or rendering nothing.
    private static readonly string[] CodesByRank = ["pre_a1", "A1", "A2", "B1", "B2", "C1", "C2"];

    private CefrLevel(int rank) => Rank = rank;

    public static CefrLevel PreA1 { get; } = new(0);

    public static CefrLevel A1 { get; } = new(1);
    public static CefrLevel A2 { get; } = new(2);
    public static CefrLevel B1 { get; } = new(3);
    public static CefrLevel B2 { get; } = new(4);
    public static CefrLevel C1 { get; } = new(5);
    public static CefrLevel C2 { get; } = new(6);

    /// <summary>The six levels content and goals may be authored at, ascending.</summary>
    public static IReadOnlyList<CefrLevel> AuthoredLevels { get; } = [A1, A2, B1, B2, C1, C2];

    /// <summary>Every level a result may report: the floor, then the authored ladder.</summary>
    public static IReadOnlyList<CefrLevel> Reportable { get; } = [PreA1, .. AuthoredLevels];

    public int Rank { get; }

    /// <summary>The stored code. The floor's is <c>pre_a1</c>; <c>below_A1</c> is a certificate band
    /// code and is deliberately not a level.</summary>
    public string Code => CodesByRank[Rank];

    public bool IsReportableFloor => Rank == 0;

    /// <summary>Accepts anything a result may report, the floor included.</summary>
    public static bool TryParse(string? code, out CefrLevel level) => TryFind(code, Reportable, out level);

    /// <summary>Accepts only a level content may be authored at. A row that arrives with the floor is
    /// authored wrong, not reported low.</summary>
    public static bool TryParseAuthored(string? code, out CefrLevel level) => TryFind(code, AuthoredLevels, out level);

    public int CompareTo(CefrLevel other) => Rank.CompareTo(other.Rank);

    public static bool operator <(CefrLevel left, CefrLevel right) => left.Rank < right.Rank;
    public static bool operator >(CefrLevel left, CefrLevel right) => left.Rank > right.Rank;
    public static bool operator <=(CefrLevel left, CefrLevel right) => left.Rank <= right.Rank;
    public static bool operator >=(CefrLevel left, CefrLevel right) => left.Rank >= right.Rank;

    public override string ToString() => Code;

    private static bool TryFind(string? code, IReadOnlyList<CefrLevel> within, out CefrLevel level)
    {
        foreach (var candidate in within)
        {
            if (string.Equals(candidate.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                level = candidate;
                return true;
            }
        }

        level = default;
        return false;
    }
}
