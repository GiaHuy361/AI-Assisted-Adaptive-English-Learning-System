using System;
using System.Collections.Generic;
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

public class SkillMatrixRecalculationJob
{
    private readonly AppDbContext _context;
    private readonly BackgroundJobExecutor _executor;
    private readonly SkillMatrixRecalculationOptions _options;
    private readonly ILogger<SkillMatrixRecalculationJob> _logger;

    public SkillMatrixRecalculationJob(
        AppDbContext context,
        BackgroundJobExecutor executor,
        IOptions<SkillMatrixRecalculationOptions> options,
        ILogger<SkillMatrixRecalculationJob> logger)
    {
        _context = context;
        _executor = executor;
        _options = options.Value;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("SkillMatrixRecalculationJob is disabled by configuration.");
            return;
        }

        await _executor.ExecuteAsync("skill-matrix-recalculation", async (executionId, token) =>
        {
            var processed = 0;
            var succeeded = 0;
            var failed = 0;
            var skipped = 0;

            var now = DateTime.UtcNow;
            var weekStart = GetWeekStart(now);
            var periodKey = $"recalc:{_options.PeriodKey}:{weekStart:yyyy-MM-dd}";

            // Fetch active learner profiles
            var activeProfiles = await _context.LearnerProfiles
                .Include(p => p.User)
                .Where(p => p.ActivityStatus == ActivityStatus.Active && !p.User.IsLocked)
                .ToListAsync(token);

            _logger.LogInformation("SkillMatrixRecalculationJob: Re-evaluating {Count} profiles.", activeProfiles.Count);

            foreach (var profile in activeProfiles)
            {
                try
                {
                    // 1. Check if recalculation already applied for this profile in this period
                    var alreadyProcessed = await _context.SkillMatrixHistories
                        .AnyAsync(h => h.LearnerProfileId == profile.Id && h.DecayPeriodKey == periodKey, token);

                    if (alreadyProcessed)
                    {
                        skipped++;
                        continue;
                    }

                    // 2. Fetch history of placement results and quiz attempts
                    var placementResults = await _context.PlacementTestResults
                        .Where(r => r.LearnerProfileId == profile.Id)
                        .OrderBy(r => r.TakenAt)
                        .ToListAsync(token);

                    var quizAttempts = await _context.QuizAttempts
                        .Include(a => a.Details)
                            .ThenInclude(d => d.Question)
                        .Where(a => a.LearnerProfileId == profile.Id)
                        .OrderBy(a => a.AttemptedAt)
                        .ToListAsync(token);

                    // Reconstruct chronological events list
                    var events = new List<ProficiencyEvent>();
                    foreach (var pr in placementResults)
                    {
                        events.Add(new ProficiencyEvent { TakenAt = pr.TakenAt, IsPlacement = true, Score = pr.Score });
                    }
                    foreach (var qa in quizAttempts)
                    {
                        events.Add(new ProficiencyEvent { TakenAt = qa.AttemptedAt, IsPlacement = false, QuizAttempt = qa });
                    }

                    events = events.OrderBy(e => e.TakenAt).ToList();

                    // Calculate final scores for each skill type
                    var finalScores = new Dictionary<SkillType, double>();
                    
                    foreach (var ev in events)
                    {
                        if (ev.IsPlacement)
                        {
                            var placementScore = ev.Score;
                            if (placementScore <= 10) placementScore *= 10; // scale out of 100 if out of 10

                            // Apply placement base to all skills
                            foreach (SkillType skill in Enum.GetValues(typeof(SkillType)))
                            {
                                if (skill == SkillType.General) continue;
                                if (!finalScores.TryGetValue(skill, out var previousScore))
                                {
                                    finalScores[skill] = placementScore;
                                }
                                else
                                {
                                    finalScores[skill] = (0.70 * previousScore) + (0.30 * placementScore);
                                }
                            }
                        }
                        else if (ev.QuizAttempt != null && ev.QuizAttempt.Details != null)
                        {
                            // Group details by question skill
                            var detailsBySkill = ev.QuizAttempt.Details
                                .Where(d => d.Question != null)
                                .GroupBy(d => d.Question.Skill);

                            foreach (var group in detailsBySkill)
                            {
                                var skill = group.Key;
                                var correctCount = group.Count(d => d.IsCorrect);
                                var totalCount = group.Count();
                                if (totalCount == 0) continue;

                                double assessmentScore = ((double)correctCount / totalCount) * 100.0;
                                double weight = Math.Min(0.40, Math.Max(0.15, totalCount / 20.0));

                                if (!finalScores.TryGetValue(skill, out var previousScore))
                                {
                                    finalScores[skill] = assessmentScore;
                                }
                                else
                                {
                                    finalScores[skill] = (previousScore * (1.0 - weight)) + (assessmentScore * weight);
                                }
                                finalScores[skill] = Math.Clamp(finalScores[skill], 0.0, 100.0);
                            }
                        }
                    }

                    // 3. Update Skill Matrix if changes exceed threshold
                    var skillMatrices = await _context.SkillMatrices
                        .Where(sm => sm.LearnerProfileId == profile.Id)
                        .ToListAsync(token);

                    await using var tx = await _context.Database.BeginTransactionAsync(token);
                    try
                    {
                        var didUpdate = false;
                        foreach (var kvp in finalScores)
                        {
                            var skill = kvp.Key;
                            var calculatedScore = Math.Round(kvp.Value, 1);

                            var dbMatrix = skillMatrices.FirstOrDefault(sm => sm.Skill == skill);
                            double currentScore = dbMatrix?.CurrentScore ?? 0.0;

                            // Check difference threshold
                            if (dbMatrix == null || Math.Abs(calculatedScore - currentScore) >= _options.DifferenceThreshold)
                            {
                                didUpdate = true;
                                if (dbMatrix == null)
                                {
                                    dbMatrix = new SkillMatrix
                                    {
                                        LearnerProfileId = profile.Id,
                                        Skill = skill,
                                        CurrentScore = calculatedScore,
                                        MasteryLevel = ClassifyMasteryLevel(calculatedScore),
                                        TotalAssessments = quizAttempts.Count,
                                        LastAssessmentScore = calculatedScore,
                                        CreatedAt = now,
                                        LastUpdatedAt = now
                                    };
                                    await _context.SkillMatrices.AddAsync(dbMatrix, token);
                                    await _context.SaveChangesAsync(token); // generate ID
                                }
                                else
                                {
                                    dbMatrix.CurrentScore = calculatedScore;
                                    dbMatrix.MasteryLevel = ClassifyMasteryLevel(calculatedScore);
                                    dbMatrix.LastUpdatedAt = now;
                                    _context.SkillMatrices.Update(dbMatrix);
                                }

                                var history = new SkillMatrixHistory
                                {
                                    SkillMatrixId = dbMatrix.Id,
                                    LearnerProfileId = profile.Id,
                                    Skill = skill,
                                    PreviousScore = currentScore,
                                    AssessmentScore = calculatedScore,
                                    NewScore = calculatedScore,
                                    SourceType = MatrixSourceType.PeriodicRecalculation,
                                    SourceId = 0,
                                    EventId = Guid.NewGuid(),
                                    Reason = $"Periodic skill matrix recalculation (Diff: {Math.Round(calculatedScore - currentScore, 1)}).",
                                    DecayPeriodKey = periodKey,
                                    RecordedAt = now
                                };

                                await _context.SkillMatrixHistories.AddAsync(history, token);
                            }
                        }

                        if (didUpdate)
                        {
                            await _context.SaveChangesAsync(token);
                            await tx.CommitAsync(token);
                            succeeded++;
                        }
                        else
                        {
                            await tx.RollbackAsync(token);
                            skipped++;
                        }
                    }
                    catch (Exception ex)
                    {
                        await tx.RollbackAsync(token);
                        failed++;
                        _logger.LogError(ex, "Failed to apply recalculated skill matrix transaction for profile {ProfileId}", profile.Id);
                    }

                    processed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex, "Failed to re-evaluate skill matrix for profile {ProfileId}", profile.Id);
                }
            }

            return (processed, succeeded, failed, skipped);
        }, cancellationToken);
    }

    private static DateTime GetWeekStart(DateTime date)
    {
        var dayOfWeek = (int)date.DayOfWeek;
        var daysSinceMonday = (dayOfWeek == 0) ? 6 : dayOfWeek - 1;
        return date.Date.AddDays(-daysSinceMonday);
    }

    private static MasteryLevel ClassifyMasteryLevel(double score)
    {
        if (score < 50.0)
        {
            return MasteryLevel.Weak;
        }
        if (score < 75.0)
        {
            return MasteryLevel.Average;
        }
        return MasteryLevel.Good;
    }

    private class ProficiencyEvent
    {
        public DateTime TakenAt { get; set; }
        public bool IsPlacement { get; set; }
        public double Score { get; set; }
        public QuizAttempt? QuizAttempt { get; set; }
    }
}
