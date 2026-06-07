using System;
using System.Collections.Generic;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Application.DTOs.Common;

public class AchievementEvaluationRequest
{
    public int UserId { get; set; }
    public int LearnerProfileId { get; set; }
    public string SourceEventId { get; set; } = string.Empty;
    public AchievementTrigger Trigger { get; set; }
    public int CompletedLessonCount { get; set; }
    public int HighScoreQuizCount { get; set; }
    public int CurrentStreakDays { get; set; }
    public int CompletedGoalCount { get; set; }
    public double SkillImprovementPoints { get; set; }
    public bool IsFirstLesson { get; set; }
    public bool IsFirstQuiz { get; set; }
    public bool IsFirstPlacementTest { get; set; }
    public DateTime OccurredAt { get; set; }
}

public enum AchievementTrigger
{
    LessonCompleted,
    QuizSubmitted,
    GoalCompleted,
    PlacementTestCompleted
}

public class EligibleAchievement
{
    public int AchievementId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AchievementType AchievementType { get; set; }
    public double MetricValue { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class AchievementAwardResult
{
    public int LearnerProfileId { get; set; }
    public List<AwardedBadgeDto> AwardedBadges { get; set; } = new();
    public int SkippedDuplicates { get; set; }
}

public class AwardedBadgeDto
{
    public int AchievementId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double MetricValue { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime AwardedAt { get; set; }
}
