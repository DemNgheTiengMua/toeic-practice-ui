using System;
using System.Collections.Generic;
using System.Linq;

namespace Services;

/// <summary>
/// Normalizes <c>TargetBands</c> for Items and Lessons to either <c>"ALL"</c>
/// or a deduplicated, uppercase, comma-separated list of band codes (e.g. <c>"A2,B1,B2"</c>).
/// </summary>
internal static class TargetBandsNormalizer
{
    internal static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "ALL";
        }

        var parts = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.ToUpperInvariant())
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (parts.Count == 0 || parts.Contains("ALL", StringComparer.Ordinal))
        {
            return "ALL";
        }

        var joined = string.Join(",", parts);
        return joined.Length > 100 ? joined[..100] : joined;
    }
}
