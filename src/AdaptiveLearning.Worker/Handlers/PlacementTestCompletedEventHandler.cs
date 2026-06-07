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

public class PlacementTestCompletedEventHandler : IEventHandler<PlacementTestCompletedEvent>
{
    private readonly ISkillMatrixService _skillMatrixService;
    private readonly IRepository<LearnerProfile> _profileRepo;
    private readonly ILogger<PlacementTestCompletedEventHandler> _logger;

    public PlacementTestCompletedEventHandler(
        ISkillMatrixService skillMatrixService,
        IRepository<LearnerProfile> profileRepo,
        ILogger<PlacementTestCompletedEventHandler> logger)
    {
        _skillMatrixService = skillMatrixService;
        _profileRepo = profileRepo;
        _logger = logger;
    }

    public async Task HandleAsync(PlacementTestCompletedEvent ev)
    {
        if (ev == null)
        {
            throw new ArgumentNullException(nameof(ev));
        }

        _logger.LogInformation("PlacementTestCompletedEvent received. EventId: {EventId}, CorrelationId: {CorrelationId}, UserId: {UserId}, Score: {Score}, AssignedLevel: {AssignedLevel}", 
            ev.EventId, ev.CorrelationId, ev.UserId, ev.Score, ev.AssignedLevel);

        // Validation
        if (ev.UserId <= 0)
        {
            throw new ArgumentException("Invalid UserId in event.");
        }
        if (string.IsNullOrEmpty(ev.AssignedLevel))
        {
            throw new ArgumentException("AssignedLevel must be provided in event.");
        }

        // Apply persistence logic
        try
        {
            // Resolve or create LearnerProfile
            var profiles = await _profileRepo.FindAsync(p => p.UserId == ev.UserId);
            var profile = profiles.FirstOrDefault();
            if (profile == null)
            {
                _logger.LogInformation("Auto-creating LearnerProfile for UserId: {UserId} upon Placement Test completion.", ev.UserId);
                Enum.TryParse<EnglishLevel>(ev.AssignedLevel, true, out var levelEnum);
                profile = new LearnerProfile
                {
                    UserId = ev.UserId,
                    Level = levelEnum == EnglishLevel.None ? EnglishLevel.A1 : levelEnum,
                    ActivityStatus = ActivityStatus.Active,
                    LastActiveAt = DateTime.UtcNow
                };
                await _profileRepo.AddAsync(profile);
                await _profileRepo.SaveChangesAsync();
            }
            else
            {
                // Update profile level based on placement test results
                if (Enum.TryParse<EnglishLevel>(ev.AssignedLevel, true, out var levelEnum) && levelEnum != EnglishLevel.None)
                {
                    profile.Level = levelEnum;
                    await _profileRepo.UpdateAsync(profile);
                    await _profileRepo.SaveChangesAsync();
                }
            }

            // Map skill results
            var skillScores = ev.SkillResults.Select(sr => new SkillScoreDto
            {
                Skill = MapSkillType(sr.SkillName),
                Score = sr.Score,
                TotalQuestions = 5, // Default assumed placement questions per skill
                CorrectAnswers = (int)Math.Round(sr.Score / 20.0) // E.g. score 100% -> 5 correct
            }).ToList();

            // Handling PlacementTestId inference limitation
            int sourceId = ev.PlacementTestId;
            if (sourceId <= 0)
            {
                _logger.LogWarning("PlacementTestId is missing or zero. Using default fallback PlacementTestId: 9999.");
                sourceId = 9999;
            }

            var updateRequest = new SkillMatrixUpdateRequest
            {
                UserId = ev.UserId,
                LearnerProfileId = profile.Id,
                EventId = ev.EventId,
                SourceType = MatrixSourceType.PlacementTest,
                SourceId = sourceId,
                SkillScores = skillScores,
                WeakTopics = new List<WeakTopicDto>(), // No weak topics recorded in placement test setup
                Level = ev.AssignedLevel,
                OccurredAt = ev.CompletedAt.UtcDateTime
            };

            var persistenceResult = await _skillMatrixService.UpdateSkillMatrixAsync(updateRequest, default);

            _logger.LogInformation("PlacementTestCompletedEventHandler successfully initialized Skill Matrix. EventId: {EventId}, UserId: {UserId}, WeakestSkill: {WeakestSkill}",
                ev.EventId, ev.UserId, persistenceResult.WeakestSkill);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Persistence failed in PlacementTestCompletedEventHandler for EventId: {EventId}. Flow will be retried.", ev.EventId);
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
