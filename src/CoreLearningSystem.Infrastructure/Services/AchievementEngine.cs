using System;
using System.Collections.Generic;
using System.Linq;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Infrastructure.Services;

public class AchievementEngine : IAchievementEngine
{
    public List<EligibleAchievement> Evaluate(
        AchievementEvaluationRequest request,
        IReadOnlyList<AchievementBadge> allActiveBadges,
        IReadOnlyList<LearnerBadge> alreadyEarned)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (allActiveBadges == null) throw new ArgumentNullException(nameof(allActiveBadges));
        if (alreadyEarned == null) throw new ArgumentNullException(nameof(alreadyEarned));

        var eligible = new List<EligibleAchievement>();
        var earnedIds = new HashSet<int>(alreadyEarned.Select(b => b.BadgeId));

        foreach (var badge in allActiveBadges)
        {
            if (earnedIds.Contains(badge.Id))
            {
                continue;
            }

            bool isEligible = false;
            double metricValue = 0;
            string reason = string.Empty;

            switch (badge.AchievementType)
            {
                case AchievementType.FirstLesson:
                    isEligible = request.IsFirstLesson || request.CompletedLessonCount >= badge.Threshold;
                    metricValue = request.CompletedLessonCount;
                    reason = "Completed your first lesson!";
                    break;

                case AchievementType.FirstQuiz:
                    isEligible = request.IsFirstQuiz;
                    metricValue = 1;
                    reason = "Completed your first quiz attempt!";
                    break;

                case AchievementType.LessonCount:
                    isEligible = request.CompletedLessonCount >= badge.Threshold;
                    metricValue = request.CompletedLessonCount;
                    reason = $"Completed {badge.Threshold} lessons.";
                    break;

                case AchievementType.QuizHighScoreCount:
                    isEligible = request.HighScoreQuizCount >= badge.Threshold;
                    metricValue = request.HighScoreQuizCount;
                    reason = $"Scored high (>=80%) on {badge.Threshold} quizzes.";
                    break;

                case AchievementType.LearningStreak:
                    isEligible = request.CurrentStreakDays >= badge.Threshold;
                    metricValue = request.CurrentStreakDays;
                    reason = $"Maintained a learning streak of {badge.Threshold} days.";
                    break;

                case AchievementType.GoalCompletionCount:
                    isEligible = request.CompletedGoalCount >= badge.Threshold;
                    metricValue = request.CompletedGoalCount;
                    reason = $"Completed {badge.Threshold} goals.";
                    break;

                case AchievementType.SkillImprovement:
                    isEligible = request.SkillImprovementPoints >= badge.Threshold;
                    metricValue = request.SkillImprovementPoints;
                    reason = $"Improved a skill by {badge.Threshold} points.";
                    break;

                case AchievementType.FirstPlacementTest:
                    isEligible = request.IsFirstPlacementTest;
                    metricValue = 1;
                    reason = "Completed your first placement test!";
                    break;
            }

            if (isEligible)
            {
                eligible.Add(new EligibleAchievement
                {
                    AchievementId = badge.Id,
                    Code = badge.Code,
                    Name = badge.Name,
                    AchievementType = badge.AchievementType,
                    MetricValue = metricValue,
                    Reason = reason
                });
            }
        }

        return eligible;
    }
}
