using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Infrastructure.Persistence;

namespace CoreLearningSystem.Infrastructure.Services;

public class AchievementCheckingJob
{
    private readonly AppDbContext _context;
    private readonly IAchievementService _achievementService;
    private readonly BackgroundJobExecutor _executor;
    private readonly ILogger<AchievementCheckingJob> _logger;

    public AchievementCheckingJob(
        AppDbContext context,
        IAchievementService achievementService,
        BackgroundJobExecutor executor,
        ILogger<AchievementCheckingJob> logger)
    {
        _context = context;
        _achievementService = achievementService;
        _executor = executor;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await _executor.ExecuteAsync("achievement-checking", async (executionId, token) =>
        {
            var processed = 0;
            var succeeded = 0;
            var failed = 0;
            var skipped = 0;

            var now = DateTime.UtcNow;

            var activeProfiles = await _context.LearnerProfiles
                .Include(p => p.User)
                .Where(p => p.ActivityStatus == ActivityStatus.Active && !p.User.IsLocked)
                .ToListAsync(token);

            _logger.LogInformation("AchievementCheckingJob: Starting reconciliation for {Count} active profiles.", activeProfiles.Count);

            foreach (var profile in activeProfiles)
            {
                processed++;
                try
                {
                    var request = new AchievementEvaluationRequest
                    {
                        UserId = profile.UserId,
                        LearnerProfileId = profile.Id,
                        SourceEventId = $"reconciliation:{profile.Id}:{now:yyyy-MM-dd}",
                        Trigger = AchievementTrigger.GoalCompleted, // Re-evaluates based on overall counts
                        OccurredAt = now
                    };

                    var result = await _achievementService.EvaluateAndAwardAsync(request, token);
                    succeeded += result.AwardedBadges.Count;
                    skipped += result.SkippedDuplicates;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex, "Failed to evaluate achievements for LearnerProfileId {ProfileId}", profile.Id);
                }
            }

            return (processed, succeeded, failed, skipped);
        }, cancellationToken);
    }
}
