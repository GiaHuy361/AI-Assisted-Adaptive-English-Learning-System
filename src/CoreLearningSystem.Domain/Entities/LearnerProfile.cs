using System;
using System.Collections.Generic;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Domain.Entities;

public class LearnerProfile
{
    public int Id { get; set; } // Can share PK with User
    public int UserId { get; set; }
    public EnglishLevel Level { get; set; } = EnglishLevel.None;
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public EnglishLevel CurrentLevel { get => Level; set => Level = value; }
    public ActivityStatus ActivityStatus { get; set; } = ActivityStatus.Active;
    public DateTime LastActiveAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    public User User { get; set; } = null!;
    public LearningPath? LearningPath { get; set; }
    public ICollection<PlacementTestResult> PlacementTestResults { get; set; } = new List<PlacementTestResult>();
    public ICollection<QuizAttempt> QuizAttempts { get; set; } = new List<QuizAttempt>();
    public ICollection<LearnerProgress> ProgressHistory { get; set; } = new List<LearnerProgress>();
    public ICollection<GoalSetting> Goals { get; set; } = new List<GoalSetting>();
    public ICollection<LearnerBadge> UnlockedBadges { get; set; } = new List<LearnerBadge>();
    public ICollection<Feedback> SubmittedFeedbacks { get; set; } = new List<Feedback>();
    public ICollection<SkillMatrix> SkillMatrices { get; set; } = new List<SkillMatrix>();
    public ICollection<SkillMatrixHistory> SkillMatrixHistories { get; set; } = new List<SkillMatrixHistory>();
    public ICollection<LearnerWeaknessHistory> WeaknessHistories { get; set; } = new List<LearnerWeaknessHistory>();
    public ICollection<Recommendation> Recommendations { get; set; } = new List<Recommendation>();
    public ICollection<RecommendationHistory> RecommendationHistories { get; set; } = new List<RecommendationHistory>();
    public ICollection<GoalProgressHistory> GoalProgressHistories { get; set; } = new List<GoalProgressHistory>();
    public ICollection<WeeklyLearningReport> WeeklyLearningReports { get; set; } = new List<WeeklyLearningReport>();
}
