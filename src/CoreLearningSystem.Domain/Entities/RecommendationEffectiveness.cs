using System;

namespace CoreLearningSystem.Domain.Entities;

public class RecommendationEffectiveness
{
    public int Id { get; set; }
    public int RecommendationId { get; set; }
    public int LearnerProfileId { get; set; }
    public int LessonId { get; set; }
    public string Skill { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public double ScoreBefore { get; set; }
    public double ScoreAfter { get; set; }
    public double Improvement { get; set; }
    public bool WasEffective { get; set; }
    public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;
    public int? SourceQuizAttemptId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    public LearnerProfile LearnerProfile { get; set; } = null!;
    public Recommendation Recommendation { get; set; } = null!;
    public Lesson Lesson { get; set; } = null!;
}
