using System;
using System.Collections.Generic;
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
