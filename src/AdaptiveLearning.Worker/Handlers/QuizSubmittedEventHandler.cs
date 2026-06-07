using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdaptiveLearning.Contracts.Events;

namespace AdaptiveLearning.Worker.Handlers;

public class QuizSubmittedEventHandler : IEventHandler<QuizSubmittedEvent>
{
    private readonly ILogger<QuizSubmittedEventHandler> _logger;

    public QuizSubmittedEventHandler(ILogger<QuizSubmittedEventHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(QuizSubmittedEvent ev)
    {
        _logger.LogInformation("QuizSubmittedEvent received and dispatched. EventId: {EventId}, CorrelationId: {CorrelationId}, UserId: {UserId}, QuizId: {QuizId}, Score: {Score}", 
            ev.EventId, ev.CorrelationId, ev.UserId, ev.QuizId, ev.Score);

        // Validation checks
        if (ev.UserId <= 0)
        {
            throw new ArgumentException("Invalid UserId in event.");
        }
        if (ev.QuizId <= 0)
        {
            throw new ArgumentException("Invalid QuizId in event.");
        }
        if (ev.QuizAttemptId <= 0)
        {
            throw new ArgumentException("Invalid QuizAttemptId in event.");
        }

        _logger.LogInformation("QuizSubmittedEvent successfully validated and processed (Phase 2 Skeleton). EventId: {EventId}", ev.EventId);

        return Task.CompletedTask;
    }
}
