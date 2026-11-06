using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Services;

namespace WebAPI;

/// <summary>
/// #199 / D0.7 / CNT-44: the telemetry schedule. Evaluates every active item whose administration count
/// has crossed a checkpoint, and quarantines the ones that breach a condition.
/// <para>
/// The interval is deliberately not a busy loop: an item's administration count only moves when a
/// learner answers, and the service itself decides whether a checkpoint was crossed. Re-running against
/// unchanged answers is free — the evaluation is keyed by (item, checkpoint), so a second pass writes
/// nothing. That is what makes a poll safe here where a poll over a maintained counter would not be.
/// </para>
/// </summary>
public sealed class ItemTelemetryBackgroundService(
    IServiceProvider serviceProvider,
    ILogger<ItemTelemetryBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ItemTelemetryBackgroundService started with interval {Interval}.", PollingInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var telemetry = scope.ServiceProvider.GetRequiredService<ItemTelemetryService>();

                var run = await telemetry.EvaluateActiveItemsAsync(stoppingToken);
                if (run.NewlyEvaluated.Count > 0)
                {
                    logger.LogInformation(
                        "ItemTelemetryBackgroundService evaluated {Count} item(s); quarantined {Quarantined}.",
                        run.NewlyEvaluated.Count, run.Quarantined.Count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A failed pass must not stop the schedule: the next one reads the same stored answers.
                logger.LogError(ex, "Error occurred while evaluating item telemetry.");
            }

            try
            {
                await Task.Delay(PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("ItemTelemetryBackgroundService stopped.");
    }
}
