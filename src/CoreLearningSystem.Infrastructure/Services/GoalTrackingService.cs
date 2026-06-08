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

public class GoalTrackingService : IGoalTrackingService
{
    private readonly AppDbContext _context;
    private readonly ILogger<GoalTrackingService> _logger;

    private static readonly HashSet<GoalType> CertificateGoalTypes = new()
    {
        GoalType.TOEIC, GoalType.IELTS, GoalType.VSTEP
    };

    public GoalTrackingService(AppDbContext context, ILogger<GoalTrackingService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<GoalProgressResult> UpdateGoalProgressAsync(GoalProgressRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var result = new GoalProgressResult { LearnerProfileId = request.LearnerProfileId };
        var now = request.OccurredAt == default ? DateTime.UtcNow : request.OccurredAt;

        var activeGoals = await _context.GoalSettings
            .Include(g => g.ProgressHistories)
            .Where(g => g.LearnerProfileId == request.LearnerProfileId
                     && g.Status == GoalStatus.Active
                     && g.Deadline > now)
            .ToListAsync(cancellationToken);

        if (!activeGoals.Any())
        {
            _logger.LogInformation("No active goals found for LearnerProfileId: {ProfileId}", request.LearnerProfileId);
            return result;
        }

        var affectedGoals = activeGoals.Where(g => IsGoalAffectedByTrigger(g.Type, request.TriggerGoalType)).ToList();

        if (!affectedGoals.Any())
        {
            // Still produce advisories for all active goals
            result.Advisories = activeGoals.Select(g => ComputeAdvisory(g, now)).ToList();
            return result;
        }

        await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var goal in affectedGoals)
            {
                // Idempotency check
                if (goal.ProgressHistories.Any(h => h.SourceEventId == request.SourceEventId))
                {
                    _logger.LogInformation("Goal progress already recorded for GoalId: {GoalId}, SourceEventId: {EventId} — skipping",
                        goal.Id, request.SourceEventId);
                    continue;
                }

                // For SkillScore goal, verify skill target matches request.SkillName if it's set
                if (goal.Type == GoalType.SkillScore && !string.IsNullOrEmpty(goal.SkillTarget))
                {
                    if (string.IsNullOrEmpty(request.SkillName) || 
                        !string.Equals(goal.SkillTarget, request.SkillName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                var prevValue = goal.CurrentValue;
                var prevStatus = goal.Status;

                // Update value based on goal type
                UpdateGoalValue(goal, request, now);

                // Sync derived fields
                SyncGoalState(goal, now);

                var history = new GoalProgressHistory
                {
                    GoalId = goal.Id,
                    LearnerProfileId = request.LearnerProfileId,
                    SourceEventId = request.SourceEventId,
                    PreviousValue = prevValue,
                    AddedValue = goal.CurrentValue - prevValue,
                    NewValue = goal.CurrentValue,
                    StatusBefore = prevStatus,
                    StatusAfter = goal.Status,
                    Reason = $"Triggered by {request.TriggerGoalType} event",
                    RecordedAt = now
                };

                _context.GoalProgressHistories.Add(history);
                goal.UpdatedAt = now;

                _logger.LogInformation(
                    "Goal progress updated. GoalId: {GoalId}, Type: {Type}, Prev: {Prev} → New: {New}, Status: {Status}, SourceEventId: {EventId}",
                    goal.Id, goal.Type, prevValue, goal.CurrentValue, goal.Status, request.SourceEventId);

                if (goal.Status == GoalStatus.Completed && prevStatus != GoalStatus.Completed)
                {
                    result.CompletedGoals.Add(new CompletedGoalDto
                    {
                        GoalId = goal.Id,
                        GoalType = goal.Type.ToString(),
                        Title = goal.Target,
                        TargetValue = goal.TargetValue,
                        AchievedValue = goal.CurrentValue,
                        CompletedAt = goal.CompletedAt ?? now
                    });

                    _logger.LogInformation("Goal completed! GoalId: {GoalId}, Type: {Type}, UserId: {UserId}",
                        goal.Id, goal.Type, request.UserId);
                }

                result.GoalsUpdated++;
            }

            await _context.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            // Advisories for all still-active goals after update
            var stillActive = activeGoals.Where(g => g.Status == GoalStatus.Active).ToList();
            result.Advisories = stillActive.Select(g => ComputeAdvisory(g, now)).ToList();

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating goal progress for profile {ProfileId}", request.LearnerProfileId);
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static bool IsGoalAffectedByTrigger(GoalType goalType, GoalType triggerType)
    {
        return (goalType, triggerType) switch
        {
            (GoalType.LessonsPerWeek, GoalType.LessonsPerWeek) => true,
            (GoalType.QuizzesPerWeek, GoalType.QuizzesPerWeek) => true,
            (GoalType.LearningStreak, GoalType.LearningStreak) => true,
            (GoalType.SkillScore, GoalType.SkillScore) => true,
            (GoalType.TargetLevel, GoalType.TargetLevel) => true,
            // Certificate goals get estimated progress from lessons
            (GoalType.TOEIC, GoalType.LessonsPerWeek) => true,
            (GoalType.IELTS, GoalType.LessonsPerWeek) => true,
            (GoalType.VSTEP, GoalType.LessonsPerWeek) => true,
            // General goal updated by both lessons and quizzes
            (GoalType.General, GoalType.LessonsPerWeek) => true,
            (GoalType.General, GoalType.QuizzesPerWeek) => true,
            _ => false
        };
    }

    private static void UpdateGoalValue(GoalSetting goal, GoalProgressRequest request, DateTime now)
    {
        if (CertificateGoalTypes.Contains(goal.Type))
        {
            // Certificate goals: only update estimated ProgressPercentage, DO NOT change CurrentValue
            // ProgressPercentage represents estimated preparation, not actual certification achievement
            // Increment by a small estimated amount (0.5% per lesson completed, max 90%)
            goal.ProgressPercentage = Math.Clamp(goal.ProgressPercentage + 0.5, 0, 90);
            // DO NOT change Status to Completed — certificate requires verified exam result
            return;
        }

        if (goal.Type == GoalType.LessonsPerWeek || goal.Type == GoalType.QuizzesPerWeek)
        {
            // Weekly period: count from history within current week
            var weekStart = GetWeekStart(now);
            var weekEnd = weekStart.AddDays(7);

            // Count existing history entries in this week + add 1 for current event
            var existingThisWeek = goal.ProgressHistories
                .Count(h => h.RecordedAt >= weekStart && h.RecordedAt < weekEnd
                         && h.AddedValue > 0);

            // Reset CurrentValue to week count (recalculate from scratch prevents double-count)
            goal.CurrentValue = existingThisWeek + 1;
            return;
        }

        if (goal.Type == GoalType.LearningStreak)
        {
            // Streak value comes from request (calculated by AchievementEngine)
            if (request.IncrementValue > goal.CurrentValue)
            {
                goal.CurrentValue = request.IncrementValue;
            }
            return;
        }

        if (goal.Type == GoalType.SkillScore)
        {
            // Set to new skill score — not increment
            if (request.NewSkillScore.HasValue)
            {
                goal.CurrentValue = request.NewSkillScore.Value;
            }
            return;
        }

        // General and TargetLevel: simple increment
        goal.CurrentValue += request.IncrementValue;
    }

    private static void SyncGoalState(GoalSetting goal, DateTime now)
    {
        // Certificate goals NEVER auto-complete
        if (CertificateGoalTypes.Contains(goal.Type))
        {
            goal.IsCompleted = false;
            return;
        }

        // Compute ProgressPercentage
        if (goal.TargetValue > 0)
        {
            goal.ProgressPercentage = Math.Clamp(goal.CurrentValue / goal.TargetValue * 100.0, 0, 100);
        }

        // Check completion
        if (goal.CurrentValue >= goal.TargetValue && goal.Status == GoalStatus.Active)
        {
            goal.Status = GoalStatus.Completed;
            goal.IsCompleted = true;
            goal.CompletedAt ??= now;
        }
        else
        {
            goal.IsCompleted = (goal.Status == GoalStatus.Completed);
        }
    }

    public GoalAdvisoryDto GetGoalAdvisory(GoalSetting goal, DateTime now)
    {
        return ComputeAdvisory(goal, now);
    }

    private static GoalAdvisoryDto ComputeAdvisory(GoalSetting goal, DateTime now)
    {
        var totalSeconds = (goal.Deadline - goal.StartDate).TotalSeconds;
        var elapsedSeconds = (now - goal.StartDate).TotalSeconds;
        var timeElapsedPct = totalSeconds > 0 ? Math.Clamp(elapsedSeconds / totalSeconds * 100.0, 0, 100) : 100;
        var progressPct = goal.ProgressPercentage;

        GoalAdvisory advisory = GoalAdvisory.Keep;
        string reason = "Goal progress is on track.";

        if (progressPct >= 100 && timeElapsedPct < 50)
        {
            advisory = GoalAdvisory.IncreaseSuggested;
            reason = $"Goal completed before 50% of time elapsed (time: {timeElapsedPct:F0}%).";
        }
        else if (timeElapsedPct >= 50 && progressPct < 10)
        {
            advisory = GoalAdvisory.DecreaseSuggested;
            reason = $"Over 50% time elapsed but only {progressPct:F0}% progress — goal may be too ambitious.";
        }
        else if (timeElapsedPct >= 50 && progressPct < 25)
        {
            advisory = GoalAdvisory.AtRisk;
            reason = $"Over 50% time elapsed with only {progressPct:F0}% progress — risk of not finishing.";
        }

        return new GoalAdvisoryDto
        {
            GoalId = goal.Id,
            Title = goal.Target,
            Advisory = advisory,
            Reason = reason,
            ProgressPercentage = progressPct,
            TimeElapsedPercentage = timeElapsedPct
        };
    }

    private static DateTime GetWeekStart(DateTime now)
    {
        var dayOfWeek = (int)now.DayOfWeek;
        var daysSinceMonday = (dayOfWeek == 0) ? 6 : dayOfWeek - 1;
        return now.Date.AddDays(-daysSinceMonday);
    }
}
