using System;

namespace AdaptiveLearning.Contracts.Events;

public record GoalCompletedEvent : BaseEvent
{
    public int UserId { get; init; }
    public int LearnerProfileId { get; init; }
    public int GoalId { get; init; }
    public string GoalType { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public double TargetValue { get; init; }
    public double AchievedValue { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
}
