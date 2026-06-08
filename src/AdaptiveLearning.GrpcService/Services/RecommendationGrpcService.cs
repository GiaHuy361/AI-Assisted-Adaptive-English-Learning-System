using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Domain.Enums;

namespace AdaptiveLearning.GrpcService.Services;

public class RecommendationGrpcService : RecommendationService.RecommendationServiceBase
{
    private readonly IQuizWeaknessAnalyzer _analyzer;
    private readonly IRecommendationService _recommendationService;
    private readonly ILogger<RecommendationGrpcService> _logger;

    public RecommendationGrpcService(
        IQuizWeaknessAnalyzer analyzer, 
        IRecommendationService recommendationService,
        ILogger<RecommendationGrpcService> logger)
    {
        _analyzer = analyzer;
        _recommendationService = recommendationService;
        _logger = logger;
    }

    public override async Task<AnalyzeQuizSubmissionResponse> AnalyzeQuizSubmission(AnalyzeQuizSubmissionRequest request, ServerCallContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        
        _logger.LogInformation("gRPC AnalyzeQuizSubmission request received. EventId: {EventId}, CorrelationId: {CorrelationId}, UserId: {UserId}, QuizId: {QuizId}, QuizAttemptId: {QuizAttemptId}",
            request.EventId, request.CorrelationId, request.UserId, request.QuizId, request.QuizAttemptId);

        try
        {
            // --- VALIDATION RULES (InvalidArgument) ---
            if (string.IsNullOrWhiteSpace(request.EventId) || !Guid.TryParse(request.EventId, out _))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "EventId must be a valid, non-empty GUID string."));
            }

            if (string.IsNullOrWhiteSpace(request.CorrelationId) || !Guid.TryParse(request.CorrelationId, out _))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "CorrelationId must be a valid, non-empty GUID string."));
            }

            if (request.UserId <= 0)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "UserId must be positive."));
            }

            if (request.QuizId <= 0)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "QuizId must be positive."));
            }

            if (request.QuizAttemptId <= 0)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "QuizAttemptId must be positive."));
            }

            if (request.TotalQuestions <= 0)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "TotalQuestions must be positive."));
            }

            if (request.CorrectAnswers < 0)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "CorrectAnswers cannot be negative."));
            }

            if (request.CorrectAnswers > request.TotalQuestions)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "CorrectAnswers cannot exceed TotalQuestions."));
            }

            if (request.Answers == null || request.Answers.Count == 0)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Answers list cannot be empty."));
            }

            // Consistency checks
            if (request.Answers.Count != request.TotalQuestions)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, $"Answer count ({request.Answers.Count}) does not match TotalQuestions ({request.TotalQuestions})."));
            }

            var correctCountInAnswers = request.Answers.Count(a => a.IsCorrect);
            if (correctCountInAnswers != request.CorrectAnswers)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, $"Correct answer count in list ({correctCountInAnswers}) does not match CorrectAnswers ({request.CorrectAnswers})."));
            }

            // Answer details validation
            foreach (var ans in request.Answers)
            {
                if (ans == null)
                {
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "Answer detail entry cannot be null."));
                }
                if (ans.QuestionId <= 0)
                {
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "QuestionId in answer detail must be positive."));
                }
                if (string.IsNullOrWhiteSpace(ans.Skill))
                {
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "Skill name in answer detail cannot be empty."));
                }
            }

            // Support CancellationToken from client call
            context.CancellationToken.ThrowIfCancellationRequested();

            // Map Protobuf to Internal Analyzer Model
            var input = new QuizAnalysisInput
            {
                UserId = request.UserId,
                QuizId = request.QuizId,
                QuizAttemptId = request.QuizAttemptId,
                Score = request.Score,
                TotalQuestions = request.TotalQuestions,
                CorrectAnswers = request.CorrectAnswers,
                Answers = request.Answers.Select(a => new AnswerAnalysisDetail
                {
                    QuestionId = a.QuestionId,
                    Skill = a.Skill,
                    Topic = a.Topic ?? string.Empty,
                    Level = a.Level ?? string.Empty,
                    IsCorrect = a.IsCorrect
                }).ToList()
            };

            // Call transport-independent analyzer
            var result = await _analyzer.AnalyzeAsync(input);

            // Map result back to protobuf response
            var response = new AnalyzeQuizSubmissionResponse
            {
                Success = true,
                AnalysisId = result.AnalysisId,
                UserId = result.UserId,
                WeakestSkill = result.WeakestSkill,
                Reason = result.Reason,
                ProcessedAt = result.ProcessedAt,
                Message = "Quiz submission weakness analysis completed successfully."
            };

            response.WeakTopics.AddRange(result.WeakTopics);
            response.SkillScores.AddRange(result.SkillScores.Select(s => new SkillScore
            {
                Skill = s.Skill,
                Score = s.Score,
                TotalQuestions = s.TotalQuestions,
                CorrectAnswers = s.CorrectAnswers,
                IncorrectAnswers = s.IncorrectAnswers
            }));

            stopwatch.Stop();
            _logger.LogInformation("gRPC AnalyzeQuizSubmission completed successfully. EventId: {EventId}, CorrelationId: {CorrelationId}, UserId: {UserId}, WeakestSkill: {WeakestSkill}, Duration: {DurationMs}ms",
                request.EventId, request.CorrelationId, request.UserId, result.WeakestSkill, stopwatch.ElapsedMilliseconds);

            return response;
        }
        catch (RpcException ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "gRPC validation failed for EventId: {EventId}, CorrelationId: {CorrelationId}. Error: {ErrorReason}",
                request.EventId, request.CorrelationId, ex.Status.Detail);
            throw;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogWarning("gRPC request cancelled by client. EventId: {EventId}, CorrelationId: {CorrelationId}",
                request.EventId, request.CorrelationId);
            throw new RpcException(new Status(StatusCode.Cancelled, "Request was cancelled."));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Unexpected error occurred during AnalyzeQuizSubmission. EventId: {EventId}, CorrelationId: {CorrelationId}",
                request.EventId, request.CorrelationId);
            throw new RpcException(new Status(StatusCode.Internal, "An unexpected internal error occurred on the server."));
        }
    }

    public override Task<StatusResponse> GetServiceStatus(StatusRequest request, ServerCallContext context)
    {
        _logger.LogInformation("gRPC GetServiceStatus request received.");

        return Task.FromResult(new StatusResponse
        {
            ServiceName = "AdaptiveLearning.GrpcService",
            Status = "HEALTHY",
            Version = "1.0.0",
            ServerTime = DateTime.UtcNow.ToString("o")
        });
    }

    public override async Task<GenerateRecommendationsResponse> GenerateRecommendations(GenerateRecommendationsRequest request, ServerCallContext context)
    {
        _logger.LogInformation("gRPC GenerateRecommendations request received. EventId: {EventId}, UserId: {UserId}",
            request.EventId, request.UserId);

        try
        {
            if (string.IsNullOrWhiteSpace(request.EventId) || !Guid.TryParse(request.EventId, out _))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "EventId must be a valid, non-empty GUID string."));
            }
            if (request.UserId <= 0)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "UserId must be positive."));
            }
            if (request.LearnerProfileId <= 0)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "LearnerProfileId must be positive."));
            }

            SkillType? weakestSkill = null;
            if (!string.IsNullOrWhiteSpace(request.WeakestSkill) && Enum.TryParse<SkillType>(request.WeakestSkill, true, out var skillVal))
            {
                weakestSkill = skillVal;
            }

            EnglishLevel currentLevel = EnglishLevel.None;
            if (!string.IsNullOrWhiteSpace(request.CurrentLevel) && Enum.TryParse<EnglishLevel>(request.CurrentLevel, true, out var lvlVal))
            {
                currentLevel = lvlVal;
            }

            var internalRequest = new CoreLearningSystem.Application.DTOs.Common.RecommendationRequest
            {
                UserId = request.UserId,
                LearnerProfileId = request.LearnerProfileId,
                SourceEventId = request.EventId,
                WeakestSkill = weakestSkill,
                WeakTopics = request.WeakTopics.ToList(),
                Level = currentLevel,
                OccurredAt = DateTime.UtcNow
            };

            var result = await _recommendationService.GenerateRecommendationsAsync(internalRequest);

            var response = new GenerateRecommendationsResponse
            {
                Success = true,
                GeneratedAt = DateTime.UtcNow.ToString("o"),
                OverallReason = result.OverallReason,
                Message = "Recommendations generated successfully via gRPC."
            };

            var lessons = result.RecommendedLessons;
            if (request.MaxRecommendations > 0)
            {
                lessons = lessons.Take(request.MaxRecommendations).ToList();
            }

            response.Recommendations.AddRange(lessons.Select(l => new RecommendedLesson
            {
                LessonId = l.LessonId,
                Title = l.Title,
                PriorityScore = l.PriorityScore,
                Reason = l.Reason,
                Skill = weakestSkill?.ToString() ?? string.Empty,
                Topic = request.WeakTopics.FirstOrDefault() ?? string.Empty,
                Level = currentLevel.ToString()
            }));

            return response;
        }
        catch (RpcException)
        {
            throw;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error occurred during GenerateRecommendations. EventId: {EventId}", request.EventId);
            throw new RpcException(new Status(StatusCode.Internal, "An unexpected internal error occurred on the server."));
        }
    }
}
