using System;

namespace CoreLearningSystem.Domain.Entities;

public class LearnerProgress
{
    public int Id { get; set; }
    public int LearnerProfileId { get; set; }
    public int LessonId { get; set; }
    public bool IsCompleted { get; set; } = false;
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    // Navigation Properties
    public LearnerProfile LearnerProfile { get; set; } = null!;
    public Lesson Lesson { get; set; } = null!;
}
