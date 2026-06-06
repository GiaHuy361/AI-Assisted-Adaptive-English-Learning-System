using System;
using System.Collections.Generic;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Domain.Entities;

public class Quiz
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public double PassingScore { get; set; } // e.g. 70.0
    public double MaxScore { get; set; } = 10.0; // Maximum score for the quiz (typically 10.0)
    public EnglishLevel Level { get; set; } = EnglishLevel.A1;
    public bool IsPlacementTest { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    public ICollection<Question> Questions { get; set; } = new List<Question>();
    public ICollection<QuizAttempt> QuizAttempts { get; set; } = new List<QuizAttempt>();
}
