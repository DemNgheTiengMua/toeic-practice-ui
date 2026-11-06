using System;
using System.Collections.Generic;
using System.Linq;
using BusinessObjects;
using Contracts;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #182 / D19: the level registry and the level type are two lists of the same six levels plus the
/// floor, kept in different layers because the view layer may not reference the domain. This is the
/// seam that stops them drifting: a level added to <see cref="CefrLevel"/> with no label lands here.
/// </summary>
public sealed class CefrLevelLabelDriftTests
{
    [Fact]
    public void The_registry_labels_exactly_the_levels_the_type_defines()
    {
        var typeCodes = CefrLevel.Reportable.Select(level => level.Code).Order(StringComparer.OrdinalIgnoreCase);
        var registryCodes = CefrLevelLabels.KnownCodes.Order(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(typeCodes, registryCodes);
        Assert.Equal(7, registryCodes.Count());
    }

    [Fact]
    public void Every_level_renders_as_its_own_label_and_never_as_its_code()
    {
        var logger = new NullLogger();

        var labels = CefrLevel.Reportable
            .Select(level => CefrLevelLabels.LabelForCode(level.Code, logger))
            .ToList();

        Assert.Equal(7, labels.Distinct().Count());
        Assert.DoesNotContain(labels, label => label.Contains('_', StringComparison.Ordinal));
        Assert.DoesNotContain(CefrLevelLabels.Unknown, labels);
        Assert.Empty(logger.Warnings);
        Assert.Equal("Below A1", CefrLevelLabels.LabelForCode(CefrLevel.PreA1.Code, logger));
    }

    [Fact]
    public void A_certificate_band_code_is_not_a_level_and_falls_back()
    {
        // "below_A1" is the certificate band table's key for a total under the lowest band. It is a
        // band, not the level floor, and must not silently become one.
        var logger = new NullLogger();

        Assert.Equal(CefrLevelLabels.Unknown, CefrLevelLabels.LabelForCode("below_A1", logger));
        Assert.Single(logger.Warnings);
        Assert.Contains("below_A1", logger.Warnings[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// #202 / D19: the same seam for the skill axis. S-01 prints a skill name above each table, and
    /// <c>listening</c> in a placard is the same defect as <c>below_A1</c> in a badge.
    /// </summary>
    [Fact]
    public void The_modality_registry_labels_exactly_the_four_skills_the_enum_defines()
    {
        var enumCodes = Enum.GetValues<ModalityDto>().Select(v => EnumCodes.ToCode(v)).Order(StringComparer.OrdinalIgnoreCase);
        var registryCodes = ModalityLabels.KnownCodes.Order(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(enumCodes, registryCodes);
        Assert.Equal(4, registryCodes.Count());
    }

    [Fact]
    public void Every_skill_renders_as_its_own_capitalised_label_and_never_as_its_code()
    {
        var logger = new NullLogger();

        var labels = Enum.GetValues<ModalityDto>()
            .Select(v => ModalityLabels.LabelForCode(EnumCodes.ToCode(v), logger))
            .ToList();

        Assert.Equal(4, labels.Distinct().Count());
        Assert.DoesNotContain(labels, label => label.Contains('_', StringComparison.Ordinal));
        Assert.DoesNotContain(ModalityLabels.Unknown, labels);
        Assert.Empty(logger.Warnings);
        // A lowercase code in a heading is the defect; the label is the word a reader expects.
        Assert.DoesNotContain(labels, label => label == label.ToLowerInvariant());
    }

    [Fact]
    public void An_unknown_skill_code_falls_back_and_logs()
    {
        var logger = new NullLogger();

        Assert.Equal(ModalityLabels.Unknown, ModalityLabels.LabelForCode("grammar", logger));
        Assert.Single(logger.Warnings);
        Assert.Contains("grammar", logger.Warnings[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// #229: the picker's list is the authored ladder, exactly. An authoring screen that offered the
    /// floor would produce a request the service refuses; one that offered B3 would produce a code the
    /// schema refuses. The list is in Contracts because WebMVC cannot see <c>BusinessObjects</c>, which
    /// is precisely why it needs this guard.
    /// </summary>
    [Fact]
    public void The_pickers_level_list_is_exactly_the_authored_ladder()
    {
        Assert.Equal(
            CefrLevel.AuthoredLevels.Select(level => level.Code),
            CefrLevelLabels.AuthoredCodes);

        Assert.DoesNotContain(CefrLevel.PreA1.Code, CefrLevelLabels.AuthoredCodes);
        Assert.All(CefrLevelLabels.AuthoredCodes,
            code => Assert.True(CefrLevel.TryParseAuthored(code, out _), $"{code} is pickable but not authorable"));
    }

    private sealed class NullLogger : ILogger
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Warnings.Add(formatter(state, exception));
        }
    }
}
