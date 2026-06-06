using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Domain.Entities;

public class LearningPathItem
{
    public int Id { get; set; }
    public int LearningPathId { get; set; }
    public int LessonId { get; set; }
    public int SequenceOrder { get; set; }
    public LessonStatus Status { get; set; } = LessonStatus.Draft;

    // Navigation Properties
    public LearningPath LearningPath { get; set; } = null!;
    public Lesson Lesson { get; set; } = null!;
}
