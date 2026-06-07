using System;
using System.Collections.Generic;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Application.DTOs.Common;

public class RecommendationRequest
{
    public int UserId { get; set; }
    public int LearnerProfileId { get; set; }
    public string SourceEventId { get; set; } = string.Empty;
    public SkillType? WeakestSkill { get; set; }
    public List<string> WeakTopics { get; set; } = new();
    public EnglishLevel Level { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public List<GoalSetting> ActiveGoals { get; set; } = new();
}

public class RecommendationResponse
{
    public int UserId { get; set; }
    public string WeakestSkill { get; set; } = string.Empty;
    public List<string> WeakTopics { get; set; } = new();
    public List<RecommendedLessonDto> RecommendedLessons { get; set; } = new();
    public string OverallReason { get; set; } = string.Empty;
}

public class RecommendedLessonDto
{
    public int LessonId { get; set; }
    public string Title { get; set; } = string.Empty;
    public double PriorityScore { get; set; }
    public string Reason { get; set; } = string.Empty;
}
