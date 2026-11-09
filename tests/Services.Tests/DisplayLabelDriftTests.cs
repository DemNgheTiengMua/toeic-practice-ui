using System;
using System.Collections.Generic;
using System.Linq;
using Contracts;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #220 / D19: no internal code reaches a page. This file holds the display registry against the
/// <c>Contracts</c> assembly, which is the layer WebMVC can actually see.
/// <para>
/// <b>Why the whole assembly and not a hand-picked list.</b> "Every code the product renders" is not a
/// list anyone can type: it is whatever a DTO property happens to carry today. A typed list drifts the
/// first time an author adds a DTO, and it drifts silently, because the page still renders. Reflecting
/// over <c>typeof(DisplayLabels).Assembly</c> is a definition that cannot fall behind: a new enum in
/// Contracts is in the set the moment it compiles.
/// </para>
/// <para>
/// <b>The three false passes this file exists to refuse.</b> A registry that maps every member to
/// itself ("PendingReview" to "PendingReview") is registered and changes nothing; a registry that
/// returns the code when it does not know it is a fallback that leaks; and a registry whose map is
/// case-sensitive passes every lowercase fixture and fails the one mixed-case row in production.
/// </para>
/// </summary>
public sealed class DisplayLabelDriftTests
{
    private static readonly IReadOnlyList<Type> ContractEnums = typeof(DisplayLabels).Assembly
        .GetTypes()
        .Where(type => type.IsEnum && type.IsPublic)
        .OrderBy(type => type.Name, StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// <b>The input that would pass a wrong registry:</b> one whose coverage is a hand-typed list of
    /// the enums somebody remembered. It would pass every assertion about those enums, and the first
    /// DTO added afterwards would print raw. This asserts the reflected set is non-trivial and fully
    /// registered, in both directions, so neither a missing family nor an invented one passes.
    /// </summary>
    [Fact]
    public void Every_enum_in_Contracts_is_registered_and_no_family_is_orphaned()
    {
        Assert.NotEmpty(ContractEnums);
        // A guard that reports "covered" for an empty set proves nothing.
        Assert.True(ContractEnums.Count >= 40, $"only {ContractEnums.Count} enums found in Contracts");

        var unregistered = ContractEnums.Where(type => !DisplayLabels.IsRegistered(type)).Select(t => t.Name).ToList();
        Assert.True(unregistered.Count == 0,
            $"Contracts enums with no display family: {string.Join(", ", unregistered)}");

        var registered = DisplayLabels.RegisteredTypes.ToHashSet();
        var orphaned = registered.Except(ContractEnums).Select(t => t.Name).ToList();
        Assert.True(orphaned.Count == 0,
            $"display families for types that are not Contracts enums: {string.Join(", ", orphaned)}");
    }

    /// <summary>
    /// <b>The input that would pass a wrong registry:</b> a family covering the enum but missing one
    /// member - the newest status, the one added last sprint. Everything about the enum looks
    /// registered; one value prints the fallback in production and is logged nowhere near the author.
    /// The key here is the wire code, not the member name: the page receives <c>in_progress</c>.
    /// </summary>
    [Fact]
    public void Every_value_of_every_registered_enum_has_a_label_for_its_wire_code()
    {
        var logger = new CapturingLogger();
        var missing = new List<string>();

        foreach (var enumType in ContractEnums)
        {
            var labels = DisplayLabels.LabelsFor(enumType);
            foreach (var value in Enum.GetValues(enumType).Cast<Enum>())
            {
                var code = value.ToWireCode();
                if (!labels.ContainsKey(code))
                {
                    missing.Add($"{enumType.Name}.{value} (wire code {code})");
                }
            }
        }

        Assert.True(missing.Count == 0, $"unlabelled enum values: {string.Join(", ", missing)}");
        Assert.Empty(logger.Errors);
    }

    /// <summary>
    /// <b>The input that would pass a wrong registry:</b> the dictionary is keyed by the member's own
    /// name, so <c>PendingAi</c> renders as "PendingAi" and every completeness check is satisfied. A
    /// member name that is not already a readable word - two capitals, a digit, an underscore - must
    /// render as something else. The single readable word is allowed to be its own label, because
    /// "Paid" is prose.
    /// </summary>
    [Fact]
    public void A_member_name_that_is_not_prose_never_becomes_its_own_label()
    {
        var offenders = new List<string>();

        foreach (var enumType in ContractEnums)
        {
            var labels = DisplayLabels.LabelsFor(enumType);
            foreach (var value in Enum.GetValues(enumType).Cast<Enum>())
            {
                var member = value.ToString();
                if (!labels.TryGetValue(value.ToWireCode(), out var label))
                {
                    continue; // the completeness test reports this one
                }

                var isOneReadableWord = IsProse(member);
                if (!isOneReadableWord && string.Equals(label, member, StringComparison.Ordinal))
                {
                    offenders.Add($"{enumType.Name}.{member} renders as itself");
                }
            }
        }

        Assert.True(offenders.Count == 0, string.Join("; ", offenders));
    }

    /// <summary>
    /// <b>The input that would pass a wrong registry:</b> a label that is the wire code, or the wire
    /// code with its underscores left in. <c>pending_ai</c> in a badge is the defect this ticket
    /// exists for. A code that is already prose (<c>Imports</c>, <c>Exams</c>) may stay itself - that
    /// is a word, not a code - so the rule is about the codes that are not prose.
    /// </summary>
    [Fact]
    public void No_label_carries_the_wire_code_or_an_underscore()
    {
        var offenders = new List<string>();

        foreach (var enumType in ContractEnums)
        {
            foreach (var (code, label) in DisplayLabels.LabelsFor(enumType))
            {
                if (label.Contains('_', StringComparison.Ordinal)
                    || (!IsProse(code) && string.Equals(label, code, StringComparison.Ordinal)))
                {
                    offenders.Add($"{enumType.Name}: {code} renders as \"{label}\"");
                }
            }
        }

        Assert.True(offenders.Count == 0, string.Join("; ", offenders));
    }

    /// <summary>
    /// <b>The input that would pass a wrong registry:</b> a family built with the default
    /// <see cref="StringComparer.Ordinal"/> while <see cref="ModalityLabels"/> uses
    /// <see cref="StringComparer.OrdinalIgnoreCase"/>. Every lowercase fixture resolves, so the tests
    /// pass - until a payload carries <c>NotMeasured</c> or a section stores <c>STANDARD_VERTICAL</c>
    /// in a differently-cased form. This asserts every family agrees about casing.
    /// </summary>
    [Fact]
    public void Every_family_resolves_a_code_regardless_of_case()
    {
        var offenders = new List<string>();

        foreach (var enumType in ContractEnums)
        {
            foreach (var (code, label) in DisplayLabels.LabelsFor(enumType))
            {
                foreach (var variant in new[] { code.ToUpperInvariant(), code.ToLowerInvariant() })
                {
                    if (!DisplayLabels.LabelsFor(enumType).TryGetValue(variant, out var resolved)
                        || !string.Equals(resolved, label, StringComparison.Ordinal))
                    {
                        offenders.Add($"{enumType.Name}: {code} does not resolve as {variant}");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0, string.Join("; ", offenders));

        // The comparison itself, through the public seam rather than the dictionary.
        Assert.Equal("Speaking", DisplayLabels.LabelForCode<ModalityDto>("SpEaKiNg", new CapturingLogger()));
    }

    /// <summary>
    /// <b>The input that would pass a wrong registry:</b> a fallback that returns the code, which is
    /// the shipping defect this ticket names - the page looks complete and says <c>pending_ai</c>. The
    /// fallback must be readable, must not be the code, and must be logged at
    /// <see cref="LogLevel.Error"/>: an unregistered enum value can only come from our own build.
    /// </summary>
    [Fact]
    public void An_unregistered_code_renders_a_readable_fallback_and_logs_an_error()
    {
        foreach (var (enumType, code) in new (Type, string)[]
                 {
                     (typeof(OrderStatusDto), "refunded"),
                     (typeof(ExamStatusDto), ""),
                     (typeof(AttemptStatusDto), "In Progress"),
                     (typeof(CreditReasonDto), "premium_path"),
                 })
        {
            var logger = new CapturingLogger();
            var label = LabelByReflection(enumType, code, logger);

            Assert.NotEqual(code, label);
            Assert.DoesNotContain("_", label, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.Single(logger.Errors);
            Assert.Empty(logger.Warnings);
        }

        // A null is the fourth shape of "no code", and it is logged with something greppable.
        var nullLogger = new CapturingLogger();
        Assert.Equal(DisplayLabels.FallbackFor(typeof(OrderStatusDto)),
            DisplayLabels.LabelForCode<OrderStatusDto>(null, nullLogger));
        Assert.Single(nullLogger.Errors);
        Assert.Contains("<null>", nullLogger.Errors[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// A registered code takes the error-free path, which is what makes the previous test's
    /// <c>Single(Errors)</c> meaningful: without this, a registry that logged on every call would
    /// satisfy it.
    /// </summary>
    [Fact]
    public void A_registered_code_renders_its_label_and_logs_nothing()
    {
        var logger = new CapturingLogger();

        Assert.Equal("Confirming with the gateway",
            DisplayLabels.LabelForCode<OrderStatusDto>("reconciling", logger));
        Assert.Equal("Free response", DisplayLabels.LabelFor(QuestionTypeDto.FreeResponse, logger));
        Assert.Equal("Not examined by this certificate",
            DisplayLabels.LabelFor(MentorSkillStandingStatus.NotExamined, logger));
        Assert.Empty(logger.Errors);
        Assert.Empty(logger.Warnings);
    }

    /// <summary>
    /// ADR-0012: skill words have one writer. <see cref="DisplayLabels"/> delegates to
    /// <see cref="ModalityLabels"/> rather than keeping its own copy, so the two can never disagree
    /// about what <c>listening</c> is called.
    /// </summary>
    [Fact]
    public void The_skill_words_stay_owned_by_ModalityLabels()
    {
        foreach (var code in ModalityLabels.KnownCodes)
        {
            Assert.Equal(
                ModalityLabels.LabelForCode(code, new CapturingLogger()),
                DisplayLabels.LabelForCode<ModalityDto>(code, new CapturingLogger()));
        }
    }

    /// <summary>
    /// Every family carries its own fallback, and no fallback is a code. A single shared "Not reported"
    /// would be simpler and say less: "Order status not reported" tells the reader which column failed,
    /// "Not reported" alone does not. <b>The input that would pass a wrong test:</b> a distinctness
    /// count, which one degraded family among forty cannot move. The assertion is per family.
    /// </summary>
    [Fact]
    public void Every_family_has_its_own_readable_fallback()
    {
        var generic = new List<string>();

        foreach (var enumType in ContractEnums)
        {
            var logger = new CapturingLogger();
            var fallback = LabelByReflection(enumType, "not_a_real_code", logger);

            Assert.False(string.IsNullOrWhiteSpace(fallback));
            Assert.DoesNotContain("_", fallback, StringComparison.Ordinal);
            Assert.NotEqual("not_a_real_code", fallback);
            Assert.Single(logger.Errors);
            Assert.Equal(fallback, DisplayLabels.FallbackFor(enumType));

            if (string.Equals(fallback, DisplayLabels.Unknown, StringComparison.Ordinal))
            {
                generic.Add(enumType.Name);
            }
        }

        Assert.True(generic.Count == 0,
            $"these families fall back to the generic \"{DisplayLabels.Unknown}\", which names no column: {string.Join(", ", generic)}");
    }

    private static string LabelByReflection(Type enumType, string? code, ILogger logger) =>
        (string)typeof(DisplayLabels)
            .GetMethod(nameof(DisplayLabels.LabelForCode))!
            .MakeGenericMethod(enumType)
            .Invoke(null, [code, logger])!;

    /// <summary>
    /// Whether a code is already a word a person would write: no underscore, no digit, and not the
    /// CamelCase or SHOUT_CASE shape of an enum's own name. <c>Imports</c> is prose; <c>STANDARD_VERTICAL</c>,
    /// <c>pending_ai</c> and <c>Cluster3</c> are not.
    /// </summary>
    private static bool IsProse(string code) =>
        code.Length > 0
        && code[0] == char.ToUpperInvariant(code[0])
        && code.Skip(1).All(c => char.IsLower(c) || c == ' ');

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Errors { get; } = [];

        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error) Errors.Add(formatter(state, exception));
            if (logLevel == LogLevel.Warning) Warnings.Add(formatter(state, exception));
        }
    }
}
