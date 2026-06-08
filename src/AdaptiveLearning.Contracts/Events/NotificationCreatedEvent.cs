using System;

namespace AdaptiveLearning.Contracts.Events;

public record NotificationCreatedEvent : BaseEvent
{
    public int NotificationId { get; init; }
    public int UserId { get; init; }
    public int? LearnerProfileId { get; init; }
    public string NotificationType { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? SourceType { get; init; }
    public string? SourceId { get; init; }
    public DateTime CreatedAt { get; init; }
}
