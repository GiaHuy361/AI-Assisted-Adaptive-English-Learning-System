using System;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Domain.Entities;

public class PlacementTestResult
{
    public int Id { get; set; }
    public int LearnerProfileId { get; set; }
    public int Score { get; set; }
    public EnglishLevel RecommendedLevel { get; set; }
    public DateTime TakenAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    public LearnerProfile LearnerProfile { get; set; } = null!;
}
