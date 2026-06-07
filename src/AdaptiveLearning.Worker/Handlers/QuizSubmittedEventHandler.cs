using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdaptiveLearning.Contracts.Events;
using AdaptiveLearning.Worker.Services;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Application.DTOs.Common;
using Grpc.Core;

namespace AdaptiveLearning.Worker.Handlers;

public class QuizSubmittedEventHandler : IEventHandler<QuizSubmittedEvent>
{
    private readonly IRecommendationGrpcClient _grpcClient;
    private readonly ISkillMatrixService _skillMatrixService;
    private readonly IRepository<LearnerProfile> _profileRepo;
    private readonly IRecommendationService _recommendationService;
    private readonly ILogger<QuizSubmittedEventHandler> _logger;

    public QuizSubmittedEventHandler(
        IRecommendationGrpcClient grpcClient,
        ISkillMatrixService skillMatrixService,
        IRepository<LearnerProfile> profileRepo,
        IRecommendationService recommendationService,
        ILogger<QuizSubmittedEventHandler> logger)
    {
        _grpcClient = grpcClient;
        _skillMatrixService = skillMatrixService;
        _profileRepo = profileRepo;
        _recommendationService = recommendationService;
        _logger = logger;
    }

    public async Task HandleAsync(QuizSubmittedEvent ev)
    {
        if (ev == null)
        {
            throw new ArgumentNullException(nameof(ev));
        }

        _logger.LogInformation("QuizSubmittedEventHandler received event. EventId: {EventId}, CorrelationId: {CorrelationId}, UserId: {UserId}, QuizId: {QuizId}",
            ev.EventId, ev.CorrelationId, ev.UserId, ev.QuizId);

        // Basic event validation
        if (ev.UserId <= 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "UserId in event must be positive."));
        }
        if (ev.QuizId <= 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "QuizId in event must be positive."));
        }
        if (ev.QuizAttemptId <= 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "QuizAttemptId in event must be positive."));
        }
        if (ev.TotalQuestions <= 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "TotalQuestions in event must be positive."));
        }
        if (ev.CorrectAnswers < 0 || ev.CorrectAnswers > ev.TotalQuestions)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "CorrectAnswers in event must be non-negative and less than or equal to TotalQuestions."));
        }
        if (ev.AnswerDetails == null || ev.AnswerDetails.Count == 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "AnswerDetails in event cannot be empty."));
        }

        // Call the gRPC Recommendation Client first
        QuizAnalysisResultModel gRpcResult;
        try
        {
            gRpcResult = await _grpcClient.AnalyzeQuizSubmissionAsync(ev, default);

            _logger.LogInformation("QuizSubmittedEventHandler processed gRPC analysis. EventId: {EventId}, CorrelationId: {CorrelationId}, UserId: {UserId}, QuizId: {QuizId}, WeakestSkill: {WeakestSkill}, WeakTopics: {WeakTopics}, Reason: {Reason}",
                ev.EventId, ev.CorrelationId, ev.UserId, ev.QuizId, gRpcResult.WeakestSkill, string.Join(", ", gRpcResult.WeakTopics), gRpcResult.Reason);

            foreach (var score in gRpcResult.SkillScores)
            {
                _logger.LogInformation("SkillScore Detail: EventId: {EventId}, Skill: {Skill}, Score: {Score}%, Total: {Total}, Correct: {Correct}, Incorrect: {Incorrect}",
                    ev.EventId, score.Skill, score.Score, score.TotalQuestions, score.CorrectAnswers, score.IncorrectAnswers);
            }
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "gRPC analysis failed for EventId: {EventId}, CorrelationId: {CorrelationId}. Code: {StatusCode}, Detail: {Detail}",
                ev.EventId, ev.CorrelationId, ex.StatusCode, ex.Status.Detail);
            throw; // Propagate so consumer can handle retry or DLQ routing
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in gRPC call in QuizSubmittedEventHandler for EventId: {EventId}", ev.EventId);
            throw;
        }

        // Apply Persistence Logic (Skill Matrix & History updates)
        try
        {
            // Resolve LearnerProfile
            var profiles = await _profileRepo.FindAsync(p => p.UserId == ev.UserId);
            var profile = profiles.FirstOrDefault();
            if (profile == null)
            {
                _logger.LogWarning("LearnerProfile not found for UserId: {UserId}. Attempting auto-creation to recover flow.", ev.UserId);
                // Create profile if missing to prevent E2E flow break in tests
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

            // Map gRPC scores to DTOs
            var skillScores = gRpcResult.SkillScores.Select(s => new SkillScoreDto
            {
                Skill = MapSkillType(s.Skill),
                Score = s.Score,
                TotalQuestions = s.TotalQuestions,
                CorrectAnswers = s.CorrectAnswers
            }).ToList();

            // Map raw answer details to get all incorrect topics and counts
            var weakTopics = ev.AnswerDetails
                .Where(a => !a.IsCorrect)
                .GroupBy(a => new { Skill = MapSkillType(a.SkillName), Topic = (a.Topic ?? string.Empty).Trim(), Level = a.Level })
                .Where(g => !string.IsNullOrEmpty(g.Key.Topic))
                .Select(g => new WeakTopicDto
                {
                    Skill = g.Key.Skill,
                    Topic = g.Key.Topic,
                    Level = g.Key.Level,
                    IncorrectCount = g.Count()
                })
                .ToList();

            var updateRequest = new SkillMatrixUpdateRequest
            {
                UserId = ev.UserId,
                LearnerProfileId = profile.Id,
                EventId = ev.EventId,
                SourceType = MatrixSourceType.Quiz,
                SourceId = ev.QuizAttemptId,
                SkillScores = skillScores,
                WeakTopics = weakTopics,
                Level = ev.AnswerDetails.FirstOrDefault()?.Level ?? string.Empty,
                OccurredAt = ev.SubmittedAt.UtcDateTime
            };

            var persistenceResult = await _skillMatrixService.UpdateSkillMatrixAsync(updateRequest, default);

            _logger.LogInformation("QuizSubmittedEventHandler successfully persisted Skill Matrix. EventId: {EventId}, UserId: {UserId}, WeakestSkill: {WeakestSkill}, RepeatedWeakTopics: {Repeated}",
                ev.EventId, ev.UserId, persistenceResult.WeakestSkill, string.Join(", ", persistenceResult.RepeatedWeakTopics));

            // Generate recommendations after Skill Matrix update completes successfully
            SkillType? weakestSkillEnum = null;
            if (Enum.TryParse<SkillType>(persistenceResult.WeakestSkill, true, out var parsedSkill))
            {
                weakestSkillEnum = parsedSkill;
            }

            // Fetch the updated profile to make sure we have the latest level and loaded matrices
            var updatedProfile = (await _profileRepo.FindAsync(p => p.Id == profile.Id)).FirstOrDefault() ?? profile;

            var recRequest = new RecommendationRequest
            {
                UserId = ev.UserId,
                LearnerProfileId = profile.Id,
                SourceEventId = ev.EventId.ToString(),
                WeakestSkill = weakestSkillEnum,
                WeakTopics = weakTopics.Select(w => w.Topic).ToList(),
                Level = updatedProfile.Level,
                OccurredAt = ev.SubmittedAt.UtcDateTime
            };

            var recResult = await _recommendationService.GenerateRecommendationsAsync(recRequest);

            _logger.LogInformation("QuizSubmittedEventHandler successfully generated recommendations. EventId: {EventId}, UserId: {UserId}, Recommended count: {Count}",
                ev.EventId, ev.UserId, recResult.RecommendedLessons.Count);

            foreach (var recLesson in recResult.RecommendedLessons)
            {
                _logger.LogInformation("Recommended Lesson: LessonId: {LessonId}, Title: {Title}, Score: {Score}, Reason: {Reason}",
                    recLesson.LessonId, recLesson.Title, recLesson.PriorityScore, recLesson.Reason);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Persistence failed in QuizSubmittedEventHandler for EventId: {EventId}. Flow will be retried.", ev.EventId);
            throw; // Throw to trigger Kafka retry and prevent offset commit
        }
    }

    private static SkillType MapSkillType(string skill)
    {
        if (Enum.TryParse<SkillType>(skill, true, out var result))
        {
            return result;
        }
        throw new RpcException(new Status(StatusCode.InvalidArgument, $"Invalid or unsupported skill name: '{skill}'"));
    }
}
