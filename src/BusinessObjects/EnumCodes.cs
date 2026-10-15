using System.Collections.Concurrent;
using System.Text;

namespace BusinessObjects;

/// <summary>Converts the domain enums in <c>Enums.cs</c> to and from the snake_case codes the
/// schema stores ("in_progress" is <see cref="AttemptStatus.InProgress"/>). Safe inside EF
/// queries: <c>AttemptStatus.Graded.ToCode()</c> has no row dependency, so EF evaluates it once
/// and sends the code as a parameter.</summary>
public static class EnumCodes
{
    private static readonly ConcurrentDictionary<Enum, string> Codes = new();

    public static string ToCode<TEnum>(this TEnum value) where TEnum : struct, Enum =>
        Codes.GetOrAdd(value, v => ToSnakeCase(v.ToString()));

    /// <summary>Parses a stored code. An unknown code is a schema/enum drift bug, so it throws.</summary>
    public static TEnum Parse<TEnum>(string code) where TEnum : struct, Enum =>
        TryParse<TEnum>(code, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(code), code, $"Unknown {typeof(TEnum).Name} code.");

    /// <summary>For untrusted input (query strings, form posts, gateway payloads).</summary>
    public static bool TryParse<TEnum>(string? code, out TEnum value) where TEnum : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(candidate.ToCode(), code, StringComparison.OrdinalIgnoreCase))
            {
                value = candidate;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0)
            {
                sb.Append('_');
            }
            sb.Append(char.ToLowerInvariant(name[i]));
        }
        return sb.ToString();
    }
}
