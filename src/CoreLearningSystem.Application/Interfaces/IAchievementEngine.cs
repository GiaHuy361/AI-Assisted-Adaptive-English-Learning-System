using System.Collections.Generic;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Domain.Entities;

namespace CoreLearningSystem.Application.Interfaces;

public interface IAchievementEngine
{
    List<EligibleAchievement> Evaluate(
        AchievementEvaluationRequest request,
        IReadOnlyList<AchievementBadge> allActiveBadges,
        IReadOnlyList<LearnerBadge> alreadyEarned);
}
