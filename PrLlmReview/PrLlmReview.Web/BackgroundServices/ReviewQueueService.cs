using Microsoft.Extensions.Hosting;
using PrLlmReview.Services;

namespace PrLlmReview.BackgroundServices;

/// <summary>
/// Long-running hosted service that drains the ReviewQueue and runs each job through
/// the ReviewOrchestratorService. Job failures are logged but do not stop the service.
/// </summary>
public sealed class ReviewQueueService : BackgroundService
{
    private readonly ReviewQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ReviewQueueService> _logger;

    public ReviewQueueService(
        ReviewQueue queue,
        IServiceProvider serviceProvider,
        ILogger<ReviewQueueService> logger)
    {
        _queue           = queue;
        _serviceProvider = serviceProvider;
        _logger          = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("Review queue service started.");

        await foreach (var job in _queue.ReadAllAsync(ct))
        {
            _logger.LogInformation(
                "Processing review for PR #{PrId} — {Title}", job.PullRequestId, job.Title);

            try
            {
                // Each job gets its own DI scope so scoped services are properly disposed
                await using var scope = _serviceProvider.CreateAsyncScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<ReviewOrchestratorService>();
                await orchestrator.RunAsync(job, ct);

                _logger.LogInformation("Review complete for PR #{PrId}", job.PullRequestId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Review failed for PR #{PrId} — {Title}", job.PullRequestId, job.Title);
            }
        }

        _logger.LogInformation("Review queue service stopped.");
    }
}
