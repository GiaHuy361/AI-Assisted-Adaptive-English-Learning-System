using System;
using System.Collections.Generic;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Domain.Entities;

public class AchievementBadge
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string Criteria { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;  // unique identifier
    public AchievementType AchievementType { get; set; } = AchievementType.LessonCount;
    public double Threshold { get; set; } = 1.0;       // metric threshold to earn badge
    public string? SkillTarget { get; set; }            // nullable — for skill-specific badges
    public bool IsActive { get; set; } = true;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    public ICollection<LearnerBadge> AwardedLearners { get; set; } = new List<LearnerBadge>();
}

