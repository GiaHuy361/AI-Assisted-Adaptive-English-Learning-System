using System;

namespace CoreLearningSystem.Domain.Entities;

public class WeeklyLearningReport
{
    public int Id { get; set; }
    public int LearnerProfileId { get; set; }
    public DateTime WeekStart { get; set; }
    public DateTime WeekEnd { get; set; }
    public int LessonsCompleted { get; set; }
    public int QuizzesCompleted { get; set; }
    public double AverageScore { get; set; }
    public string StrongestSkill { get; set; } = string.Empty;
    public string WeakestSkill { get; set; } = string.Empty;
    public string GoalProgressSummary { get; set; } = string.Empty; // JSON
    public string BadgesEarned { get; set; } = string.Empty; // JSON
    public int RecommendationsCompleted { get; set; }
    public int StreakDays { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    public int? NotificationId { get; set; }

    // Navigation Properties
    public LearnerProfile LearnerProfile { get; set; } = null!;
    public Notification? Notification { get; set; }
}
