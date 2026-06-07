using System;

namespace AdaptiveLearning.Contracts.Events;

public record BadgeAwardedEvent : BaseEvent
{
    public int UserId { get; init; }
    public int LearnerProfileId { get; init; }
    public int AchievementId { get; init; }
    public string AchievementCode { get; init; } = string.Empty;
    public string AchievementName { get; init; } = string.Empty;
    public DateTimeOffset AwardedAt { get; init; }
    public double ProgressValue { get; init; }
    public string Reason { get; init; } = string.Empty;
}
