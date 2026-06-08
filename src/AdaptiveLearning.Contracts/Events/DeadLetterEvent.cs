using System;

namespace AdaptiveLearning.Contracts.Events;

public record DeadLetterEvent : BaseEvent
{
    public string OriginalTopic { get; init; } = string.Empty;
    public int OriginalPartition { get; init; }
    public long OriginalOffset { get; init; }
    public string OriginalKey { get; init; } = string.Empty;
    public string TargetEventType { get; init; } = string.Empty;
    public Guid? TargetEventId { get; init; }
    public Guid? TargetCorrelationId { get; init; }
    public string OriginalPayload { get; init; } = string.Empty;
    public string ErrorType { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
    public DateTimeOffset FailedAt { get; init; } = DateTimeOffset.UtcNow;
    public int RetryCount { get; init; }
}
