using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdaptiveLearning.Contracts.Events;

namespace AdaptiveLearning.Worker.Handlers;

public class PlacementTestCompletedEventHandler : IEventHandler<PlacementTestCompletedEvent>
{
    private readonly ILogger<PlacementTestCompletedEventHandler> _logger;

    public PlacementTestCompletedEventHandler(ILogger<PlacementTestCompletedEventHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(PlacementTestCompletedEvent ev)
    {
        _logger.LogInformation("PlacementTestCompletedEvent received and dispatched. EventId: {EventId}, CorrelationId: {CorrelationId}, UserId: {UserId}, Score: {Score}, AssignedLevel: {AssignedLevel}", 
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

        _logger.LogInformation("PlacementTestCompletedEvent successfully validated and processed (Phase 2 Skeleton). SkillResults Count: {Count}", ev.SkillResults.Count);

        return Task.CompletedTask;
    }
}
