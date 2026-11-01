using System;
using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>#153: how a goal's required hours compare with the hours available before the deadline.
/// <para>
/// There is no <c>Unknown</c> member: ADR-0006 requires the wire mirror to have exactly the domain
/// enum's members, and the domain enum deliberately has none either — an undecidable goal has no
/// verdict, so the field is null rather than a fourth code the CHECK constraint would reject.
/// </para></summary>
[JsonConverter(typeof(JsonStringEnumConverter<FeasibilityStatusDto>))]
public enum FeasibilityStatusDto
{
    [JsonStringEnumMemberName("feasible")] Feasible,

    [JsonStringEnumMemberName("stretch")] Stretch,

    [JsonStringEnumMemberName("unrealistic")] Unrealistic,
}
