using System;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Domain.Entities;

public class Feedback
{
    public int Id { get; set; }
    public int LearnerProfileId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int Rating { get; set; }
    public FeedbackTargetType TargetType { get; set; } = FeedbackTargetType.System;
    public int? TargetId { get; set; }
    public FeedbackStatus Status { get; set; } = FeedbackStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public string? AdminResponse { get; set; }

    // Admin Review Info (compatibility)
    public int? ReviewedByAdminId { get; set; }
    public string? ReviewComment { get; set; }
    public DateTime? ReviewedAt { get; set; }

    // Navigation Properties
    public LearnerProfile LearnerProfile { get; set; } = null!;
}

