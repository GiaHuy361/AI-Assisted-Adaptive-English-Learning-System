using System;
using System.Collections.Generic;
using System.Linq;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Infrastructure.Services;

public class AdaptiveRecommendationEngine : IAdaptiveRecommendationEngine
{
    public List<Recommendation> GenerateAndRank(
        List<Lesson> candidateLessons,
        LearnerProfile profile,
        List<LearnerWeaknessHistory> activeOrImprovingWeaknesses,
        List<string> repeatedWeakTopics,
        SkillType? weakestSkill,
        List<string> currentEventWeakTopics,
        EnglishLevel currentLevel,
        string sourceEventId)
    {
        if (candidateLessons == null) throw new ArgumentNullException(nameof(candidateLessons));
        if (profile == null) throw new ArgumentNullException(nameof(profile));
        if (activeOrImprovingWeaknesses == null) throw new ArgumentNullException(nameof(activeOrImprovingWeaknesses));
        if (repeatedWeakTopics == null) throw new ArgumentNullException(nameof(repeatedWeakTopics));

        var result = new List<Recommendation>();

        int learnerLevelOrder = GetLevelOrder(currentLevel);

        // Define eligible skills
        var eligibleSkills = new HashSet<SkillType>();
        if (weakestSkill.HasValue)
        {
            eligibleSkills.Add(weakestSkill.Value);
        }
        foreach (var w in activeOrImprovingWeaknesses)
        {
            eligibleSkills.Add(w.Skill);
        }
        foreach (var m in profile.SkillMatrices)
        {
            if (m.CurrentScore < 75)
            {
                eligibleSkills.Add(m.Skill);
            }
        }

        // If no specific weak skills are found, default to all skills present in candidate lessons to prevent empty recommendations
        if (eligibleSkills.Count == 0)
        {
            foreach (var lesson in candidateLessons)
            {
                eligibleSkills.Add(lesson.Skill);
            }
        }

        foreach (var lesson in candidateLessons)
        {
            // 1. Level range check
            int lessonLevelOrder = GetLevelOrder(lesson.Level);
            if (learnerLevelOrder > 0 && lessonLevelOrder > 0)
            {
                if (Math.Abs(lessonLevelOrder - learnerLevelOrder) > 1)
                {
                    continue; // Out of level range
                }
            }
            else
            {
                continue; // Invalid level mapping
            }

            // 2. Skill eligibility check
            if (!eligibleSkills.Contains(lesson.Skill))
            {
                continue; // Not related to an eligible skill
            }

            // 3. Compute score components
            // Component A: Weakest-skill match (Max 35)
            double skillScoreVal = 0;
            if (weakestSkill.HasValue && lesson.Skill == weakestSkill.Value)
            {
                skillScoreVal = 35;
            }
            else
            {
                var matrix = profile.SkillMatrices.FirstOrDefault(m => m.Skill == lesson.Skill);
                if (matrix != null)
                {
                    if (matrix.CurrentScore < 50)
                    {
                        skillScoreVal = 20;
                    }
                    else if (matrix.CurrentScore >= 50 && matrix.CurrentScore < 75)
                    {
                        skillScoreVal = 10;
                    }
                }
            }

            // Component B: Active/improving topic match (Max 30)
            double topicScoreVal = 0;
            string normLessonTopic = (lesson.Topic ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(normLessonTopic))
            {
                var weakness = activeOrImprovingWeaknesses.FirstOrDefault(w =>
                    w.Skill == lesson.Skill &&
                    (w.Topic ?? string.Empty).Trim().Equals(normLessonTopic, StringComparison.OrdinalIgnoreCase)
                );
                if (weakness != null)
                {
                    if (weakness.Status == WeaknessStatus.Active)
                    {
                        topicScoreVal = 30;
                    }
                    else if (weakness.Status == WeaknessStatus.Improving)
                    {
                        topicScoreVal = 15;
                    }
                }
            }

            // Component C: Level alignment (Max 15)
            double levelScoreVal = 0;
            if (lessonLevelOrder == learnerLevelOrder)
            {
                levelScoreVal = 15;
            }
            else if (lessonLevelOrder == learnerLevelOrder - 1)
            {
                var matrix = profile.SkillMatrices.FirstOrDefault(m => m.Skill == lesson.Skill);
                if (matrix != null && matrix.CurrentScore < 50)
                {
                    levelScoreVal = 10;
                }
            }
            else if (lessonLevelOrder == learnerLevelOrder + 1)
            {
                var matrix = profile.SkillMatrices.FirstOrDefault(m => m.Skill == lesson.Skill);
                if (matrix != null && matrix.CurrentScore >= 75)
                {
                    levelScoreVal = 5;
                }
            }

            // Component D: Repeated weakness (Max 10)
            double repeatedScoreVal = 0;
            if (!string.IsNullOrEmpty(normLessonTopic))
            {
                if (repeatedWeakTopics.Any(t => (t ?? string.Empty).Trim().Equals(normLessonTopic, StringComparison.OrdinalIgnoreCase)))
                {
                    repeatedScoreVal = 10;
                }
            }

            // Component E: Goal match (Max 5)
            double goalScoreVal = 0; // Audited to be 0 as GoalSetting doesn't have reliable Skill mapping

            // Component F: Current-event recency (Max 5)
            double recencyScoreVal = 0;
            if (!string.IsNullOrEmpty(normLessonTopic))
            {
                if (currentEventWeakTopics.Any(t => (t ?? string.Empty).Trim().Equals(normLessonTopic, StringComparison.OrdinalIgnoreCase)))
                {
                    recencyScoreVal = 5;
                }
            }

            // Total clamped priority score
            double totalScore = skillScoreVal + topicScoreVal + levelScoreVal + repeatedScoreVal + goalScoreVal + recencyScoreVal;
            totalScore = Math.Clamp(totalScore, 0.0, 100.0);

            // Construct recommendation reasons
            var reasons = new List<string>();
            if (skillScoreVal == 35) reasons.Add("Kỹ năng yếu nhất hiện tại");
            else if (skillScoreVal > 0) reasons.Add($"Điểm số kỹ năng còn thấp ({skillScoreVal} điểm)");

            if (topicScoreVal == 30) reasons.Add($"Chủ đề '{lesson.Topic}' đang là điểm yếu hoạt động");
            else if (topicScoreVal == 15) reasons.Add($"Chủ đề '{lesson.Topic}' đang được cải thiện");

            if (levelScoreVal == 15) reasons.Add("Phù hợp hoàn hảo với trình độ hiện tại");
            else if (levelScoreVal == 10) reasons.Add("Trình độ thấp hơn 1 cấp để củng cố nền tảng");
            else if (levelScoreVal == 5) reasons.Add("Trình độ cao hơn 1 cấp để thử thách nâng cao");

            if (repeatedScoreVal > 0) reasons.Add("Chủ đề thường xuyên gặp lỗi sai");
            if (recencyScoreVal > 0) reasons.Add("Lỗi sai vừa gặp phải trong bài làm gần nhất");

            string reasonText = reasons.Count > 0 ? string.Join("; ", reasons) : "Được đề xuất để nâng cao năng lực học tập";

            var recommendation = new Recommendation
            {
                LearnerProfileId = profile.Id,
                LessonId = lesson.Id,
                Skill = lesson.Skill,
                Topic = lesson.Topic,
                Level = lesson.Level,
                PriorityScore = totalScore,
                Reason = reasonText,
                Status = RecommendationStatus.Active,
                SourceEventId = sourceEventId,
                GeneratedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow, // Will be overridden by service options
                CreatedAt = DateTime.UtcNow
            };

            result.Add(recommendation);
        }

        // Apply tie-breaker rules
        var ranked = result
            .OrderByDescending(r => r.PriorityScore)
            .ThenByDescending(r => !string.IsNullOrEmpty(r.Topic) && currentEventWeakTopics.Any(t => t.Trim().Equals(r.Topic, StringComparison.OrdinalIgnoreCase)))
            .ThenByDescending(r => r.Level == currentLevel)
            .ThenBy(r => r.LessonId)
            .ToList();

        return ranked;
    }

    private int GetLevelOrder(EnglishLevel level)
    {
        return level switch
        {
            EnglishLevel.A1 => 1,
            EnglishLevel.A2 => 2,
            EnglishLevel.B1 => 3,
            EnglishLevel.B2 => 4,
            EnglishLevel.C1 => 5,
            EnglishLevel.C2 => 6,
            _ => 0
        };
    }
}
