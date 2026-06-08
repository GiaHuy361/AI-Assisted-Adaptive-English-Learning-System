using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Infrastructure.Persistence;

namespace CoreLearningSystem.Infrastructure.Services;

public class RecommendationAnalyticsService : IRecommendationAnalyticsService
{
    private readonly AppDbContext _context;

    public RecommendationAnalyticsService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<RecommendationStatisticSnapshot> ComputeAndSaveSnapshotAsync(DateTime periodStart, DateTime periodEnd)
    {
        // 1. Gather counts
        var totalRecs = await _context.Recommendations
            .CountAsync(r => r.GeneratedAt >= periodStart && r.GeneratedAt <= periodEnd);

        var completedRecs = await _context.Recommendations
            .CountAsync(r => r.Status == RecommendationStatus.Completed && 
                             r.CompletedAt.HasValue &&
                             r.CompletedAt.Value >= periodStart && 
                             r.CompletedAt.Value <= periodEnd);

        var evaluations = await _context.RecommendationEffectivenesses
            .Where(e => e.EvaluatedAt >= periodStart && e.EvaluatedAt <= periodEnd)
            .ToListAsync();

        var effectiveCount = evaluations.Count(e => e.WasEffective);
        var effectivenessRate = completedRecs > 0 ? (double)effectiveCount / completedRecs : 0.0;
        var avgImprovement = evaluations.Count > 0 ? evaluations.Average(e => e.Improvement) : 0.0;

        // 2. Identify top effective lesson, skill, and topic
        int? topLessonId = null;
        string? topSkill = null;
        string? topTopic = null;

        if (evaluations.Count > 0)
        {
            var topLessonGroup = evaluations
                .Where(e => e.WasEffective)
                .GroupBy(e => e.LessonId)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();
            if (topLessonGroup != null) topLessonId = topLessonGroup.Key;

            var topSkillGroup = evaluations
                .Where(e => e.WasEffective)
                .GroupBy(e => e.Skill)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();
            if (topSkillGroup != null) topSkill = topSkillGroup.Key;

            var topTopicGroup = evaluations
                .Where(e => e.WasEffective)
                .GroupBy(e => e.Topic)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();
            if (topTopicGroup != null) topTopic = topTopicGroup.Key;
        }

        var snapshot = new RecommendationStatisticSnapshot
        {
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            LessonId = topLessonId,
            Skill = topSkill,
            Topic = topTopic,
            RecommendationCount = totalRecs,
            CompletionCount = completedRecs,
            EffectiveCount = effectiveCount,
            EffectivenessRate = effectivenessRate,
            AverageImprovement = avgImprovement,
            GeneratedAt = DateTime.UtcNow
        };

        await _context.RecommendationStatisticSnapshots.AddAsync(snapshot);
        await _context.SaveChangesAsync();

        return snapshot;
    }
}
