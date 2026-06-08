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

public class GoalStatusTrackingJob
{
    private readonly AppDbContext _context;
    private readonly IGoalTrackingService _goalTrackingService;
    private readonly INotificationService _notificationService;
    private readonly BackgroundJobExecutor _executor;
    private readonly ILogger<GoalStatusTrackingJob> _logger;

    public GoalStatusTrackingJob(
        AppDbContext context,
        IGoalTrackingService goalTrackingService,
        INotificationService notificationService,
        BackgroundJobExecutor executor,
        ILogger<GoalStatusTrackingJob> logger)
    {
        _context = context;
        _goalTrackingService = goalTrackingService;
        _notificationService = notificationService;
        _executor = executor;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await _executor.ExecuteAsync("goal-status-tracking", async (executionId, token) =>
        {
            var processed = 0;
            var succeeded = 0;
            var failed = 0;
            var skipped = 0;

            var now = DateTime.UtcNow;

            // Load active goals
            var activeGoals = await _context.GoalSettings
                .Include(g => g.LearnerProfile)
                .Where(g => g.Status == GoalStatus.Active)
                .ToListAsync(token);

            _logger.LogInformation("GoalStatusTrackingJob: Found {Count} active goals to process.", activeGoals.Count);

            foreach (var goal in activeGoals)
            {
                processed++;
                try
                {
                    // 1. Check Deadline Expiration
                    if (goal.Deadline < now)
                    {
                        goal.Status = GoalStatus.Expired;
                        goal.IsCompleted = false;
                        goal.UpdatedAt = now;

                        await _context.SaveChangesAsync(token);
                        _logger.LogInformation("GoalStatusTrackingJob: GoalId {GoalId} has expired.", goal.Id);

                        // Create notification for expiration
                        var expIdempotencyKey = $"goal-expired:{goal.Id}";
                        var expReq = new CreateNotificationRequest
                        {
                            UserId = goal.LearnerProfile.UserId,
                            LearnerProfileId = goal.LearnerProfileId,
                            Type = NotificationType.System,
                            Channel = NotificationChannel.InApp,
                            Title = "Mục tiêu học tập hết hạn",
                            Message = $"Mục tiêu \"{goal.Target}\" của bạn đã hết hạn vào ngày {goal.Deadline:dd/MM/yyyy} mà chưa hoàn thành.",
                            IdempotencyKey = expIdempotencyKey,
                            SourceType = "GoalExpired",
                            SourceId = goal.Id.ToString()
                        };

                        await _notificationService.CreateNotificationAsync(expReq, token);
                        succeeded++;
                        continue;
                    }

                    // 2. Advisory check (completed goals are not expired or checked here since we loaded Active ones)
                    var advisoryDto = _goalTrackingService.GetGoalAdvisory(goal, now);

                    if (advisoryDto.Advisory != GoalAdvisory.Keep)
                    {
                        var advType = advisoryDto.Advisory.ToString().ToLower();
                        var dateStr = now.ToString("yyyy-MM-dd");
                        var idempotencyKey = $"goal-advisory:{goal.Id}:{advType}:{dateStr}";

                        var title = advisoryDto.Advisory == GoalAdvisory.AtRisk 
                            ? "Cảnh báo mục tiêu học tập có rủi ro" 
                            : "Gợi ý điều chỉnh mục tiêu học tập";

                        var notifType = advisoryDto.Advisory == GoalAdvisory.AtRisk
                            ? NotificationType.GoalAtRisk
                            : NotificationType.GoalAtRisk; // map both to GoalAtRisk as per NotificationType enum

                        var req = new CreateNotificationRequest
                        {
                            UserId = goal.LearnerProfile.UserId,
                            LearnerProfileId = goal.LearnerProfileId,
                            Type = notifType,
                            Channel = NotificationChannel.InApp,
                            Title = title,
                            Message = advisoryDto.Reason,
                            IdempotencyKey = idempotencyKey,
                            SourceType = "GoalAdvisory",
                            SourceId = goal.Id.ToString()
                        };

                        var details = await _notificationService.CreateNotificationAsync(req, token);
                        if (details != null)
                        {
                            succeeded++;
                        }
                        else
                        {
                            skipped++; // already created today
                        }
                    }
                    else
                    {
                        skipped++;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex, "Failed to process GoalStatusTracking for GoalId {GoalId}", goal.Id);
                }
            }

            return (processed, succeeded, failed, skipped);
        }, cancellationToken);
    }
}
