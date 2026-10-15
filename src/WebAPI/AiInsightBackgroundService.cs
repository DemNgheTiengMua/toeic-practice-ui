using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Services;

namespace WebAPI;

/// <summary>
/// Background worker processing pending AI insights in the queue.
/// Satisfies Ticket #20 acceptance criteria: "Background worker in WebAPI polling pending insights".
/// </summary>
public sealed class AiInsightBackgroundService(
    IServiceProvider serviceProvider,
    ILogger<AiInsightBackgroundService> logger) : BackgroundService
{
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("AiInsightBackgroundService started with interval {Interval}.", _pollingInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var insightService = scope.ServiceProvider.GetRequiredService<IAiInsightService>();

                int processed = await insightService.ProcessPendingInsightsAsync(stoppingToken);
                if (processed > 0)
                {
                    logger.LogInformation("AiInsightBackgroundService processed {Count} pending insight(s).", processed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred while processing AI insight queue.");
            }

            try
            {
                await Task.Delay(_pollingInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("AiInsightBackgroundService stopped.");
    }
}
