using System;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Domain.Entities;

public class Recommendation
{
    public int Id { get; set; }
    public int LearnerProfileId { get; set; }
    public int LessonId { get; set; }
    public SkillType Skill { get; set; }
    public string Topic { get; set; } = string.Empty;
    public EnglishLevel Level { get; set; }
    public double PriorityScore { get; set; }
    public string Reason { get; set; } = string.Empty;
    public RecommendationStatus Status { get; set; }
    public string SourceEventId { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? DismissedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation Properties
    public LearnerProfile LearnerProfile { get; set; } = null!;
    public Lesson Lesson { get; set; } = null!;
}
