using System;

namespace CoreLearningSystem.Domain.Entities;

public class Feedback
{
    public int Id { get; set; }
    public int LearnerProfileId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int Rating { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Admin Review Info
    public int? ReviewedByAdminId { get; set; }
    public string? ReviewComment { get; set; }
    public DateTime? ReviewedAt { get; set; }

    // Navigation Properties
    public LearnerProfile LearnerProfile { get; set; } = null!;
}
