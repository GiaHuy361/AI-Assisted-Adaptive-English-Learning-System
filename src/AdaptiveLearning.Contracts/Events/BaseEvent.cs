using System;

namespace AdaptiveLearning.Contracts.Events;

public record BaseEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public string EventType { get; init; }
    
    public BaseEvent()
    {
        EventType = GetType().Name;
    }

    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public Guid CorrelationId { get; init; } = Guid.NewGuid();
    public string Version { get; init; } = "1.0";
}
