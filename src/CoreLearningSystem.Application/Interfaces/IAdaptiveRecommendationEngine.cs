using System.Collections.Generic;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Application.Interfaces;

public interface IAdaptiveRecommendationEngine
{
    /// <summary>
    /// Generate and rank recommendations.
    /// feedbackScores: optional dict keyed by lessonId → delta score (positive = bonus, negative = penalty).
    /// Injected by the service layer; engine itself must NOT query DB or cache.
    /// </summary>
    List<Recommendation> GenerateAndRank(
        List<Lesson> candidateLessons,
        LearnerProfile profile,
        List<LearnerWeaknessHistory> activeOrImprovingWeaknesses,
        List<string> repeatedWeakTopics,
        SkillType? weakestSkill,
        List<string> currentEventWeakTopics,
        EnglishLevel currentLevel,
        string sourceEventId,
        List<GoalSetting>? activeGoals = null,
        Dictionary<int, double>? feedbackScores = null
    );
}
