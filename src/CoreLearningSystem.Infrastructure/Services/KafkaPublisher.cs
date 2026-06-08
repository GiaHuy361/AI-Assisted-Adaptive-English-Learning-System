using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Infrastructure.Persistence;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using BackendEvents = CoreLearningSystem.Application.DTOs.Events;
using ContractEvents = AdaptiveLearning.Contracts.Events;
using ContractTopics = AdaptiveLearning.Contracts.Topics;

namespace CoreLearningSystem.Infrastructure.Services;

public class KafkaPublisher : IKafkaPublisher
{
    private readonly IProducer<string, string> _producer;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<KafkaPublisher> _logger;

    public KafkaPublisher(
        IProducer<string, string> producer,
        AppDbContext dbContext,
        ILogger<KafkaPublisher> logger)
    {
        _producer = producer;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task PublishQuizSubmittedAsync(BackendEvents.QuizSubmittedEvent ev)
    {
        _logger.LogInformation("Outbox: Enqueueing QuizSubmittedEvent for AttemptId: {AttemptId}", ev.AttemptId);

        try
        {
            // Query DB to enrich details
            var attempt = await _dbContext.QuizAttempts
                .Include(a => a.Details)
                    .ThenInclude(d => d.Question)
                .FirstOrDefaultAsync(a => a.Id == ev.AttemptId);

            var profile = await _dbContext.LearnerProfiles.FindAsync(ev.LearnerProfileId);
            var userId = profile?.UserId ?? ev.LearnerProfileId;

            var answerDetails = new List<ContractEvents.QuizAnswerDetail>();
            if (attempt != null)
            {
                foreach (var detail in attempt.Details)
                {
                    answerDetails.Add(new ContractEvents.QuizAnswerDetail
                    {
                        QuestionId = detail.QuestionId,
                        SkillName = detail.Question?.Skill.ToString() ?? "General",
                        Topic = detail.Question?.Topic ?? string.Empty,
                        Level = detail.Question?.Level.ToString() ?? "A1",
                        IsCorrect = detail.IsCorrect
                    });
                }
            }

            var totalQuestions = attempt != null ? (attempt.CorrectAnswersCount + attempt.IncorrectAnswersCount) : 0;
            var correctAnswers = attempt?.CorrectAnswersCount ?? 0;

            var contractEvent = new ContractEvents.QuizSubmittedEvent
            {
                UserId = userId,
                QuizId = ev.QuizId,
                QuizAttemptId = ev.AttemptId,
                Score = ev.Score,
                TotalQuestions = totalQuestions,
                CorrectAnswers = correctAnswers,
                SubmittedAt = DateTimeOffset.UtcNow,
                AnswerDetails = answerDetails
            };

            await EnqueueOutboxAsync(ContractTopics.TopicNames.QuizSubmitted, ev.AttemptId.ToString(), contractEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue QuizSubmittedEvent for AttemptId: {AttemptId}", ev.AttemptId);
            throw;
        }
    }

    public async Task PublishPlacementTestCompletedAsync(BackendEvents.PlacementTestCompletedEvent ev)
    {
        _logger.LogInformation("Outbox: Enqueueing PlacementTestCompletedEvent for ResultId: {ResultId}", ev.TestResultId);

        try
        {
            var profile = await _dbContext.LearnerProfiles.FindAsync(ev.LearnerProfileId);
            var userId = profile?.UserId ?? ev.LearnerProfileId;

            // Enrich SkillResults dynamically by scanning the Placement Test QuizAttempt
            var skillResults = new List<ContractEvents.SkillScore>();
            var placementQuiz = await _dbContext.Quizzes
                .FirstOrDefaultAsync(q => q.IsPlacementTest || q.Level == Domain.Enums.EnglishLevel.PlacementTest);

            if (placementQuiz != null)
            {
                var attempt = await _dbContext.QuizAttempts
                    .Include(a => a.Details)
                        .ThenInclude(d => d.Question)
                    .FirstOrDefaultAsync(a => a.QuizId == placementQuiz.Id && a.LearnerProfileId == ev.LearnerProfileId);

                if (attempt != null && attempt.Details != null)
                {
                    skillResults = attempt.Details
                        .Where(d => d.Question != null)
                        .GroupBy(d => d.Question.Skill)
                        .Select(g => new ContractEvents.SkillScore
                        {
                            SkillName = g.Key.ToString(),
                            Score = g.Count() > 0 ? Math.Round(((double)g.Count(x => x.IsCorrect) / g.Count()) * 100.0, 1) : 0.0
                        })
                        .ToList();
                }
            }

            var contractEvent = new ContractEvents.PlacementTestCompletedEvent
            {
                UserId = userId,
                PlacementTestId = placementQuiz?.Id ?? 0,
                Score = ev.Score,
                AssignedLevel = ev.RecommendedLevel.ToString(),
                SkillResults = skillResults,
                CompletedAt = DateTimeOffset.UtcNow
            };

            await EnqueueOutboxAsync(ContractTopics.TopicNames.PlacementTestCompleted, ev.TestResultId.ToString(), contractEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue PlacementTestCompletedEvent for ResultId: {ResultId}", ev.TestResultId);
            throw;
        }
    }

    public async Task PublishGoalCompletedAsync(BackendEvents.GoalCompletedEvent ev)
    {
        _logger.LogInformation("Outbox: Enqueueing GoalCompletedEvent for GoalId: {GoalId}", ev.GoalId);
        try
        {
            var profile = await _dbContext.LearnerProfiles.FindAsync(ev.LearnerProfileId);
            var userId = profile?.UserId ?? ev.LearnerProfileId;
            var goal = await _dbContext.GoalSettings.FindAsync(ev.GoalId);

            var contractEvent = new ContractEvents.GoalCompletedEvent
            {
                UserId = userId,
                LearnerProfileId = ev.LearnerProfileId,
                GoalId = ev.GoalId,
                GoalType = goal?.Type.ToString() ?? "General",
                Title = goal?.Target ?? ev.Target,
                TargetValue = goal?.TargetValue ?? 1.0,
                AchievedValue = goal?.CurrentValue ?? 1.0,
                CompletedAt = DateTimeOffset.UtcNow
            };

            await EnqueueOutboxAsync(ContractTopics.TopicNames.GoalCompleted, ev.GoalId.ToString(), contractEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue GoalCompletedEvent for GoalId: {GoalId}", ev.GoalId);
            throw;
        }
    }

    public async Task PublishLessonCompletedAsync(BackendEvents.LessonCompletedEvent ev)
    {
        _logger.LogInformation("Outbox: Enqueueing LessonCompletedEvent for LessonId: {LessonId}", ev.LessonId);

        try
        {
            var profile = await _dbContext.LearnerProfiles.FindAsync(ev.LearnerProfileId);
            var userId = profile?.UserId ?? ev.LearnerProfileId;

            var contractEvent = new ContractEvents.LessonCompletedEvent
            {
                UserId = userId,
                LessonId = ev.LessonId,
                SkillName = ev.SkillName,
                Topic = ev.Topic,
                Level = ev.Level,
                CompletedAt = DateTimeOffset.UtcNow
            };

            await EnqueueOutboxAsync(ContractTopics.TopicNames.LessonCompleted, ev.LessonId.ToString(), contractEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue LessonCompletedEvent for LessonId: {LessonId}", ev.LessonId);
            throw;
        }
    }

    public async Task PublishFeedbackSubmittedAsync(BackendEvents.FeedbackSubmittedEvent ev)
    {
        _logger.LogInformation("Outbox: Enqueueing FeedbackSubmittedEvent for LearnerId: {LearnerId}", ev.LearnerProfileId);

        try
        {
            var profile = await _dbContext.LearnerProfiles.FindAsync(ev.LearnerProfileId);
            var userId = profile?.UserId ?? ev.LearnerProfileId;

            var contractEvent = new ContractEvents.FeedbackSubmittedEvent
            {
                UserId = userId,
                TargetType = ev.TargetType,
                TargetId = ev.TargetId,
                Rating = ev.Rating,
                Comment = ev.Comment,
                SubmittedAt = DateTimeOffset.UtcNow
            };

            await EnqueueOutboxAsync(ContractTopics.TopicNames.FeedbackSubmitted, ev.LearnerProfileId.ToString(), contractEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue FeedbackSubmittedEvent for LearnerId: {LearnerId}", ev.LearnerProfileId);
            throw;
        }
    }

    public async Task PublishAsync(string topic, string key, object message)
    {
        if (message is ContractEvents.BaseEvent baseEvent)
        {
            await EnqueueOutboxAsync(topic, key, baseEvent);
        }
        else
        {
            throw new ArgumentException("Message must inherit from BaseEvent", nameof(message));
        }
    }

    private async Task EnqueueOutboxAsync<T>(string topic, string key, T payload) where T : ContractEvents.BaseEvent
    {
        var messageJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        var headers = new Dictionary<string, string>
        {
            { "correlation-id", payload.CorrelationId.ToString() },
            { "event-id", payload.EventId.ToString() },
            { "event-type", payload.EventType }
        };

        var outboxMessage = new OutboxMessage
        {
            EventId = payload.EventId.ToString(),
            AggregateType = payload.EventType,
            AggregateId = key,
            EventType = payload.EventType,
            Topic = topic,
            Payload = messageJson,
            HeadersJson = JsonSerializer.Serialize(headers),
            Status = OutboxStatus.Pending,
            OccurredAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        await _dbContext.OutboxMessages.AddAsync(outboxMessage);
        await _dbContext.SaveChangesAsync();
    }
}
