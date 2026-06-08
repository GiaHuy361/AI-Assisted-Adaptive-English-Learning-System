using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CoreLearningSystem.Application.Options;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Infrastructure.Persistence;

namespace CoreLearningSystem.Infrastructure.Services;

public class CleanupJob
{
    private readonly AppDbContext _context;
    private readonly CleanupOptions _options;
    private readonly BackgroundJobExecutor _executor;
    private readonly ILogger<CleanupJob> _logger;

    public CleanupJob(
        AppDbContext context,
        IOptions<CleanupOptions> options,
        BackgroundJobExecutor executor,
        ILogger<CleanupJob> logger)
    {
        _context = context;
        _options = options.Value;
        _executor = executor;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await _executor.ExecuteAsync("cleanup", async (executionId, token) =>
        {
            var processed = 0;
            var succeeded = 0;
            var failed = 0;
            var skipped = 0;

            var now = DateTime.UtcNow;

            // 1. Cleanup old successful NotificationDeliveryAttempt records
            try
            {
                var successCutoff = now.AddDays(-_options.NotificationAttemptRetentionDays);
                var successAttempts = await _context.NotificationDeliveryAttempts
                    .Where(a => a.Status == NotificationStatus.Sent && a.CompletedAt <= successCutoff)
                    .ToListAsync(token);

                if (successAttempts.Any())
                {
                    _context.NotificationDeliveryAttempts.RemoveRange(successAttempts);
                    var count = await _context.SaveChangesAsync(token);
                    succeeded += count;
                    processed += successAttempts.Count;
                    _logger.LogInformation("CleanupJob: Removed {Count} successful notification delivery attempts.", successAttempts.Count);
                }
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex, "CleanupJob: Error clearing successful notification attempts.");
            }

            // 2. Cleanup old failed NotificationDeliveryAttempt records (failed delivery diagnostic details)
            try
            {
                var failureCutoff = now.AddDays(-_options.FailedNotificationRetentionDays);
                var failedAttempts = await _context.NotificationDeliveryAttempts
                    .Where(a => a.Status == NotificationStatus.Failed && a.CompletedAt <= failureCutoff)
                    .ToListAsync(token);

                if (failedAttempts.Any())
                {
                    _context.NotificationDeliveryAttempts.RemoveRange(failedAttempts);
                    var count = await _context.SaveChangesAsync(token);
                    succeeded += count;
                    processed += failedAttempts.Count;
                    _logger.LogInformation("CleanupJob: Removed {Count} failed notification delivery attempts.", failedAttempts.Count);
                }
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex, "CleanupJob: Error clearing failed notification attempts.");
            }

            // 3. Cleanup old BackgroundJobExecution records
            try
            {
                var jobLogCutoff = now.AddDays(-_options.JobLogRetentionDays);
                // Never delete running jobs or logs newer than retention
                var oldJobLogs = await _context.BackgroundJobExecutions
                    .Where(e => e.Status != JobStatus.Running && e.CompletedAt <= jobLogCutoff)
                    .ToListAsync(token);

                if (oldJobLogs.Any())
                {
                    _context.BackgroundJobExecutions.RemoveRange(oldJobLogs);
                    var count = await _context.SaveChangesAsync(token);
                    succeeded += count;
                    processed += oldJobLogs.Count;
                    _logger.LogInformation("CleanupJob: Removed {Count} background job execution logs.", oldJobLogs.Count);
                }
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex, "CleanupJob: Error clearing old background job logs.");
            }

            return (processed, succeeded, failed, skipped);
        }, cancellationToken);
    }
}
