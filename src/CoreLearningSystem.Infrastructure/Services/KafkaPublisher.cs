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
        _logger.LogInformation("Publishing QuizSubmittedEvent for AttemptId: {AttemptId}", ev.AttemptId);

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

            await SendMessageAsync(ContractTopics.TopicNames.QuizSubmitted, ev.AttemptId.ToString(), contractEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish QuizSubmittedEvent for AttemptId: {AttemptId}", ev.AttemptId);
            throw;
        }
    }

    public async Task PublishPlacementTestCompletedAsync(BackendEvents.PlacementTestCompletedEvent ev)
    {
        _logger.LogInformation("Publishing PlacementTestCompletedEvent for ResultId: {ResultId}", ev.TestResultId);

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
                PlacementTestId = placementQuiz?.Id ?? 0, // Blocked/Partial if quiz not found
                Score = ev.Score,
                AssignedLevel = ev.RecommendedLevel.ToString(),
                SkillResults = skillResults,
                CompletedAt = DateTimeOffset.UtcNow
            };

            await SendMessageAsync(ContractTopics.TopicNames.PlacementTestCompleted, ev.TestResultId.ToString(), contractEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish PlacementTestCompletedEvent for ResultId: {ResultId}", ev.TestResultId);
            throw;
        }
    }

    public Task PublishGoalCompletedAsync(BackendEvents.GoalCompletedEvent ev)
    {
        _logger.LogWarning("PublishGoalCompletedEvent is not actively mapped in Phase 2 scope.");
        return Task.CompletedTask;
    }

    public async Task PublishLessonCompletedAsync(BackendEvents.LessonCompletedEvent ev)
    {
        _logger.LogInformation("Publishing LessonCompletedEvent for LessonId: {LessonId}", ev.LessonId);

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

            await SendMessageAsync(ContractTopics.TopicNames.LessonCompleted, ev.LessonId.ToString(), contractEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish LessonCompletedEvent for LessonId: {LessonId}", ev.LessonId);
            throw;
        }
    }

    public async Task PublishFeedbackSubmittedAsync(BackendEvents.FeedbackSubmittedEvent ev)
    {
        _logger.LogInformation("Publishing FeedbackSubmittedEvent for LearnerId: {LearnerId}", ev.LearnerProfileId);

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

            await SendMessageAsync(ContractTopics.TopicNames.FeedbackSubmitted, ev.LearnerProfileId.ToString(), contractEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish FeedbackSubmittedEvent for LearnerId: {LearnerId}", ev.LearnerProfileId);
            throw;
        }
    }

    private async Task SendMessageAsync<T>(string topic, string key, T payload) where T : ContractEvents.BaseEvent
    {
        var messageJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        var message = new Message<string, string>
        {
            Key = key,
            Value = messageJson
        };

        // Attach correlation metadata to headers
        message.Headers = new Headers
        {
            { "correlation-id", payload.CorrelationId.ToByteArray() },
            { "event-id", payload.EventId.ToByteArray() },
            { "event-type", System.Text.Encoding.UTF8.GetBytes(payload.EventType) }
        };

        _logger.LogInformation("Sending message of type {EventType} to topic {Topic} with CorrelationId {CorrelationId}...", payload.EventType, topic, payload.CorrelationId);

        var deliveryResult = await _producer.ProduceAsync(topic, message);
        
        _logger.LogInformation("Message successfully delivered to topic: {Topic}, partition: {Partition}, offset: {Offset}", 
            deliveryResult.Topic, deliveryResult.Partition, deliveryResult.Offset);
    }
}
