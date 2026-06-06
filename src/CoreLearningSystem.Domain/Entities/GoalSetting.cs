using System;
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

    // Navigation Properties
    public LearnerProfile LearnerProfile { get; set; } = null!;
}
