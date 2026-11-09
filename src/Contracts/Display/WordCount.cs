using System.Text.RegularExpressions;

namespace Contracts;

/// <summary>
/// #239: how many words a written answer holds — one rule, read by the service that stores the count
/// and by the composer that shows it as the learner types.
/// <para>
/// It lives in <c>Contracts</c> rather than in either consumer because WebMVC cannot reference
/// BusinessObjects (ADR-0002's layering), so a rule kept on the domain side would have to be retyped in
/// JavaScript — and two retypings of a counting rule is exactly how "the page says 142, the saved
/// answer says 141" happens. <see cref="Pattern"/> is the regex source the page compiles, so the
/// server and the browser run the same expression, not two expressions that agree today.
/// </para>
/// <para>
/// <b>Why these four separators and not <c>\s</c>.</b> <c>\s</c> also matches a no-break space, so a
/// learner pasting "one&#160;two" from a word processor would read "2 words" above a stored 1. A
/// no-break space holds one word together; that is what it is for. A hyphen likewise holds one word
/// together, and a newline separates two.
/// </para>
/// </summary>
public static class WordCount
{
    /// <summary>The four separators as a regex source, handed to the composer verbatim.</summary>
    public const string Pattern = @"[ \t\r\n]+";

    private static readonly Regex Separators = new(Pattern, RegexOptions.Compiled);

    public static int Count(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var words = 0;
        foreach (var token in Separators.Split(text))
        {
            if (token.Length > 0) words++;
        }

        return words;
    }
}
