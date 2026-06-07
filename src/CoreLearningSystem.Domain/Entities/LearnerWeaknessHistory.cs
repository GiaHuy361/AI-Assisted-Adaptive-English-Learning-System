using System;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Domain.Entities;

public class LearnerWeaknessHistory
{
    public int Id { get; set; }
    public int LearnerProfileId { get; set; }
    public SkillType Skill { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public int IncorrectCount { get; set; }
    public int OccurrenceCount { get; set; }
    public DateTime LastOccurredAt { get; set; } = DateTime.UtcNow;
    public DateTime FirstOccurredAt { get; set; } = DateTime.UtcNow;
    public int SourceQuizAttemptId { get; set; }
    public Guid LastEventId { get; set; }
    public WeaknessStatus Status { get; set; } = WeaknessStatus.Active;

    // Navigation Properties
    public LearnerProfile LearnerProfile { get; set; } = null!;
}
