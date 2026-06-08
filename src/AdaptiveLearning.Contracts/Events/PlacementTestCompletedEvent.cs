using System;
using System.Collections.Generic;

namespace AdaptiveLearning.Contracts.Events;

public record SkillScore
{
    public string SkillName { get; init; } = string.Empty;
    public double Score { get; init; }
}

public record PlacementTestCompletedEvent : BaseEvent
{
    public int UserId { get; init; }
    public int PlacementTestId { get; init; }
    public int Score { get; init; }
    public string AssignedLevel { get; init; } = string.Empty;
    public List<SkillScore> SkillResults { get; init; } = new();
    public DateTimeOffset CompletedAt { get; init; }
}
