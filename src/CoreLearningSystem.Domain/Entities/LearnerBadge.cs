using System;

namespace CoreLearningSystem.Domain.Entities;

public class LearnerBadge
{
    public int Id { get; set; }
    public int LearnerProfileId { get; set; }
    public int BadgeId { get; set; }
    public DateTime UnlockedAt { get; set; } = DateTime.UtcNow;

    public string SourceEventId { get; set; } = string.Empty;
    public double ProgressValue { get; set; }   // metric value at time of award
    public string Reason { get; set; } = string.Empty;

    // Navigation Properties
    public LearnerProfile LearnerProfile { get; set; } = null!;
    public AchievementBadge Badge { get; set; } = null!;
}

