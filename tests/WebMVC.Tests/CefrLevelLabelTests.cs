using System;
using System.Collections.Generic;
using System.Linq;
using Contracts;
using Microsoft.Extensions.Logging;
using Xunit;

namespace WebMVC.Tests;

/// <summary>
/// #182 / D19: every level renders as its own label, and a code the registry does not know renders as
/// readable text and is logged — it never reaches the page as itself. "below_A1" in a badge is the
/// defect this seam exists to make impossible (LRN-34).
/// </summary>
public sealed class CefrLevelLabelTests
{
    [Fact]
    public void Every_level_renders_as_its_own_label()
    {
        var logger = new CapturingLogger();

        var labels = new[] { "pre_a1", "A1", "A2", "B1", "B2", "C1", "C2" }
            .Select(code => CefrLevelLabels.LabelForCode(code, logger))
            .ToList();

        Assert.Equal(["Below A1", "A1", "A2", "B1", "B2", "C1", "C2"], labels);
        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public void A_case_different_code_still_finds_its_label()
    {
        Assert.Equal("B2", CefrLevelLabels.LabelForCode("b2", new CapturingLogger()));
    }

    [Fact]
    public void An_unmapped_code_falls_back_to_a_readable_label_and_logs()
    {
        var logger = new CapturingLogger();

        foreach (var code in new[] { "below_A1", "CAT_B1", "D1", "" })
        {
            var label = CefrLevelLabels.LabelForCode(code, logger);

            Assert.Equal(CefrLevelLabels.Unknown, label);
            Assert.DoesNotContain("_", label, StringComparison.Ordinal);
            Assert.NotEqual(code, label);
        }

        Assert.Equal(4, logger.Warnings.Count);
        Assert.Contains(logger.Warnings, warning => warning.Contains("below_A1", StringComparison.Ordinal));
    }

    private sealed class CapturingLogger : ILogger
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
