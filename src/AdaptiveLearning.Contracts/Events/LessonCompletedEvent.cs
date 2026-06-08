using System;

namespace AdaptiveLearning.Contracts.Events;

public record LessonCompletedEvent : BaseEvent
{
    public int UserId { get; init; }
    public int LessonId { get; init; }
    public string SkillName { get; init; } = string.Empty;
    public string Topic { get; init; } = string.Empty;
    public string Level { get; init; } = string.Empty;
    public DateTimeOffset CompletedAt { get; init; }
}
