using System.Collections.Generic;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Application.Interfaces;

public interface IAdaptiveRecommendationEngine
{
    List<Recommendation> GenerateAndRank(
        List<Lesson> candidateLessons,
        LearnerProfile profile,
        List<LearnerWeaknessHistory> activeOrImprovingWeaknesses,
        List<string> repeatedWeakTopics,
        SkillType? weakestSkill,
        List<string> currentEventWeakTopics,
        EnglishLevel currentLevel,
        string sourceEventId
    );
}
