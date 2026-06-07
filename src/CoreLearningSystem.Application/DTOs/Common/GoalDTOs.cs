using System;
using System.Collections.Generic;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Application.DTOs.Common;

public class GoalProgressRequest
{
    public int UserId { get; set; }
    public int LearnerProfileId { get; set; }
    public string SourceEventId { get; set; } = string.Empty;
    public GoalType TriggerGoalType { get; set; }  // which type of goal this event affects
    public double IncrementValue { get; set; } = 1.0;
    public string? SkillName { get; set; }         // for SkillScore goals
    public double? NewSkillScore { get; set; }     // for SkillScore goals
    public DateTime OccurredAt { get; set; }
}

public class GoalProgressResult
{
    public int LearnerProfileId { get; set; }
    public List<CompletedGoalDto> CompletedGoals { get; set; } = new();
    public List<GoalAdvisoryDto> Advisories { get; set; } = new();
    public int GoalsUpdated { get; set; }
}

public class CompletedGoalDto
{
    public int GoalId { get; set; }
    public string GoalType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public double TargetValue { get; set; }
    public double AchievedValue { get; set; }
    public DateTime CompletedAt { get; set; }
}

public class GoalAdvisoryDto
{
    public int GoalId { get; set; }
    public string Title { get; set; } = string.Empty;
    public GoalAdvisory Advisory { get; set; }
    public string Reason { get; set; } = string.Empty;
    public double ProgressPercentage { get; set; }
    public double TimeElapsedPercentage { get; set; }
}
