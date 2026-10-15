using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>Moves values between the domain enums (BusinessObjects) and their wire mirrors
/// (<c>Enums.cs</c> here) by member name, and reads the wire code a member serialises as.</summary>
public static class EnumMapping
{
    private static readonly ConcurrentDictionary<Enum, string> WireCodes = new();

    /// <summary>Same-named member of another enum: <c>domainStatus.MapTo&lt;OrderStatusDto&gt;()</c>.
    /// A missing member means the two enums drifted apart, which the mirror test catches first.</summary>
    public static TTo MapTo<TTo>(this Enum value) where TTo : struct, Enum =>
        Enum.Parse<TTo>(value.ToString());

    /// <summary>The string the member is sent as on the wire ("in_progress").</summary>
    public static string ToWireCode(this Enum value) =>
        WireCodes.GetOrAdd(value, v =>
            v.GetType().GetField(v.ToString())?.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name
            ?? v.ToString());

    /// <summary>Parses a stored code straight into a wire enum (wire codes are the database codes).
    /// For Services that build DTOs from entity rows; an unknown code is drift, so it throws.</summary>
    public static TEnum ParseWireCode<TEnum>(string code) where TEnum : struct, Enum =>
        TryParseWireCode<TEnum>(code, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(code), code, $"Unknown {typeof(TEnum).Name} code.");

    /// <summary>Parses a wire code from untrusted input (form post, query string).</summary>
    public static bool TryParseWireCode<TEnum>(string? code, out TEnum value) where TEnum : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(candidate.ToWireCode(), code, StringComparison.OrdinalIgnoreCase))
            {
                value = candidate;
                return true;
            }
        }

        value = default;
        return false;
    }
}
