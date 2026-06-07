using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdaptiveLearning.Contracts.Events;

namespace AdaptiveLearning.Worker.Handlers;

public class LessonCompletedEventHandler : IEventHandler<LessonCompletedEvent>
{
    private readonly ILogger<LessonCompletedEventHandler> _logger;

    public LessonCompletedEventHandler(ILogger<LessonCompletedEventHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(LessonCompletedEvent ev)
    {
        _logger.LogInformation("LessonCompletedEvent received and dispatched. EventId: {EventId}, CorrelationId: {CorrelationId}, UserId: {UserId}, LessonId: {LessonId}", 
            ev.EventId, ev.CorrelationId, ev.UserId, ev.LessonId);

        // Validation
        if (ev.UserId <= 0)
        {
            throw new ArgumentException("Invalid UserId in event.");
        }
        if (ev.LessonId <= 0)
        {
            throw new ArgumentException("Invalid LessonId in event.");
        }

        _logger.LogInformation("LessonCompletedEvent successfully validated and processed (Phase 2 Skeleton). EventId: {EventId}", ev.EventId);

        return Task.CompletedTask;
    }
}
