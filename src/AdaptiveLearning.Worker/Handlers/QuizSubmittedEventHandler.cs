using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdaptiveLearning.Contracts.Events;
using AdaptiveLearning.Worker.Services;
using Grpc.Core;

namespace AdaptiveLearning.Worker.Handlers;

public class QuizSubmittedEventHandler : IEventHandler<QuizSubmittedEvent>
{
    private readonly IRecommendationGrpcClient _grpcClient;
    private readonly ILogger<QuizSubmittedEventHandler> _logger;

    public QuizSubmittedEventHandler(IRecommendationGrpcClient grpcClient, ILogger<QuizSubmittedEventHandler> logger)
    {
        _grpcClient = grpcClient;
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

        try
        {
            // Call the gRPC Recommendation Client
            var result = await _grpcClient.AnalyzeQuizSubmissionAsync(ev, default);

            // Log detailed structured logging format
            _logger.LogInformation("QuizSubmittedEventHandler processed gRPC analysis. EventId: {EventId}, CorrelationId: {CorrelationId}, UserId: {UserId}, QuizId: {QuizId}, WeakestSkill: {WeakestSkill}, WeakTopics: {WeakTopics}, Reason: {Reason}",
                ev.EventId, ev.CorrelationId, ev.UserId, ev.QuizId, result.WeakestSkill, string.Join(", ", result.WeakTopics), result.Reason);

            foreach (var score in result.SkillScores)
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
            _logger.LogError(ex, "Unexpected error in QuizSubmittedEventHandler for EventId: {EventId}", ev.EventId);
            throw;
        }
    }
}
