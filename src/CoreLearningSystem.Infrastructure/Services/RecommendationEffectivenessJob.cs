using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CoreLearningSystem.Application.Options;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Infrastructure.Persistence;

namespace CoreLearningSystem.Infrastructure.Services;

public class RecommendationEffectivenessJob
{
    private readonly AppDbContext _context;
    private readonly BackgroundJobExecutor _executor;
    private readonly RecommendationEffectivenessOptions _options;
    private readonly ILogger<RecommendationEffectivenessJob> _logger;

    public RecommendationEffectivenessJob(
        AppDbContext context,
        BackgroundJobExecutor executor,
        IOptions<RecommendationEffectivenessOptions> options,
        ILogger<RecommendationEffectivenessJob> logger)
    {
        _context = context;
        _executor = executor;
        _options = options.Value;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await _executor.ExecuteAsync("recommendation-effectiveness", async (executionId, token) =>
        {
            var processed = 0;
            var succeeded = 0;
            var failed = 0;
            var skipped = 0;

            // Find Completed recommendations
            var completedRecommendations = await _context.Recommendations
                .Include(r => r.Lesson)
                .Where(r => r.Status == RecommendationStatus.Completed && r.CompletedAt.HasValue)
                .ToListAsync(token);

            if (completedRecommendations.Count == 0)
            {
                return (processed, succeeded, failed, skipped);
            }

            _logger.LogInformation("RecommendationEffectivenessJob: Found {Count} completed recommendations to evaluate.", completedRecommendations.Count);

            foreach (var rec in completedRecommendations)
            {
                processed++;

                try
                {
                    // Check if already evaluated
                    var alreadyEvaluated = await _context.RecommendationEffectivenesses
                        .AnyAsync(e => e.RecommendationId == rec.Id, token);

                    if (alreadyEvaluated)
                    {
                        skipped++;
                        continue;
                    }

                    // Find first quiz attempt containing questions on the same skill/topic after completion date
                    var subsequentAttempts = await _context.QuizAttempts
                        .Include(a => a.Details)
                            .ThenInclude(d => d.Question)
                        .Where(a => a.LearnerProfileId == rec.LearnerProfileId && 
                                    a.AttemptedAt > rec.CompletedAt.Value &&
                                    a.AttemptedAt <= rec.CompletedAt.Value.AddDays(_options.EvaluationWindowDays))
                        .OrderBy(a => a.AttemptedAt)
                        .ToListAsync(token);

                    QuizAttempt? matchingQuizAttempt = null;
                    foreach (var attempt in subsequentAttempts)
                    {
                        var hasSameSkillTopic = attempt.Details.Any(d => d.Question != null && 
                                                                         d.Question.Skill == rec.Skill &&
                                                                         d.Question.Topic.Equals(rec.Topic, StringComparison.OrdinalIgnoreCase));
                        if (hasSameSkillTopic)
                        {
                            matchingQuizAttempt = attempt;
                            break;
                        }
                    }

                    if (matchingQuizAttempt == null)
                    {
                        // No later quiz => not evaluated (skip)
                        skipped++;
                        continue;
                    }

                    // Find new score in history for this quiz attempt
                    var historyAfter = await _context.SkillMatrixHistories
                        .Where(h => h.LearnerProfileId == rec.LearnerProfileId &&
                                    h.Skill == rec.Skill &&
                                    h.SourceType == MatrixSourceType.Quiz &&
                                    h.SourceId == matchingQuizAttempt.Id)
                        .FirstOrDefaultAsync(token);

                    double scoreAfter = historyAfter?.NewScore ?? matchingQuizAttempt.Score;

                    // Find score before recommendation was generated
                    var historyBefore = await _context.SkillMatrixHistories
                        .Where(h => h.LearnerProfileId == rec.LearnerProfileId &&
                                    h.Skill == rec.Skill &&
                                    h.RecordedAt <= rec.GeneratedAt)
                        .OrderByDescending(h => h.RecordedAt)
                        .FirstOrDefaultAsync(token);

                    double scoreBefore = historyBefore?.NewScore ?? historyAfter?.PreviousScore ?? 0.0;

                    double improvement = scoreAfter - scoreBefore;
                    
                    // Effectiveness rules:
                    // 1. Score improved by configured threshold
                    // 2. Subsequent quiz score satisfies minimum requirement if configured
                    bool wasEffective = improvement >= _options.MinimumImprovementPoints;

                    var effectiveness = new RecommendationEffectiveness
                    {
                        RecommendationId = rec.Id,
                        LearnerProfileId = rec.LearnerProfileId,
                        LessonId = rec.LessonId,
                        Skill = rec.Skill.ToString(),
                        Topic = rec.Topic,
                        ScoreBefore = scoreBefore,
                        ScoreAfter = scoreAfter,
                        Improvement = improvement,
                        WasEffective = wasEffective,
                        EvaluatedAt = DateTime.UtcNow,
                        SourceQuizAttemptId = matchingQuizAttempt.Id,
                        CreatedAt = DateTime.UtcNow
                    };

                    _context.RecommendationEffectivenesses.Add(effectiveness);
                    succeeded++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex, "Failed to evaluate recommendation effectiveness for ID {RecId}", rec.Id);
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
