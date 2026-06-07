using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Infrastructure.Persistence;

namespace CoreLearningSystem.Infrastructure.Services;

public class BackgroundJobExecutor
{
    private readonly AppDbContext _context;
    private readonly ILogger<BackgroundJobExecutor> _logger;

    public BackgroundJobExecutor(AppDbContext context, ILogger<BackgroundJobExecutor> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task ExecuteAsync(
        string jobName,
        Func<string, CancellationToken, Task<(int processed, int succeeded, int failed, int skipped)>> jobAction,
        CancellationToken cancellationToken)
    {
        var executionId = Guid.NewGuid().ToString();
        var execution = new BackgroundJobExecution
        {
            JobName = jobName,
            ExecutionId = executionId,
            StartedAt = DateTime.UtcNow,
            Status = JobStatus.Running,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            _context.BackgroundJobExecutions.Add(execution);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write initial BackgroundJobExecution for job {JobName} with ExecutionId {ExecutionId}.", jobName, executionId);
        }

        var sw = Stopwatch.StartNew();
        JobStatus finalStatus = JobStatus.Failed;
        int processed = 0, succeeded = 0, failed = 0, skipped = 0;
        string? errorMessage = null;

        try
        {
            (processed, succeeded, failed, skipped) = await jobAction(executionId, cancellationToken);
            finalStatus = failed > 0 ? JobStatus.PartiallySucceeded : JobStatus.Succeeded;
        }
        catch (OperationCanceledException)
        {
            finalStatus = JobStatus.Cancelled;
            errorMessage = "Job was cancelled.";
            _logger.LogWarning("Job {JobName} with ExecutionId {ExecutionId} was cancelled.", jobName, executionId);
        }
        catch (Exception ex)
        {
            finalStatus = JobStatus.Failed;
            errorMessage = ex.Message + "\n" + ex.StackTrace;
            _logger.LogError(ex, "Job {JobName} with ExecutionId {ExecutionId} failed.", jobName, executionId);
            throw; // Rethrow to let Hangfire scheduling retry mechanism know about it
        }
        finally
        {
            sw.Stop();

            // Try to update log safely
            try
            {
                var dbExecution = await _context.BackgroundJobExecutions.FindAsync(new object[] { execution.Id }, cancellationToken);
                if (dbExecution != null)
                {
                    dbExecution.CompletedAt = DateTime.UtcNow;
                    dbExecution.Status = finalStatus;
                    dbExecution.ProcessedCount = processed;
                    dbExecution.SuccessCount = succeeded;
                    dbExecution.FailedCount = failed;
                    dbExecution.SkippedCount = skipped;
                    dbExecution.ErrorMessage = errorMessage;
                    dbExecution.DurationMilliseconds = sw.Elapsed.TotalMilliseconds;

                    await _context.SaveChangesAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update final BackgroundJobExecution log for job {JobName} with ExecutionId {ExecutionId}.", jobName, executionId);
            }
        }
    }
}
