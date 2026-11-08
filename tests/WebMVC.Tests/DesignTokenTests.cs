using System.Text.RegularExpressions;

namespace WebMVC.Tests;

/// <summary>
/// #220 / §5: the signing palette and type rules hold through the design tokens. The test asserts the
/// <em>declared value</em>, never the property's presence: <c>border-radius: 0</c> is the correct
/// answer and a grep for <c>border-radius</c> fails it.
/// <para>
/// <b>The two decisions this file records.</b>
/// </para>
/// <para>
/// <b>1. <c>box-shadow: inset 0 0 0 …</c> is a shadow.</b> It was one declaration, on
/// <c>.state-switcher--unknown</c> in <c>base.css</c>, and it re-drew the red border that was already
/// on the same rule. The argument for keeping it - "an inset ring is a re-rendered keyline, not depth"
/// - is true of the pixel and false of the rule: the system's own words are "no shadow", and a rule
/// with one named exception is a rule nobody can enforce. It is gone, replaced by the border alone,
/// and this file now holds every <c>box-shadow</c> in the stylesheet to <c>none</c>.
/// </para>
/// <para>
/// <b>2. A hard-stop <c>linear-gradient</c> is a ruled line, not a wash.</b> The answer sheet draws its
/// rule on the leading with two stops at the same position. Banning the function outright would delete
/// the rule; allowing it unconditionally would let a real gradient in under the same name. The test
/// allows a gradient only when its stops share a position, which is the difference between 1px of ink
/// and a fade.
/// </para>
/// </summary>
public sealed class DesignTokenTests
{
    private static readonly string CssRoot =
        Path.Combine(TestPathHelper.FindRepoRoot(), "src", "WebMVC", "wwwroot", "css");

    private static IEnumerable<string> Stylesheets() =>
        Directory.EnumerateFiles(CssRoot, "*.css", SearchOption.AllDirectories);

    /// <summary>
    /// The tokens compile to square corners. <b>The input that would pass a wrong guard:</b> a stylesheet
    /// that sets no radius at all, which the browser renders rounded on an input or a button. This
    /// asserts the token's declared value, so <c>0</c> is the only pass and <c>2px</c> is a failure.
    /// </summary>
    [Fact]
    public void The_radius_tokens_are_zero()
    {
        var tokens = File.ReadAllText(Path.Combine(CssRoot, "tokens.css"));

        var declared = Declarations(tokens, "border-radius")
            .Concat(Declarations(tokens, "--radius-sm"))
            .Concat(Declarations(tokens, "--radius-md"))
            .Concat(Declarations(tokens, "--radius-lg"))
            .ToList();

        Assert.NotEmpty(declared);
        Assert.All(declared, value => Assert.Equal("0", value));
    }

    /// <summary>
    /// No stylesheet rounds a corner. <b>The input that would pass a wrong guard:</b> a components sheet
    /// that inherits <c>0</c> from the token while setting <c>border-radius: 8px</c> on one modal - the
    /// token test still passes. This walks every declaration in every sheet and requires the value to be
    /// <c>0</c> (an explicit <c>0 !important</c> is the same claim, so it passes).
    /// </summary>
    [Fact]
    public void No_stylesheet_sets_a_non_zero_radius()
    {
        foreach (var path in Stylesheets())
        {
            foreach (var value in Declarations(File.ReadAllText(path), "border-radius"))
            {
                Assert.True(value is "0" or "0 !important",
                    $"{Path.GetFileName(path)} sets border-radius: {value}");
            }
        }
    }

