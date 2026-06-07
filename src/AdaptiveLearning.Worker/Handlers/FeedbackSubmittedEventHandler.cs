using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdaptiveLearning.Contracts.Events;

namespace AdaptiveLearning.Worker.Handlers;

public class FeedbackSubmittedEventHandler : IEventHandler<FeedbackSubmittedEvent>
{
    private readonly ILogger<FeedbackSubmittedEventHandler> _logger;

    public FeedbackSubmittedEventHandler(ILogger<FeedbackSubmittedEventHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(FeedbackSubmittedEvent ev)
    {
        _logger.LogInformation("FeedbackSubmittedEvent received and dispatched. EventId: {EventId}, CorrelationId: {CorrelationId}, UserId: {UserId}, Rating: {Rating}", 
            ev.EventId, ev.CorrelationId, ev.UserId, ev.Rating);

        // Validation
        if (ev.UserId <= 0)
        {
            throw new ArgumentException("Invalid UserId in event.");
        }
        if (ev.Rating < 1 || ev.Rating > 5)
        {
            throw new ArgumentException("Invalid Rating in event. Rating must be 1 to 5.");
        }

        // Note: TargetType/TargetId might be empty due to blocked API data
        _logger.LogInformation("FeedbackSubmittedEvent successfully validated and processed (Phase 2 Skeleton). TargetType: {TargetType}, TargetId: {TargetId}", 
            ev.TargetType, ev.TargetId);

        return Task.CompletedTask;
    }
}
