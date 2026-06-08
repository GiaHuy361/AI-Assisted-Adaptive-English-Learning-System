using System;

namespace AdaptiveLearning.Contracts.Events;

public record FeedbackSubmittedEvent : BaseEvent
{
    public int UserId { get; init; }
    public string TargetType { get; init; } = string.Empty;
    public int? TargetId { get; init; }
    public int Rating { get; init; }
    public string Comment { get; init; } = string.Empty;
    public DateTimeOffset SubmittedAt { get; init; }
}
