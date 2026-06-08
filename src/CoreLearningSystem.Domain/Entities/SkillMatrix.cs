using System;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Domain.Entities;

public class SkillMatrix
{
    public int Id { get; set; }
    public int LearnerProfileId { get; set; }
    public SkillType Skill { get; set; }
    public double CurrentScore { get; set; }
    public MasteryLevel MasteryLevel { get; set; }
    public int TotalAssessments { get; set; }
    public double LastAssessmentScore { get; set; }
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    public LearnerProfile LearnerProfile { get; set; } = null!;
}
