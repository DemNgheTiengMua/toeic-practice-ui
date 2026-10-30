using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebAPI.Extensions;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// F4, API side: a client that disconnects while a query runs fires RequestAborted, and SqlClient
/// reports a command cancelled mid-execution as a SqlException ("Operation cancelled by user"),
/// not an OperationCanceledException. The exception handler treats only the latter as a
/// disconnect, so aborted dashboard loads logged "fail: ExceptionHandlerMiddleware". These tests
/// run real commands against SQL Server (LocalDB, or TEST_DB_CONNECTION on CI) through the app's
/// own error pipeline.
/// </summary>
public sealed class ClientAbortDuringQueryTests
{
    [Fact]
    public async Task A_client_abort_mid_query_is_a_disconnect_not_a_logged_failure()
    {
        using var clientLeft = new CancellationTokenSource();

        var (context, errors) = await SendAsync(clientLeft.Token,
            token => RunLongQueryAsync(token, () => clientLeft.CancelAfter(TimeSpan.FromMilliseconds(400))));

        Assert.Equal(StatusCodes.Status499ClientClosedRequest, context.Response.StatusCode);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task A_cancelled_query_the_client_did_not_abort_is_still_logged()
    {
        // Swallowing every cancelled command would hide a sick database behind a quiet log.
        using var other = new CancellationTokenSource();

        var (context, errors) = await SendAsync(CancellationToken.None,
            _ => RunLongQueryAsync(other.Token, () => other.CancelAfter(TimeSpan.FromMilliseconds(400))));

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public async Task A_genuine_database_error_is_logged_even_after_the_client_left()
    {
        using var clientLeft = new CancellationTokenSource();
        clientLeft.Cancel();

        var (_, errors) = await SendAsync(clientLeft.Token, async _ =>
        {
            await using var connection = await OpenMasterAsync();
            await using var command = new SqlCommand("THROW 50001, 'genuine failure', 1;", connection);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        });

        Assert.NotEmpty(errors);
    }

    /// <summary>Runs one request through UseApiProblemDetails and the given endpoint, recording
    /// every log entry at Error or above.</summary>
    private static async Task<(HttpContext Context, IReadOnlyList<string> Errors)> SendAsync(
        CancellationToken requestAborted, Func<CancellationToken, Task> endpoint)
    {
        var recorder = new ErrorRecorder();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(recorder);
        builder.Services.AddApiProblemDetails();
        await using var app = builder.Build();

        app.UseApiProblemDetails();
        IApplicationBuilder pipeline = app;
        pipeline.Run(context => endpoint(context.RequestAborted));
        var invoke = pipeline.Build();

        await using var scope = app.Services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, RequestAborted = requestAborted };
        await invoke(context);
        return (context, recorder.Entries.ToList());
    }

    /// <summary>A query still running when the cancel fires: the timer starts only once the
    /// connection is open, so the cancel lands mid-execution, the case SqlClient reports as a
    /// SqlException.</summary>
    private static async Task RunLongQueryAsync(CancellationToken token, Action startCancelTimer)
    {
        await using var connection = await OpenMasterAsync();
        await using var command = new SqlCommand("WAITFOR DELAY '00:00:10'; SELECT 1;", connection);
        startCancelTimer();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
        }
    }

    private static async Task<SqlConnection> OpenMasterAsync()
    {
        var builder = new SqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("TEST_DB_CONNECTION")
            ?? @"Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True")
        {
            InitialCatalog = "master",
        };
        var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private sealed class ErrorRecorder : ILoggerProvider
    {
        public ConcurrentQueue<string> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Recorder(categoryName, Entries);

        public void Dispose()
        {
        }

        private sealed class Recorder(string category, ConcurrentQueue<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                {
                    entries.Enqueue($"{category}: {formatter(state, exception)} [{exception?.GetType().Name}]");
                }
            }
        }
    }
}
