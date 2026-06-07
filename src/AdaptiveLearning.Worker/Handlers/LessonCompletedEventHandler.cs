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
    private readonly ILogger<LessonCompletedEventHandler> _logger;

    public LessonCompletedEventHandler(
        ISkillMatrixService skillMatrixService,
        IRepository<LearnerProfile> profileRepo,
        ILogger<LessonCompletedEventHandler> logger)
    {
        _skillMatrixService = skillMatrixService;
        _profileRepo = profileRepo;
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
