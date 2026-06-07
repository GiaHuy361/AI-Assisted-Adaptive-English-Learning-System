using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AdaptiveLearning.Contracts.Events;
using AdaptiveLearning.GrpcService;
using AdaptiveLearning.Worker.Options;

namespace AdaptiveLearning.Worker.Services;

public class RecommendationGrpcClient : IRecommendationGrpcClient
{
    private readonly RecommendationService.RecommendationServiceClient _client;
    private readonly RecommendationGrpcOptions _options;
    private readonly ILogger<RecommendationGrpcClient> _logger;

    public RecommendationGrpcClient(
        RecommendationService.RecommendationServiceClient client,
        IOptions<RecommendationGrpcOptions> options,
        ILogger<RecommendationGrpcClient> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<QuizAnalysisResultModel> AnalyzeQuizSubmissionAsync(QuizSubmittedEvent ev, CancellationToken cancellationToken)
    {
        if (ev == null)
        {
            throw new ArgumentNullException(nameof(ev));
        }

        // Map request
        var request = new AnalyzeQuizSubmissionRequest
        {
            EventId = ev.EventId.ToString(),
            CorrelationId = ev.CorrelationId.ToString(),
            UserId = ev.UserId,
            QuizId = ev.QuizId,
            QuizAttemptId = ev.QuizAttemptId,
            Score = ev.Score,
            TotalQuestions = ev.TotalQuestions,
            CorrectAnswers = ev.CorrectAnswers,
            SubmittedAt = ev.SubmittedAt.ToString("o")
        };

        foreach (var ans in ev.AnswerDetails)
        {
            request.Answers.Add(new AnswerDetail
            {
                QuestionId = ans.QuestionId,
                Skill = ans.SkillName,
                Topic = ans.Topic ?? string.Empty,
                Level = ans.Level ?? string.Empty,
                IsCorrect = ans.IsCorrect
            });
        }

        int attempt = 0;
        int maxAttempts = 1 + Math.Max(0, _options.MaxRetryAttempts);

        while (true)
        {
            attempt++;
            
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));

            try
            {
                var deadline = DateTime.UtcNow.AddSeconds(_options.RequestTimeoutSeconds);
                var callOptions = new CallOptions(deadline: deadline, cancellationToken: cts.Token);

                _logger.LogInformation("Sending gRPC AnalyzeQuizSubmission request to service at {ServiceUrl}. Attempt {Attempt}/{MaxAttempts}. EventId: {EventId}, CorrelationId: {CorrelationId}",
                    _options.ServiceUrl, attempt, maxAttempts, ev.EventId, ev.CorrelationId);

                var stopwatch = Stopwatch.StartNew();
                var response = await _client.AnalyzeQuizSubmissionAsync(request, callOptions);
                stopwatch.Stop();

                _logger.LogInformation("gRPC AnalyzeQuizSubmission responded successfully in {DurationMs}ms. EventId: {EventId}",
                    stopwatch.ElapsedMilliseconds, ev.EventId);

                return new QuizAnalysisResultModel
                {
                    Success = response.Success,
                    AnalysisId = response.AnalysisId,
                    UserId = response.UserId,
                    WeakestSkill = response.WeakestSkill,
                    WeakTopics = response.WeakTopics.ToList(),
                    SkillScores = response.SkillScores.Select(s => new SkillScoreModel
                    {
                        Skill = s.Skill,
                        Score = s.Score,
                        TotalQuestions = s.TotalQuestions,
                        CorrectAnswers = s.CorrectAnswers,
                        IncorrectAnswers = s.IncorrectAnswers
                    }).ToList(),
                    Reason = response.Reason,
                    ProcessedAt = response.ProcessedAt,
                    Message = response.Message
                };
            }
            catch (RpcException ex) when (IsTransient(ex.StatusCode) && attempt < maxAttempts)
            {
                _logger.LogWarning(ex, "Transient gRPC error ({StatusCode}) on attempt {Attempt}/{MaxAttempts} for EventId: {EventId}. Retrying...",
                    ex.StatusCode, attempt, maxAttempts, ev.EventId);

                try
                {
                    await Task.Delay(_options.RetryDelayMilliseconds, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw new RpcException(new Status(StatusCode.Cancelled, "gRPC retry delayed call was cancelled."));
                }
            }
            catch (RpcException ex)
            {
                _logger.LogError(ex, "gRPC call failed with StatusCode: {StatusCode}, Detail: {Detail}. Attempt: {Attempt}/{MaxAttempts}. EventId: {EventId}",
                    ex.StatusCode, ex.Status.Detail, attempt, maxAttempts, ev.EventId);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in gRPC client call. Attempt: {Attempt}/{MaxAttempts}. EventId: {EventId}",
                    attempt, maxAttempts, ev.EventId);
                throw new RpcException(new Status(StatusCode.Internal, ex.Message));
            }
        }
    }

    private static bool IsTransient(StatusCode statusCode)
    {
        return statusCode == StatusCode.Unavailable ||
               statusCode == StatusCode.DeadlineExceeded ||
               statusCode == StatusCode.ResourceExhausted;
    }
}
