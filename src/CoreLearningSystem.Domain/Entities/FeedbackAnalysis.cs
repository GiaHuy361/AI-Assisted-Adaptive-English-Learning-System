using System;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Domain.Entities;

public class FeedbackAnalysis
{
    public int Id { get; set; }
    
    // Normalized key (e.g. "lesson:123", "quiz:45", "system:global")
    public string AggregateKey { get; set; } = string.Empty;

    public FeedbackTargetType TargetType { get; set; }
    public int? TargetId { get; set; }

    public int FeedbackCount { get; set; }
    public double AverageRating { get; set; }
    public int PositiveCount { get; set; }
    public int NeutralCount { get; set; }
    public int NegativeCount { get; set; }
    public int LowRatingCount { get; set; }

    public DateTime LastFeedbackAt { get; set; }
    public DateTime LastAnalyzedAt { get; set; } = DateTime.UtcNow;

    public FeedbackAlertStatus AlertStatus { get; set; } = FeedbackAlertStatus.Normal;
    public DateTime? AlertedAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public static string BuildAggregateKey(FeedbackTargetType targetType, int? targetId)
    {
        if (targetType == FeedbackTargetType.System)
        {
            return "system:global";
        }
        return $"{targetType.ToString().ToLowerInvariant().Trim()}:{targetId}";
    }
}

