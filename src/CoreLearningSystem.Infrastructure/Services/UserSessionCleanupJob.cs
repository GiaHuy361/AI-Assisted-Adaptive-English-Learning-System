using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Infrastructure.Persistence;

namespace CoreLearningSystem.Infrastructure.Services;

public class UserSessionCleanupJob
{
    private readonly AppDbContext _context;
    private readonly BackgroundJobExecutor _executor;
    private readonly ILogger<UserSessionCleanupJob> _logger;

    public UserSessionCleanupJob(
        AppDbContext context,
        BackgroundJobExecutor executor,
        ILogger<UserSessionCleanupJob> logger)
    {
        _context = context;
        _executor = executor;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await _executor.ExecuteAsync("session-cleanup", async (executionId, token) =>
        {
            var processed = 0;
            var succeeded = 0;
            var failed = 0;
            var skipped = 0;

            var now = DateTime.UtcNow;

            // Find sessions that have expired but are still marked Active
            var expiredSessions = await _context.UserSessions
                .Where(s => s.Status == SessionStatus.Active && s.ExpiresAt < now)
                .ToListAsync(token);

            if (expiredSessions.Count == 0)
            {
                return (processed, succeeded, failed, skipped);
            }

            _logger.LogInformation("UserSessionCleanupJob: Found {Count} expired sessions to clean up.", expiredSessions.Count);

            foreach (var session in expiredSessions)
            {
                processed++;
                try
                {
                    session.Status = SessionStatus.Expired;
                    _context.UserSessions.Update(session);
                    succeeded++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex, "Failed to mark session {SessionId} as expired", session.Id);
                }
            }

            await _context.SaveChangesAsync(token);

            return (processed, succeeded, failed, skipped);
        }, cancellationToken);
    }
}
