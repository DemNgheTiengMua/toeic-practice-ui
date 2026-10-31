using System;
using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>#153: how a goal's required hours compare with the hours available before the deadline.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FeasibilityStatusDto>))]
public enum FeasibilityStatusDto
{
    /// <summary>No answer to give: no deadline, or bands that carry no study hours. Not a refusal.</summary>
    [JsonStringEnumMemberName("unknown")] Unknown,

    [JsonStringEnumMemberName("feasible")] Feasible,

    [JsonStringEnumMemberName("stretch")] Stretch,

    [JsonStringEnumMemberName("unrealistic")] Unrealistic,
}
