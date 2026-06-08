using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdaptiveLearning.Contracts.Events;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Domain.Enums;

namespace AdaptiveLearning.Worker.Handlers;

public class BadgeAwardedEventHandler : IEventHandler<BadgeAwardedEvent>
{
    private readonly INotificationService _notificationService;
    private readonly ILogger<BadgeAwardedEventHandler> _logger;

    public BadgeAwardedEventHandler(
        INotificationService notificationService,
        ILogger<BadgeAwardedEventHandler> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task HandleAsync(BadgeAwardedEvent ev)
    {
        if (ev == null) throw new ArgumentNullException(nameof(ev));

        _logger.LogInformation("BadgeAwardedEventHandler received event. EventId: {EventId}, UserId: {UserId}, LearnerProfileId: {ProfileId}, BadgeCode: {Code}, BadgeName: {Name}, MetricValue: {Metric}, Reason: {Reason}",
            ev.EventId, ev.UserId, ev.LearnerProfileId, ev.AchievementCode, ev.AchievementName, ev.ProgressValue, ev.Reason);

        if (ev.UserId <= 0 || ev.LearnerProfileId <= 0 || ev.AchievementId <= 0 || string.IsNullOrEmpty(ev.AchievementCode))
        {
            _logger.LogWarning("Invalid parameters in BadgeAwardedEvent: UserId={UserId}, ProfileId={ProfileId}, AchievementId={AchievementId}, Code={Code}",
                ev.UserId, ev.LearnerProfileId, ev.AchievementId, ev.AchievementCode);
            return;
        }

        try
        {
            var notifReq = new CreateNotificationRequest
            {
                UserId = ev.UserId,
                LearnerProfileId = ev.LearnerProfileId,
                Type = NotificationType.BadgeAwarded,
                Channel = NotificationChannel.InApp,
                Title = "Huy hiệu mới đã mở khóa",
                Message = $"Chúc mừng! Bạn đã nhận được huy hiệu \"{ev.AchievementName}\" vì đã đạt được: {ev.Reason}.",
                IdempotencyKey = $"badge-awarded:{ev.LearnerProfileId}:{ev.AchievementId}",
                SourceType = "BadgeAwarded",
                SourceId = ev.AchievementId.ToString()
            };

            await _notificationService.CreateNotificationAsync(notifReq, default);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create badge awarded notification for UserId {UserId}, Badge {BadgeId}", ev.UserId, ev.AchievementId);
        }
    }
}
