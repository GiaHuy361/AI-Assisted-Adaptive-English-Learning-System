using System;
using System.Collections.Generic;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Domain.Entities;

public class GoalSetting
{
    public int Id { get; set; }
    public int LearnerProfileId { get; set; }
    public string Target { get; set; } = string.Empty;
    public GoalType Type { get; set; } = GoalType.General;
    public double ProgressPercentage { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime Deadline { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public double TargetValue { get; set; } = 1.0;
    public double CurrentValue { get; set; } = 0.0;
    public string Unit { get; set; } = string.Empty;
    public string? SkillTarget { get; set; }       // nullable — for SkillScore goals
    public string? TargetLevel { get; set; }       // nullable — for TargetLevel goals
    public DateTime StartDate { get; set; } = DateTime.UtcNow;
    public GoalStatus Status { get; set; } = GoalStatus.Active;
    public DateTime? CompletedAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    public LearnerProfile LearnerProfile { get; set; } = null!;
    public ICollection<GoalProgressHistory> ProgressHistories { get; set; } = new List<GoalProgressHistory>();
}

