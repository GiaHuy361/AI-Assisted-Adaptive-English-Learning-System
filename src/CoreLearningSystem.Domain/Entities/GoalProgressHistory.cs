using System;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Domain.Entities;

public class GoalProgressHistory
{
    public int Id { get; set; }
    public int GoalId { get; set; }
    public int LearnerProfileId { get; set; }
    public string SourceEventId { get; set; } = string.Empty;
    public double PreviousValue { get; set; }
    public double AddedValue { get; set; }
    public double NewValue { get; set; }
    public GoalStatus StatusBefore { get; set; }
    public GoalStatus StatusAfter { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public GoalSetting Goal { get; set; } = null!;
    public LearnerProfile LearnerProfile { get; set; } = null!;
}
