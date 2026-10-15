using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Services;

namespace WebAPI;

/// <summary>
/// Background worker processing pending AI gradings in the queue.
/// Satisfies Ticket #18 acceptance criteria: "Background worker processes the pending queue".
/// </summary>
public sealed class AiGradingBackgroundService(
    IServiceProvider serviceProvider,
    ILogger<AiGradingBackgroundService> logger) : BackgroundService
{
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("AiGradingBackgroundService started with interval {Interval}.", _pollingInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var writingService = scope.ServiceProvider.GetRequiredService<IWritingPracticeService>();

                int processed = await writingService.ProcessPendingGradingsAsync(stoppingToken);
                if (processed > 0)
                {
                    logger.LogInformation("AiGradingBackgroundService processed {Count} pending grading(s).", processed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred while processing AI grading queue.");
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

        logger.LogInformation("AiGradingBackgroundService stopped.");
    }
}