    /// <summary>
    /// The shadow tokens are empty, and every <c>box-shadow</c> in the stylesheet is <c>none</c>.
    /// <b>The input that would pass a wrong guard:</b> reading only <c>tokens.css</c>, which would miss
    /// the inset ring this ticket removed from <c>base.css</c> - which is exactly how it survived. The
    /// comments are stripped first: this file's own explanation of that removal contains the property,
    /// and a scan that reads prose as code fails on its own documentation.
    /// </summary>
    [Fact]
    public void The_shadow_tokens_and_every_box_shadow_are_none()
    {
        var tokens = Uncommented(File.ReadAllText(Path.Combine(CssRoot, "tokens.css")));
        Assert.Equal(["none"], Declarations(tokens, "--shadow-sm").ToList());
        Assert.Equal(["none"], Declarations(tokens, "--shadow-md").ToList());

        var offenders = new List<string>();
        foreach (var path in Stylesheets())
        {
            foreach (var value in Declarations(Uncommented(File.ReadAllText(path)), "box-shadow"))
            {
                if (value is not ("none" or "none !important"))
                {
                    offenders.Add($"{Path.GetFileName(path)}: box-shadow: {value}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "box-shadow must be none (§5: a sign has no shadow):\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// No blur and no backdrop filter. <b>The input that would pass a wrong guard:</b> a rule that bans
    /// <c>backdrop-filter</c> but not <c>filter: blur()</c>, which frosts a panel just as well.
    /// </summary>
    [Fact]
    public void No_stylesheet_blurs_anything()
    {
        foreach (var path in Stylesheets())
        {
            var css = File.ReadAllText(path);
            Assert.DoesNotContain("backdrop-filter", css, StringComparison.Ordinal);
            Assert.DoesNotMatch(@"filter\s*:\s*[^;]*blur\(", css);
        }
    }

    /// <summary>
    /// The one gradient allowed is a hard stop. <b>The input that would pass a wrong guard:</b> banning
    /// <c>linear-gradient(</c> outright, which deletes the answer sheet's rule lines and leaves the
    /// sheet unruled - a visual defect introduced by a passing test. Every gradient must have its first
    /// two colours at the same position, and there must be exactly one.
    /// </summary>
    [Fact]
    public void The_only_gradient_is_a_hard_stopped_rule_line()
    {
        var gradients = new List<(string File, string Body)>();

        foreach (var path in Stylesheets())
        {
            var css = Uncommented(File.ReadAllText(path));
            foreach (Match match in Regex.Matches(css, @"(?<fn>\w+-gradient)\("))
            {
                var args = BalancedArguments(css, match.Index + match.Length - 1);
                gradients.Add((Path.GetFileName(path), args));
            }
        }

        Assert.Single(gradients);
        var (file, body) = gradients[0];   // body is the argument list, not the call
        Assert.Equal("components.css", file);

        // Both stops at the same position: `linear-gradient(to bottom, transparent calc(1lh - 1px),
        // var(--line-soft) calc(1lh - 1px))`. Two different positions is a fade.
        var stops = StopPositions(body);
        Assert.True(stops.Count >= 2, $"the gradient in {file} has no comparable stops: {body}");
        Assert.All(stops, stop => Assert.Equal(stops[0], stop));
    }

    /// <summary>
    /// The position of each colour stop in a gradient, read with balanced parentheses so
    /// <c>calc(1lh - 1px)</c> is one stop and not three.
    /// </summary>
    private static IReadOnlyList<string> StopPositions(string body)
    {
        // Drop the direction argument ("to bottom"), then split on top-level commas.
        var comma = body.IndexOf(',');
        var remainder = comma >= 0 ? body[(comma + 1)..] : body;

        var stops = new List<string>();
        var depth = 0;
        var current = new System.Text.StringBuilder();

        foreach (var character in remainder)
        {
            if (character == '(') depth++;
            if (character == ')') depth--;

            if (character == ',' && depth == 0)
            {
                stops.Add(Position(current.ToString()));
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        stops.Add(Position(current.ToString()));
        return stops;
    }

    /// <summary>The argument list of the call whose <c>(</c> sits at <paramref name="open"/>, parens balanced.</summary>
    private static string BalancedArguments(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')' && --depth == 0) return text[(open + 1)..i];
        }

        return text[(open + 1)..];
    }

    private static string Position(string stop)
    {
        // `<colour> <position>`; the position is everything after the colour, whitespace-delimited
        // but with its own parentheses balanced: `calc(1lh - 1px)` is one token.
        var text = stop.Trim();
        var depth = 0;
        var boundary = -1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')') depth--;
            else if (text[i] == ' ' && depth == 0) boundary = i;
        }

        return boundary >= 0 ? text[(boundary + 1)..].Trim() : "";
    }

    /// <summary>The value of every declaration of <paramref name="property"/>, in file order.</summary>
    private static IEnumerable<string> Declarations(string css, string property) =>
        Regex.Matches(css, @"(?<![\w-])" + Regex.Escape(property) + @"\s*:\s*(?<value>[^;]+);")
            .Select(match => match.Groups["value"].Value.Trim());

    /// <summary>Removes <c>/* … */</c> comments: prose that mentions a property is not a declaration.</summary>
    private static string Uncommented(string css) =>
        Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);

    /// <summary>
    /// Every figure is tabular monospace. <b>The input that would pass a wrong guard:</b> naming the
    /// <c>--font-mono</c> token, which proves a variable exists and nothing about whether a number uses
    /// it. This asserts the monospace is tabular by construction and that the utility class applies it.
    /// </summary>
    [Fact]
    public void The_data_face_is_a_tabular_monospace()
    {
        var tokens = File.ReadAllText(Path.Combine(CssRoot, "tokens.css"));
        var mono = Declarations(tokens, "--font-mono").Single();

        Assert.Contains("IBM Plex Mono", mono, StringComparison.Ordinal);
        Assert.Contains("monospace", mono, StringComparison.Ordinal);

        var baseSheet = File.ReadAllText(Path.Combine(CssRoot, "base.css"));
        var monoRule = Regex.Match(baseSheet, @"\.font-mono\s*\{(?<body>[^}]*)\}");
        Assert.True(monoRule.Success, "base.css has no .font-mono rule");
        Assert.Contains("var(--font-mono)", monoRule.Groups["body"].Value, StringComparison.Ordinal);
        Assert.Contains("tabular-nums", monoRule.Groups["body"].Value, StringComparison.Ordinal);
    }

    /// <summary>
    /// The four sign fields are the palette, and each is one flat colour. <b>The input that would pass a
    /// wrong guard:</b> a green that looks right - the system fixes the hex, so a near-enough green is a
    /// different colour on a different screen.
    /// </summary>
    [Fact]
    public void The_sign_roles_are_the_four_painted_fields()
    {
        var tokens = File.ReadAllText(Path.Combine(CssRoot, "tokens.css"));

        Assert.Equal(["#b31217"], Declarations(tokens, "--sign-red").ToList());
        Assert.Equal(["#0b4ea2"], Declarations(tokens, "--sign-blue").ToList());
        Assert.Equal(["#f2b705"], Declarations(tokens, "--sign-gold").ToList());
        Assert.Equal(["#0a6b3c"], Declarations(tokens, "--sign-green").ToList());
        Assert.Equal(["#14181d"], Declarations(tokens, "--ink").ToList());
    }
}
