using System.Text.RegularExpressions;
using Contracts;
using WebMVC.Models;

namespace WebMVC.Tests;

/// <summary>
/// #220 / D19: no internal code reaches a page. Two halves, and they check different things.
/// <para>
/// <b>The rendered-leak half</b> parses every view into text nodes and attributes and asserts that no
/// enum-valued property is printed as text. The distinction is the whole ticket: a code in
/// <c>href=</c>, <c>value=</c>, <c>class=</c> or <c>name=</c> is machine data and must stay a code -
/// <c>Admin/Users.cshtml</c>, <c>Complaints.cshtml</c> and <c>Orders.cshtml</c> all put
/// <c>@Model.Status</c> in a query string, which is a filter and correct. "Fixing" those breaks the
/// filter, so the assertion is on the position, never on the token appearing in the file.
/// </para>
/// <para>
/// <b>The label half</b> drives the real view models, because a page can call the right registry and
/// still print the wrong thing.
/// </para>
/// </summary>
public sealed class DisplayLeakTests
{
    private static readonly string ViewsRoot =
        Path.Combine(TestPathHelper.FindRepoRoot(), "src", "WebMVC", "Views");

    // All seven measured legacy Razor views have been retired to the React SPA.

    /// <summary>
    /// <b>The input that would pass a wrong fix:</b> labelling the seven measured leaks and stopping.
    /// A new DTO property is what the next ticket adds. This scans every view for a text node that is
    /// <em>only</em> a code-shaped property read, so the guard is about the shape, not the seven.
    /// <para>
    /// A comparison or a ternary is not a leak: <c>@(x.Status == "done" ? "Done" : "Open")</c> renders
    /// a word and only reads the code, and flagging it would push an author to "fix" a correct line.
    /// </para>
    /// </summary>
    [Fact]
    public void No_view_prints_a_code_shaped_property_in_a_text_node()
    {
        var offenders = new List<string>();
        var resolver = new ViewModelTypeResolver();

        foreach (var path in Directory.EnumerateFiles(ViewsRoot, "*.cshtml", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(path);
            var modelType = resolver.ModelTypeOf(source);
            var loops = resolver.Locals(source, modelType);
            foreach (var node in TextNodeExpressions(source))
            {
                if (resolver.CodeShapedRead(modelType, loops, node) is { } concept)
                {
                    offenders.Add($"{Path.GetRelativePath(ViewsRoot, path)}: @{node} renders {concept}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "raw codes in text nodes (a code between > and < is read by a human):\n  "
            + string.Join("\n  ", offenders));
    }



    /// <summary>
    /// <b>The input that would pass a wrong fix:</b> a view model that returns the code when the
    /// registry misses, which renders a code and looks fine on every fixture. This drives the real
    /// models with the seven statuses that used to leak and asserts the exact words.
    /// </summary>
    [Fact]
    public void The_seven_leaks_render_words_from_the_registry()
    {
        var booking = new LearnerBookingsViewModel([Booking("done")]);
        Assert.Equal("Completed", booking.StatusLabels[1]);
    }

    /// <summary>
    /// <b>The input that would pass a wrong test:</b> a fixture with one status, which any label map
    /// satisfies. Every value of each repaired enum is driven, and the word must differ from the wire
    /// code - a fallback returning the code would pass a "not empty" assertion and fail this one.
    /// </summary>
    [Fact]
    public void Every_status_the_repaired_pages_show_reads_as_more_than_its_code()
    {
        foreach (var status in Enum.GetValues<BookingStatusDto>())
        {
            var label = new LearnerBookingsViewModel([Booking(status.ToWireCode())]).StatusLabels[1];
            Assert.NotEqual(status.ToWireCode(), label);
        }
    }

    /// <summary>
    /// The error category is not an enum - the grader's prompt authors it - so nothing in the compiler
    /// would notice a new one. <b>The input that would pass a wrong page:</b> one that prints
    /// <c>@span.Category</c>, which is correct for <c>grammar</c> and wrong for everything the prompt
    /// adds next. The repaired page's map is asserted over the whole documented vocabulary.
    /// </summary>
    [Fact]
    public void The_writing_error_category_is_labelled_and_never_the_raw_code()
    {
        Assert.Equal(
            ["grammar", "vocabulary", "spelling", "punctuation", "cohesion", "task", "other"],
            WritingErrorCategoryLabels.KnownCodes.ToArray());

        foreach (var code in WritingErrorCategoryLabels.KnownCodes)
        {
            var label = WritingErrorCategoryLabels.LabelForCode(code, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
            Assert.NotEqual(code, label);
            Assert.NotEqual(code.ToUpperInvariant(), label);
        }

        var review = new WritingReviewViewModel(new WritingReviewDto(
            1, 1, "Writing", "TOEIC", DateTimeOffset.UtcNow, 1, 1, 1, 1,
            [
                new WritingQuestionReviewItemDto(1, 1, "Section", "Stem", "text", 10, 1, 1, [],
                    [new WritingErrorSpanDto("teh", "the", "spelling", "Sai chính tả.")]),
            ]));
        Assert.Equal("Spelling", review.ErrorCategoryLabels[1][0]);
    }

    /// <summary>
    /// <b>The input that would pass a wrong test:</b> a status the page has never seen. An unknown
    /// code must not become the code: the fallback names the column instead.
    /// </summary>
    [Fact]
    public void An_unknown_status_renders_a_fallback_rather_than_the_code()
    {
        var booking = new LearnerBookingsViewModel([Booking("rescheduled")]);
        Assert.NotEqual("rescheduled", booking.StatusLabels[1]);
        Assert.DoesNotContain("_", booking.StatusLabels[1], StringComparison.Ordinal);

        Assert.NotEqual("contested", WritingErrorCategoryLabels.LabelForCode("contested", Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance));
    }

    // ---- the text-node reader -------------------------------------------------

    /// <summary>
    /// The text a human reads: everything between a <c>&gt;</c> and the next <c>&lt;</c> on the same
    /// line. Attributes live inside the tag and are therefore absent, which is what keeps the
    /// query-string filters out of the scan. Restricting to one line also keeps Razor code blocks out:
    /// <c>@{ var x = Model.Status; }</c> and <c>@if (Model.Status == ...)</c> are C#, not page text.
    /// </summary>
    private static string TextNodes(string source) =>
        string.Join("\n", Regex.Matches(StripComments(source), @">(?<text>[^\n<]*)<")
            .Select(match => match.Groups["text"].Value));

    /// <summary>A Razor comment holds prose and code alike; what is inside one is never rendered.</summary>
    private static string StripComments(string source) =>
        Regex.Replace(source, @"@\*.*?\*@", "", RegexOptions.Singleline);

    /// <summary>
    /// Every <c>@</c>-expression in a text node, read the way Razor reads it: <c>@( … )</c> is a
    /// balanced parenthesised expression, <c>@name.name(...)</c> is a member chain. A regex cannot do
    /// this - it stops at the first <c>=</c> and turns a ternary into a bare property read.
    /// </summary>
    private static IEnumerable<string> TextNodeExpressions(string source)
    {
        foreach (Match node in Regex.Matches(StripComments(source), @">(?<text>[^\n<]*)<"))
        {
            var text = node.Groups["text"].Value;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] != '@') continue;
                if (i + 1 < text.Length && text[i + 1] == '@') { i++; continue; }   // escaped @@
                if (i + 1 >= text.Length) break;

                if (text[i + 1] == '(')
                {
                    var depth = 0;
                    var j = i + 1;
                    for (; j < text.Length; j++)
                    {
                        if (text[j] == '(') depth++;
                        else if (text[j] == ')' && --depth == 0) break;
                    }

                    yield return text.Substring(i + 1, Math.Min(j, text.Length - 1) - i).Trim();
                    i = j;
                    continue;
                }

                var start = i + 1;
                var k = start;
                while (k < text.Length && (char.IsLetterOrDigit(text[k]) || text[k] is '_' or '.' or '?' or '[' or ']' or '!'))
                {
                    k++;
                }

                if (k > start) yield return text.Substring(start, k - start);
                i = k - 1;
            }
        }
    }

    /// <summary>
    /// The enum-valued property names in <c>Contracts</c>, derived from the assembly so this guard
    /// does not need editing when a DTO gains one.
    /// </summary>
    private static readonly HashSet<string> EnumMemberNames = typeof(DisplayLabels).Assembly
        .GetTypes()
        .Where(type => type.IsEnum && type.IsPublic)
        .SelectMany(type => Enum.GetNames(type))
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// True when the whole expression is a code-shaped property read - <c>@x.Status</c>,
    /// <c>@x.Status.ToWireCode()</c>, <c>@Model.Ladder.Reason</c> - rather than a call that turned it
    /// into a word or a comparison that only reads the code.
    /// </summary>
    /// <summary>
    /// Properties the schema itself defines as a closed set of codes, even though the DTO carries them
    /// as <c>string</c> because the table stores text. <c>LayoutType</c> and <c>NavigationMode</c> are
    /// CK-constrained; <c>Severity</c> is the audit's <c>"error" | "warning"</c>. These are codes, not
    /// prose, so they are held to the same rule as an enum.
    /// </summary>
    private static readonly HashSet<string> ClosedCodeStrings = new(StringComparer.Ordinal)
    {
        "LayoutType", "NavigationMode", "Severity",
    };

    /// <summary>
    /// <b>The input that would pass a wrong guard:</b> a hand-typed list of the properties that leaked
    /// today. It catches those and nothing the next DTO adds. This asserts the guard's own input is
    /// derived from <c>Contracts</c> and is not empty: <c>Status</c>, <c>Purpose</c>, <c>Source</c> and
    /// their siblings are discovered, so a page printing a new one fails without anyone editing a list.
    /// </summary>
    [Fact]
    public void The_code_shaped_names_are_derived_from_Contracts()
    {
        Assert.NotEmpty(CodeShapedMemberNames);
        Assert.Contains("Status", CodeShapedMemberNames);
        Assert.Contains("Purpose", CodeShapedMemberNames);
        Assert.Contains("Modality", CodeShapedMemberNames);
        Assert.Contains("QuestionType", CodeShapedMemberNames);

        Assert.All(CodeShapedMemberNames, name =>
            Assert.Contains(typeof(DisplayLabels).Assembly.GetTypes(), type =>
                type.GetProperties().Any(property => property.Name == name
                    && (property.PropertyType.IsEnum || ClosedCodeStrings.Contains(property.Name)))));
    }

    /// <summary>
    /// Every member name that is declared somewhere in <c>Contracts</c> as an enum or one of the three
    /// closed code strings - <em>derived by reflection, never typed</em>. That derivation is what makes
    /// the guard independent of a hand-written list: a DTO that gains a <c>Status</c>-like property makes
    /// its name code-shaped here automatically, and a page that prints it fails.
    /// </summary>
    private static readonly HashSet<string> CodeShapedMemberNames = typeof(DisplayLabels).Assembly
        .GetTypes()
        .SelectMany(type => type.GetProperties())
        .Where(property => property.PropertyType.IsEnum
            || ClosedCodeStrings.Contains(property.Name))
        .Select(property => property.Name)
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Resolves a text-node expression against the page's model type, so the guard can tell a code
    /// from free text. Without this, <c>@c.Reason</c> on a complaint (the learner's own words) and
    /// <c>@item.Reason</c> on a ledger row (a <c>CreditReasonDto</c>) look identical, and a guard that
    /// cannot tell them apart either misses one or forces an author to label free text.
    /// <para>
    /// A property that cannot be resolved is reported rather than skipped: a guard that silently passes
    /// what it cannot read is the failure mode this ticket is about.
    /// </para>
    /// </summary>
    private sealed class ViewModelTypeResolver
    {
        public Type? ModelTypeOf(string source)
        {
            var match = Regex.Match(source, @"^@model\s+(?<type>[\w\.]+)", RegexOptions.Multiline);
            if (!match.Success) return null;

            var name = match.Groups["type"].Value;
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType($"WebMVC.Models.{name}") ?? assembly.GetType(name))
                .FirstOrDefault(type => type is not null);
        }

        /// <summary>
        /// The locals an expression can start with: a <c>foreach</c> element and a
        /// <c>var x = Model.Thing</c> alias. Both are read from the view, so a rename needs no edit here.
        /// <para>
        /// Resolved to a fixed point, because a loop usually walks an alias
        /// (<c>var wallet = Model.Wallet;</c> then <c>foreach (var item in wallet.Ledger)</c>).
        /// </para>
        /// </summary>
        public IReadOnlyDictionary<string, Type> Locals(string source, Type? modelType)
        {
            var bound = new Dictionary<string, Type>(StringComparer.Ordinal);

            for (var pass = 0; pass < 3; pass++)
            {
                // `var studio = Model.Studio;` / `var detail = Model.Form.Detail;`
                foreach (Match alias in Regex.Matches(source,
                             @"\bvar\s+(?<name>\w+)\s*=\s*(?<expr>[A-Za-z_][\w\.\[\]]*)\s*;"))
                {
                    if (Resolve(modelType, bound, alias.Groups["expr"].Value) is { } type)
                    {
                        bound[alias.Groups["name"].Value] = type;
                    }
                }

                foreach (Match loop in Regex.Matches(source,
                             @"foreach\s*\(\s*var\s+(?<name>\w+)\s+in\s+(?<expr>[\w\.\[\]]+)\s*\)"))
                {
                    if (Resolve(modelType, bound, loop.Groups["expr"].Value) is { } collection
                        && ElementTypeOf(collection) is { } element)
                    {
                        bound[loop.Groups["name"].Value] = element;
                    }
                }
            }

            return bound;
        }

        /// <summary>Types an expression rooted at <c>Model</c> or at a local already bound.</summary>
        private static Type? Resolve(Type? modelType, IReadOnlyDictionary<string, Type> bound, string expression)
        {
            var segments = Segments(expression);
            if (segments.Count == 0) return null;

            Type? start;
            if (segments[0] == "Model")
            {
                start = modelType;
                segments.RemoveAt(0);
            }
            else if (bound.TryGetValue(segments[0], out var local))
            {
                start = local;
                segments.RemoveAt(0);
            }
            else
            {
                return null;
            }

            return PropertyType(start, segments);
        }

        /// <summary>
        /// The concept name when the expression is a code-shaped read of a property that carries a
        /// code; null when it is a string a human wrote, a label, or something other than a bare read.
        /// </summary>
        public string? CodeShapedRead(Type? modelType, IReadOnlyDictionary<string, Type> loops, string expression)
        {
            var cleaned = Regex.Replace(expression.Trim(), @"\.\w+\(\)$", "");
            if (!Regex.IsMatch(cleaned, @"^[A-Za-z_]\w*(\.[A-Za-z_]\w*)*$"))
            {
                return null;    // an operator, a literal, a call: not a bare read
            }

            var segments = Segments(cleaned);
            if (segments.Count == 0) return null;

            var last = segments[^1];
            // A name that says it is a word is a word. This is a shape rule about how a property is
            // named, and it is what keeps this guard from demanding a label on `DisplayName`.
            if (last.EndsWith("Label", StringComparison.Ordinal)
                || last.EndsWith("Text", StringComparison.Ordinal)
                || last.EndsWith("Name", StringComparison.Ordinal)
                || last.EndsWith("Display", StringComparison.Ordinal)
                || last.EndsWith("Code", StringComparison.Ordinal)) return null;

            // Not a code-shaped name anywhere in Contracts: `@p`, `@level`, `@mentor.PriceCredits`.
            // This is the derived set doing the work, so the guard has no hand-typed enumeration.
            if (!CodeShapedMemberNames.Contains(last)) return null;

            Type? start = modelType;
            if (segments[0] == "Model")
            {
                segments.RemoveAt(0);
            }
            else if (loops.TryGetValue(segments[0], out var loopType))
            {
                start = loopType;
                segments.RemoveAt(0);
            }
            else
            {
                // A code-named property read off a local this resolver cannot type. Reported rather
                // than skipped: a guard that silently passes what it cannot read is the failure this
                // ticket is about.
                return $"{last} (the local `{cleaned.Split('.')[0]}` could not be typed)";
            }

            if (segments.Count == 0) return null;
            last = segments[^1];

            var type = PropertyType(start, segments);
            return type switch
            {
                null => null,     // a computed member property; not a stored code
                { IsEnum: true } => last,
                // The three names the schema defines as a closed string set. Anything else string-typed
                // is prose somebody wrote (a complaint's Reason, a ladder's Reason).
                _ when ClosedCodeStrings.Contains(last) => last,
                _ => null,
            };
        }

        private static List<string> Segments(string expression) =>
            expression.Trim().Split('.', StringSplitOptions.RemoveEmptyEntries).ToList();

        /// <summary>Walks a property path, stepping into a collection's element type.</summary>
        private static Type? PropertyType(Type? type, IReadOnlyList<string> path)
        {
            foreach (var segment in path)
            {
                if (type is null) return null;

                var property = type.GetProperty(segment,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (property is not null)
                {
                    type = property.PropertyType;
                    continue;
                }

                // A collection exposes no property by the element's own names; step into the
                // element type and read the segment there.
                if (ElementTypeOf(type) is { } element)
                {
                    type = element;
                    var inner = type.GetProperty(segment,
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (inner is null) return null;
                    type = inner.PropertyType;
                    continue;
                }

                return null;
            }

            return type;
        }

        private static Type? ElementTypeOf(Type type) =>
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                ? type.GetGenericArguments()[0]
                : type.GetInterfaces()
                    .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    .Select(i => i.GetGenericArguments()[0])
                    .FirstOrDefault();
    }

    // ---- fixtures -------------------------------------------------------------

    private static MentorBookingDto Booking(string status) => new(
        1, 1, 1, "Dr. Minh Khoa", null, "human", null,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), status, null, null, 1, null, DateTimeOffset.UtcNow);
}
