using System;
using System.Collections.Generic;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Domain.Entities;

public class Lesson
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public SkillType Skill { get; set; } = SkillType.General;
    public string Topic { get; set; } = string.Empty;
    public EnglishLevel Level { get; set; } = EnglishLevel.A1;
    public int DurationInMinutes { get; set; }
    public LessonStatus Status { get; set; } = LessonStatus.Draft;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation Properties
    public int? QuizId { get; set; }
    public Quiz? Quiz { get; set; }
    public ICollection<LearningPathItem> LearningPathItems { get; set; } = new List<LearningPathItem>();
    public ICollection<LearnerProgress> ProgressHistory { get; set; } = new List<LearnerProgress>();
}
