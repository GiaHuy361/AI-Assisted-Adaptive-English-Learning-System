using System;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Domain.Entities;

public class RecommendationHistory
{
    public int Id { get; set; }
    public int RecommendationId { get; set; }
    public int LearnerProfileId { get; set; }
    public int LessonId { get; set; }
    public string SourceEventId { get; set; } = string.Empty;
    public RecommendationAction Action { get; set; }
    public RecommendationStatus? PreviousStatus { get; set; }
    public RecommendationStatus NewStatus { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    public Recommendation Recommendation { get; set; } = null!;
    public LearnerProfile LearnerProfile { get; set; } = null!;
    public Lesson Lesson { get; set; } = null!;
}
