using System;

namespace CoreLearningSystem.Domain.Entities;

public class LearnerBadge
{
    public int Id { get; set; }
    public int LearnerProfileId { get; set; }
    public int BadgeId { get; set; }
    public DateTime UnlockedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    public LearnerProfile LearnerProfile { get; set; } = null!;
    public AchievementBadge Badge { get; set; } = null!;
}
