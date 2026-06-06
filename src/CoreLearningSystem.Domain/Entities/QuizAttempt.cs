using System;
using System.Collections.Generic;

namespace CoreLearningSystem.Domain.Entities;

public class QuizAttempt
{
    public int Id { get; set; }
    public int QuizId { get; set; }
    public int LearnerProfileId { get; set; }
    public double Score { get; set; }
    public int CorrectAnswersCount { get; set; }
    public int IncorrectAnswersCount { get; set; }
    public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;
    public bool IsPassed { get; set; }

    // Navigation Properties
    public Quiz Quiz { get; set; } = null!;
    public LearnerProfile LearnerProfile { get; set; } = null!;
    public ICollection<QuizAttemptDetail> Details { get; set; } = new List<QuizAttemptDetail>();
}
