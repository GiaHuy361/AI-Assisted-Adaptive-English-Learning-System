using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Infrastructure.Persistence;

namespace CoreLearningSystem.Infrastructure.Services;

public class RecommendationRegenerationJob
{
    private readonly AppDbContext _context;
    private readonly IRecommendationService _recommendationService;
    private readonly BackgroundJobExecutor _executor;
    private readonly ILogger<RecommendationRegenerationJob> _logger;

    public RecommendationRegenerationJob(
        AppDbContext context,
        IRecommendationService recommendationService,
        BackgroundJobExecutor executor,
        ILogger<RecommendationRegenerationJob> logger)
    {
        _context = context;
        _recommendationService = recommendationService;
        _executor = executor;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await _executor.ExecuteAsync("recommendation-regeneration", async (executionId, token) =>
        {
            var processed = 0;
            var succeeded = 0;
            var failed = 0;
            var skipped = 0;

            // Find ineffective evaluations
            var ineffectiveEvaluations = await _context.RecommendationEffectivenesses
                .Include(e => e.Recommendation)
                .Where(e => !e.WasEffective)
                .ToListAsync(token);

            if (ineffectiveEvaluations.Count == 0)
            {
                return (processed, succeeded, failed, skipped);
            }

            _logger.LogInformation("RecommendationRegenerationJob: Found {Count} ineffective recommendations to process.", ineffectiveEvaluations.Count);

            foreach (var eval in ineffectiveEvaluations)
            {
                processed++;

                try
                {
                    // Check if already regenerated
                    var regenEventId = $"regen_{eval.RecommendationId}";
                    var alreadyRegenerated = await _context.Recommendations
                        .AnyAsync(r => r.SourceEventId == regenEventId, token);

                    if (alreadyRegenerated)
                    {
                        skipped++;
                        continue;
                    }

                    // Verify if learner is still weak in this skill/topic
                    if (!Enum.TryParse<SkillType>(eval.Skill, true, out var skillType))
                    {
                        skipped++;
                        continue;
                    }

                    var topic = eval.Topic;
                    var isStillWeak = await _context.LearnerWeaknessHistories
                        .AnyAsync(w => w.LearnerProfileId == eval.LearnerProfileId &&
                                       w.Skill == skillType &&
                                       w.Topic == topic &&
                                       (w.Status == WeaknessStatus.Active || w.Status == WeaknessStatus.Improving), token);

                    if (!isStillWeak)
                    {
                        skipped++;
                        continue;
                    }

                    // Expire/Replace the old recommendation in DB
                    var rec = eval.Recommendation;
                    if (rec != null && rec.Status == RecommendationStatus.Completed)
                    {
                        // Add history log for Replaced action
                        var history = new RecommendationHistory
                        {
                            RecommendationId = rec.Id,
                            LearnerProfileId = rec.LearnerProfileId,
                            LessonId = rec.LessonId,
                            SourceEventId = regenEventId,
                            Action = RecommendationAction.Replaced,
                            PreviousStatus = RecommendationStatus.Completed,
                            NewStatus = RecommendationStatus.Replaced,
                            Reason = "Recommendation marked Replaced due to ineffectiveness.",
                            RecordedAt = DateTime.UtcNow
                        };
                        await _context.RecommendationHistories.AddAsync(history, token);
                    }

                    // Trigger regeneration via the recommendation service
                    var profile = await _context.LearnerProfiles.FindAsync(eval.LearnerProfileId);
                    if (profile == null)
                    {
                        failed++;
                        continue;
                    }

                    _logger.LogInformation("RecommendationRegenerationJob: Regenerating recommendation for LearnerProfile {ProfileId}, Skill {Skill}, Topic {Topic}",
                        eval.LearnerProfileId, eval.Skill, eval.Topic);

                    var request = new RecommendationRequest
                    {
                        UserId = profile.UserId,
                        LearnerProfileId = profile.Id,
                        SourceEventId = regenEventId,
                        WeakestSkill = skillType,
                        WeakTopics = new List<string> { eval.Topic },
                        Level = profile.Level,
                        OccurredAt = DateTime.UtcNow
                    };

                    await _recommendationService.GenerateRecommendationsAsync(request);
                    succeeded++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex, "Failed to regenerate recommendations for effectiveness ID {EvalId}", eval.Id);
                }
            }

            if (succeeded > 0)
            {
                await _context.SaveChangesAsync(token);
            }

            return (processed, succeeded, failed, skipped);
        }, cancellationToken);
    }
}
