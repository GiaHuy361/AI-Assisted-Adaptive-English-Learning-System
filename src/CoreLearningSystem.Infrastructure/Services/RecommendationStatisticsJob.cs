using System;
using System.Threading;
using System.Threading.Tasks;
using CoreLearningSystem.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace CoreLearningSystem.Infrastructure.Services;

public class RecommendationStatisticsJob
{
    private readonly IRecommendationAnalyticsService _analyticsService;
    private readonly BackgroundJobExecutor _executor;
    private readonly ILogger<RecommendationStatisticsJob> _logger;

    public RecommendationStatisticsJob(
        IRecommendationAnalyticsService analyticsService,
        BackgroundJobExecutor executor,
        ILogger<RecommendationStatisticsJob> logger)
    {
        _analyticsService = analyticsService;
        _executor = executor;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await _executor.ExecuteAsync("recommendation-statistics", async (executionId, token) =>
        {
            var processed = 1;
            var succeeded = 0;
            var failed = 0;
            var skipped = 0;

            try
            {
                var now = DateTime.UtcNow;
                var periodStart = now.AddDays(-7);
                var periodEnd = now;

                _logger.LogInformation("RecommendationStatisticsJob: Computing snapshot for period {Start} to {End}", periodStart, periodEnd);
                
                await _analyticsService.ComputeAndSaveSnapshotAsync(periodStart, periodEnd);
                
                succeeded = 1;
            }
            catch (Exception ex)
            {
                failed = 1;
                _logger.LogError(ex, "Failed to compute recommendation statistics snapshot.");
            }

            return (processed, succeeded, failed, skipped);
        }, cancellationToken);
    }
}
