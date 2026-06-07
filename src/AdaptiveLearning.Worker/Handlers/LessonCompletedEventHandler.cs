using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdaptiveLearning.Contracts.Events;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Application.DTOs.Common;

namespace AdaptiveLearning.Worker.Handlers;

public class LessonCompletedEventHandler : IEventHandler<LessonCompletedEvent>
{
    private readonly ISkillMatrixService _skillMatrixService;
    private readonly IRepository<LearnerProfile> _profileRepo;
    private readonly IRecommendationService _recommendationService;
    private readonly IGoalTrackingService _goalTrackingService;
    private readonly IAchievementService _achievementService;
    private readonly IKafkaPublisher _kafkaPublisher;
    private readonly ILogger<LessonCompletedEventHandler> _logger;

    public LessonCompletedEventHandler(
        ISkillMatrixService skillMatrixService,
        IRepository<LearnerProfile> profileRepo,
        IRecommendationService recommendationService,
        IGoalTrackingService goalTrackingService,
        IAchievementService achievementService,
        IKafkaPublisher kafkaPublisher,
        ILogger<LessonCompletedEventHandler> logger)
    {
        _skillMatrixService = skillMatrixService;
        _profileRepo = profileRepo;
        _recommendationService = recommendationService;
        _goalTrackingService = goalTrackingService;
        _achievementService = achievementService;
        _kafkaPublisher = kafkaPublisher;
        _logger = logger;
    }

    public async Task HandleAsync(LessonCompletedEvent ev)
    {
        if (ev == null)
        {
            throw new ArgumentNullException(nameof(ev));
        }

        _logger.LogInformation("LessonCompletedEvent received. EventId: {EventId}, CorrelationId: {CorrelationId}, UserId: {UserId}, LessonId: {LessonId}, Skill: {SkillName}, Topic: {Topic}", 
            ev.EventId, ev.CorrelationId, ev.UserId, ev.LessonId, ev.SkillName, ev.Topic);

        // Validation
        if (ev.UserId <= 0)
        {
            throw new ArgumentException("Invalid UserId in event.");
        }
        if (ev.LessonId <= 0)
        {
            throw new ArgumentException("Invalid LessonId in event.");
        }

        // Apply persistence logic
        try
        {
            // Resolve or create LearnerProfile
            var profiles = await _profileRepo.FindAsync(p => p.UserId == ev.UserId);
            var profile = profiles.FirstOrDefault();
            if (profile == null)
            {
                _logger.LogInformation("Auto-creating LearnerProfile for UserId: {UserId} upon Lesson completion.", ev.UserId);
                profile = new LearnerProfile
                {
                    UserId = ev.UserId,
                    Level = EnglishLevel.A1,
                    ActivityStatus = ActivityStatus.Active,
                    LastActiveAt = DateTime.UtcNow
                };
                await _profileRepo.AddAsync(profile);
                await _profileRepo.SaveChangesAsync();
            }

            var completedTopic = new WeakTopicDto
            {
                Skill = MapSkillType(ev.SkillName),
                Topic = ev.Topic,
                Level = ev.Level,
                IncorrectCount = 0 // 0 for lesson completions
            };

            var updateRequest = new SkillMatrixUpdateRequest
            {
                UserId = ev.UserId,
                LearnerProfileId = profile.Id,
                EventId = ev.EventId,
                SourceType = MatrixSourceType.LessonCompletion,
                SourceId = ev.LessonId,
                SkillScores = new List<SkillScoreDto>(),
                WeakTopics = new List<WeakTopicDto> { completedTopic },
                Level = ev.Level,
                OccurredAt = ev.CompletedAt.UtcDateTime
            };

            var persistenceResult = await _skillMatrixService.UpdateSkillMatrixAsync(updateRequest, default);

            _logger.LogInformation("LessonCompletedEventHandler successfully processed lesson completion. EventId: {EventId}, UserId: {UserId}, WeakestSkill: {WeakestSkill}",
                ev.EventId, ev.UserId, persistenceResult.WeakestSkill);

            // Update recommendation status for this lesson to Completed
            await _recommendationService.HandleLessonCompletedAsync(
                profile.Id,
                ev.LessonId,
                ev.EventId.ToString()
            );

            _logger.LogInformation("LessonCompletedEventHandler updated recommendation status. EventId: {EventId}, UserId: {UserId}, LessonId: {LessonId}",
                ev.EventId, ev.UserId, ev.LessonId);

            // 1. Update Lesson Goal
            var lessonGoalRequest = new GoalProgressRequest
            {
                UserId = ev.UserId,
                LearnerProfileId = profile.Id,
                SourceEventId = $"{ev.EventId}_lesson",
                TriggerGoalType = GoalType.LessonsPerWeek,
                IncrementValue = 1.0,
                OccurredAt = ev.CompletedAt.UtcDateTime
            };
            var lessonGoalResult = await _goalTrackingService.UpdateGoalProgressAsync(lessonGoalRequest, default);

            // Publish completed goals
            if (lessonGoalResult.CompletedGoals != null)
            {
                foreach (var completedGoal in lessonGoalResult.CompletedGoals)
                {
                    var goalCompletedEvent = new GoalCompletedEvent
                    {
                        UserId = ev.UserId,
                        LearnerProfileId = profile.Id,
                        GoalId = completedGoal.GoalId,
                        GoalType = completedGoal.GoalType,
                        Title = completedGoal.Title,
                        TargetValue = completedGoal.TargetValue,
                        AchievedValue = completedGoal.AchievedValue,
                        CompletedAt = completedGoal.CompletedAt
                    };
                    await _kafkaPublisher.PublishAsync(AdaptiveLearning.Contracts.Topics.TopicNames.GoalCompleted, completedGoal.GoalId.ToString(), goalCompletedEvent);
                }
            }

            // 2. Evaluate and Award Achievements
            var achievementRequest = new AchievementEvaluationRequest
            {
                UserId = ev.UserId,
                LearnerProfileId = profile.Id,
                SourceEventId = ev.EventId.ToString(),
                Trigger = AchievementTrigger.LessonCompleted,
                OccurredAt = ev.CompletedAt.UtcDateTime
            };
            await _achievementService.EvaluateAndAwardAsync(achievementRequest, default);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Persistence failed in LessonCompletedEventHandler for EventId: {EventId}. Flow will be retried.", ev.EventId);
            throw; // Throw to trigger Kafka consumer retry
        }
    }

    private static SkillType MapSkillType(string skill)
    {
        if (Enum.TryParse<SkillType>(skill, true, out var result))
        {
            return result;
        }
        throw new ArgumentException($"Invalid or unsupported skill name: '{skill}'");
    }
}
