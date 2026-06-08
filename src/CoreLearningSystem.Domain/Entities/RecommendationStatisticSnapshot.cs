using System;

namespace CoreLearningSystem.Domain.Entities;

public class RecommendationStatisticSnapshot
{
    public int Id { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int? LessonId { get; set; }
    public string? Skill { get; set; }
    public string? Topic { get; set; }
    public int RecommendationCount { get; set; }
    public int CompletionCount { get; set; }
    public int EffectiveCount { get; set; }
    public double EffectivenessRate { get; set; }
    public double AverageImprovement { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    public Lesson? Lesson { get; set; }
}
