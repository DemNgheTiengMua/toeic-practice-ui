using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebAPI.Extensions;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// F4: a client that disconnects while an endpoint queries the database makes SqlClient throw
/// SqlException "Operation cancelled by user". ExceptionHandlerMiddleware only treats
/// OperationCanceledException and IOException as a disconnect, so UseApiProblemDetails has to
/// recognise the rest itself, or every abandoned page load is logged as an unhandled error.
/// </summary>
public sealed class ProblemDetailsClientAbortTests
{
    [Fact]
    public async Task A_query_cancelled_by_a_client_abort_answers_499_without_an_error_log()
    {
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();

        var (status, levels) = await RunAsync(aborted.Token);

        Assert.Equal(StatusCodes.Status499ClientClosedRequest, status);
        Assert.DoesNotContain(levels, level => level >= LogLevel.Error);
    }

    [Fact]
    public async Task The_same_failure_on_a_live_request_is_still_logged_and_answered_500()
    {
        var (status, levels) = await RunAsync(CancellationToken.None);

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.Contains(LogLevel.Error, levels);
    }

    private static async Task<(int Status, LogLevel[] Levels)> RunAsync(CancellationToken requestAborted)
    {
        var log = new LevelRecorder();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(log));
        services.AddMetrics();
        services.AddSingleton(new DiagnosticListener("WebAPI.Tests"));
        services.AddApiProblemDetails();
        await using var provider = services.BuildServiceProvider();

        var app = new ApplicationBuilder(provider);
        app.UseApiProblemDetails();
        // SqlException has no public constructor; only its type matters to the handler.
        var failure = (Exception)RuntimeHelpers.GetUninitializedObject(typeof(SqlException));
        app.Run(_ => throw failure);

        var context = new DefaultHttpContext { RequestServices = provider };
        context.RequestAborted = requestAborted;
        await app.Build()(context);

        return (context.Response.StatusCode, log.Levels.ToArray());
    }

    /// <summary>Records only the level of each entry; formatting an uninitialised exception is
    /// not what these tests are about.</summary>
    private sealed class LevelRecorder : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<LogLevel> Levels { get; } = new();

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Levels.Enqueue(logLevel);

        public void Dispose()
        {
        }
    }
}
