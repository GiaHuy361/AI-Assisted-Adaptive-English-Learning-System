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

public class LearningReminderJob
{
    private readonly AppDbContext _context;
    private readonly INotificationService _notificationService;
    private readonly BackgroundJobExecutor _executor;
    private readonly ILogger<LearningReminderJob> _logger;

    public LearningReminderJob(
        AppDbContext context,
        INotificationService notificationService,
        BackgroundJobExecutor executor,
        ILogger<LearningReminderJob> logger)
    {
        _context = context;
        _notificationService = notificationService;
        _executor = executor;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await _executor.ExecuteAsync("learning-reminder", async (executionId, token) =>
        {
            var processed = 0;
            var succeeded = 0;
            var failed = 0;
            var skipped = 0;

            var activeProfiles = await _context.LearnerProfiles
                .Include(p => p.User)
                .Where(p => p.ActivityStatus == ActivityStatus.Active && !p.User.IsLocked)
                .ToListAsync(token);

            _logger.LogInformation("LearningReminderJob: Found {Count} active learner profiles to check.", activeProfiles.Count);

            var now = DateTime.UtcNow;

            foreach (var profile in activeProfiles)
            {
                processed++;
                try
                {
                    var latestActivity = await LearnerActivityResolver.GetLatestActivityUtcAsync(_context, profile.Id, token);
                    
                    // Default threshold: 48 hours (2 days) of inactivity
                    if (latestActivity == null || (now - latestActivity.Value).TotalHours >= 48)
                    {
                        var utcDateStr = now.ToString("yyyy-MM-dd");
                        var idempotencyKey = $"reminder:{profile.UserId}:{utcDateStr}";

                        // Create reminder
                        var req = new CreateNotificationRequest
                        {
                            UserId = profile.UserId,
                            LearnerProfileId = profile.Id,
                            Type = NotificationType.LearningReminder,
                            Channel = NotificationChannel.InAppAndEmail,
                            Title = "Nhắc nhở học tập hàng ngày",
                            Message = "Đã hơn 2 ngày bạn chưa đăng nhập hoặc học tập. Hãy dành 15 phút hôm nay để luyện tập tiếng Anh nhé!",
                            IdempotencyKey = idempotencyKey,
                            SourceType = "Inactivity",
                            SourceId = profile.Id.ToString()
                        };

                        var details = await _notificationService.CreateNotificationAsync(req, token);
                        if (details != null)
                        {
                            succeeded++;
                        }
                        else
                        {
                            skipped++; // Means it was skipped due to idempotency key
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
                    _logger.LogError(ex, "Failed to check or send reminder for LearnerProfileId {ProfileId}", profile.Id);
                }
            }

            return (processed, succeeded, failed, skipped);
        }, cancellationToken);
    }
}
