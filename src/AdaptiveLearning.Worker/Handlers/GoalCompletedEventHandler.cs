using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdaptiveLearning.Contracts.Events;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Application.DTOs.Common;

namespace AdaptiveLearning.Worker.Handlers;

public class GoalCompletedEventHandler : IEventHandler<GoalCompletedEvent>
{
    private readonly IAchievementService _achievementService;
    private readonly IRepository<LearnerProfile> _profileRepo;
    private readonly INotificationService _notificationService;
    private readonly ILogger<GoalCompletedEventHandler> _logger;

    public GoalCompletedEventHandler(
        IAchievementService achievementService,
        IRepository<LearnerProfile> profileRepo,
        INotificationService notificationService,
        ILogger<GoalCompletedEventHandler> logger)
    {
        _achievementService = achievementService;
        _profileRepo = profileRepo;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task HandleAsync(GoalCompletedEvent ev)
    {
        if (ev == null) throw new ArgumentNullException(nameof(ev));

        _logger.LogInformation("GoalCompletedEventHandler received event. EventId: {EventId}, UserId: {UserId}, GoalId: {GoalId}",
            ev.EventId, ev.UserId, ev.GoalId);

        if (ev.UserId <= 0 || ev.GoalId <= 0 || ev.LearnerProfileId <= 0)
        {
            _logger.LogWarning("Invalid parameters in GoalCompletedEvent: UserId={UserId}, GoalId={GoalId}, ProfileId={ProfileId}",
                ev.UserId, ev.GoalId, ev.LearnerProfileId);
            return;
        }

        try
        {
            var profile = await _profileRepo.GetByIdAsync(ev.LearnerProfileId);
            if (profile == null)
            {
                _logger.LogWarning("LearnerProfile not found for Id: {ProfileId}", ev.LearnerProfileId);
                return;
            }

            // Create notification for completed goal
            var notifReq = new CreateNotificationRequest
            {
                UserId = ev.UserId,
                LearnerProfileId = ev.LearnerProfileId,
                Type = NotificationType.GoalCompleted,
                Channel = NotificationChannel.InApp,
                Title = "Mục tiêu học tập hoàn thành",
                Message = $"Chúc mừng! Bạn đã hoàn thành mục tiêu \"{ev.Title}\" với kết quả {ev.AchievedValue} / {ev.TargetValue}.",
                IdempotencyKey = $"goal-completed:{ev.GoalId}",
                SourceType = "GoalCompleted",
                SourceId = ev.GoalId.ToString()
            };
            await _notificationService.CreateNotificationAsync(notifReq, default);

            var request = new AchievementEvaluationRequest
            {
                UserId = ev.UserId,
                LearnerProfileId = ev.LearnerProfileId,
                SourceEventId = ev.EventId.ToString(),
                Trigger = AchievementTrigger.GoalCompleted,
                OccurredAt = ev.OccurredAt.UtcDateTime
            };

            await _achievementService.EvaluateAndAwardAsync(request, default);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process GoalCompletedEvent for EventId: {EventId}", ev.EventId);
            throw;
        }
    }
}
