using System;
using System.Collections.Generic;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Domain.Entities;

public class LearningPath
{
    public int PathId { get; set; }
    public int LearnerId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public LearningPathStatus Status { get; set; } = LearningPathStatus.NotStarted;

    // Navigation Properties
    public LearnerProfile LearnerProfile { get; set; } = null!;
    public ICollection<LearningPathItem> Items { get; set; } = new List<LearningPathItem>();
}
