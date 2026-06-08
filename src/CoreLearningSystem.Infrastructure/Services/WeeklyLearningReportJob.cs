using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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

public class WeeklyLearningReportJob
{
    private readonly AppDbContext _context;
    private readonly INotificationService _notificationService;
    private readonly BackgroundJobExecutor _executor;
    private readonly ILogger<WeeklyLearningReportJob> _logger;

    public WeeklyLearningReportJob(
        AppDbContext context,
        INotificationService notificationService,
        BackgroundJobExecutor executor,
        ILogger<WeeklyLearningReportJob> logger)
    {
        _context = context;
        _notificationService = notificationService;
        _executor = executor;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await _executor.ExecuteAsync("weekly-learning-report", async (executionId, token) =>
        {
            var processed = 0;
            var succeeded = 0;
            var failed = 0;
            var skipped = 0;

            var now = DateTime.UtcNow;
            
            // Calculate previous week: Monday 00:00:00 UTC to next Monday 00:00:00 UTC (exclusive)
            var currentWeekMonday = GetWeekStart(now);
            var weekStart = currentWeekMonday.AddDays(-7);
            var weekEnd = currentWeekMonday;

            var weekStartStr = weekStart.ToString("yyyy-MM-dd");

            var activeProfiles = await _context.LearnerProfiles
                .Include(p => p.User)
                .Where(p => p.ActivityStatus == ActivityStatus.Active && !p.User.IsLocked)
                .ToListAsync(token);

            _logger.LogInformation("WeeklyLearningReportJob: Generating reports for previous week ({Start} to {End}) for {Count} profiles.", 
                weekStartStr, weekEnd.ToString("yyyy-MM-dd"), activeProfiles.Count);

            foreach (var profile in activeProfiles)
            {
                processed++;
                try
                {
                    var idempotencyKey = $"weekly-report:{profile.UserId}:{weekStartStr}";

                    // 1. Check if already exists in DB
                    var reportExists = await _context.WeeklyLearningReports
                        .AnyAsync(r => r.LearnerProfileId == profile.Id && r.WeekStart == weekStart, token);

                    if (reportExists)
                    {
                        skipped++;
                        continue;
                    }

                    // 2. Aggregate metrics
                    var lessonsCompleted = await _context.LearnerProgresses
                        .CountAsync(lp => lp.LearnerProfileId == profile.Id 
                                       && lp.IsCompleted 
                                       && lp.CompletedAt >= weekStart 
                                       && lp.CompletedAt < weekEnd, token);

                    var quizzesCompleted = await _context.QuizAttempts
                        .CountAsync(qa => qa.LearnerProfileId == profile.Id 
                                       && qa.AttemptedAt >= weekStart 
                                       && qa.AttemptedAt < weekEnd, token);

                    var quizAttempts = await _context.QuizAttempts
                        .Where(qa => qa.LearnerProfileId == profile.Id 
                                  && qa.AttemptedAt >= weekStart 
                                  && qa.AttemptedAt < weekEnd)
                        .Select(qa => qa.Score)
                        .ToListAsync(token);

                    var averageScore = quizAttempts.Any() ? quizAttempts.Average() : 0.0;

                    var recommendationsCompleted = await _context.Recommendations
                        .CountAsync(r => r.LearnerProfileId == profile.Id 
                                      && r.Status == RecommendationStatus.Completed 
                                      && r.CompletedAt >= weekStart 
                                      && r.CompletedAt < weekEnd, token);

                    // Streak Days inside this week
                    var streakDays = await CalculateWeeklyStreakAsync(profile.Id, weekStart, weekEnd, token);

                    // Strongest and Weakest Skill from SkillMatrix
                    var skillMatrices = await _context.SkillMatrices
                        .Where(sm => sm.LearnerProfileId == profile.Id)
                        .ToListAsync(token);

                    var strongestSkill = skillMatrices.OrderByDescending(sm => sm.CurrentScore).FirstOrDefault()?.Skill.ToString() ?? "General";
                    var weakestSkill = skillMatrices.OrderBy(sm => sm.CurrentScore).FirstOrDefault()?.Skill.ToString() ?? "General";

                    // Badges Earned
                    var badges = await _context.LearnerBadges
                        .Include(lb => lb.Badge)
                        .Where(lb => lb.LearnerProfileId == profile.Id 
                                  && lb.UnlockedAt >= weekStart 
                                  && lb.UnlockedAt < weekEnd)
                        .Select(lb => lb.Badge.Code)
                        .ToListAsync(token);
                    
                    var badgesJson = JsonSerializer.Serialize(badges);

                    // Goal Progress Summary
                    var goals = await _context.GoalSettings
                        .Where(g => g.LearnerProfileId == profile.Id 
                                 && (g.Status == GoalStatus.Active || (g.CompletedAt >= weekStart && g.CompletedAt < weekEnd)))
                        .Select(g => new { g.Id, g.Target, g.ProgressPercentage, Status = g.Status.ToString() })
                        .ToListAsync(token);
                    
                    var goalsJson = JsonSerializer.Serialize(goals);

                    // 3. Create Notification for summary
                    var badgeCount = badges.Count;
                    var summaryMsg = $"Báo cáo tuần {weekStartStr}: Bạn đã hoàn thành {lessonsCompleted} bài học, làm {quizzesCompleted} bài quiz (điểm TB: {averageScore:F1}%), hoàn thành {recommendationsCompleted} gợi ý bài học và đạt {badgeCount} huy hiệu mới!";

                    var notifReq = new CreateNotificationRequest
                    {
                        UserId = profile.UserId,
                        LearnerProfileId = profile.Id,
                        Type = NotificationType.WeeklyReport,
                        Channel = NotificationChannel.InAppAndEmail,
                        Title = $"Báo cáo học tập tuần ({weekStartStr})",
                        Message = summaryMsg,
                        IdempotencyKey = idempotencyKey,
                        SourceType = "WeeklyReport",
                        SourceId = weekStartStr
                    };

                    var details = await _notificationService.CreateNotificationAsync(notifReq, token);

                    // 4. Save WeeklyLearningReport
                    var report = new WeeklyLearningReport
                    {
                        LearnerProfileId = profile.Id,
                        WeekStart = weekStart,
                        WeekEnd = weekEnd,
                        LessonsCompleted = lessonsCompleted,
                        QuizzesCompleted = quizzesCompleted,
                        AverageScore = averageScore,
                        StrongestSkill = strongestSkill,
                        WeakestSkill = weakestSkill,
                        GoalProgressSummary = goalsJson,
                        BadgesEarned = badgesJson,
                        RecommendationsCompleted = recommendationsCompleted,
                        StreakDays = streakDays,
                        GeneratedAt = now,
                        NotificationId = details?.Id
                    };

                    _context.WeeklyLearningReports.Add(report);
                    await _context.SaveChangesAsync(token);

                    succeeded++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex, "Failed to generate weekly learning report for LearnerProfileId {ProfileId}", profile.Id);
                }
            }

            return (processed, succeeded, failed, skipped);
        }, cancellationToken);
    }

    private static DateTime GetWeekStart(DateTime date)
    {
        var dayOfWeek = (int)date.DayOfWeek;
        var daysSinceMonday = (dayOfWeek == 0) ? 6 : dayOfWeek - 1; // Sunday=0 -> 6 days back
        return date.Date.AddDays(-daysSinceMonday);
    }

    private async Task<int> CalculateWeeklyStreakAsync(int profileId, DateTime weekStart, DateTime weekEnd, CancellationToken cancellationToken)
    {
        var progressDates = await _context.LearnerProgresses
            .Where(lp => lp.LearnerProfileId == profileId && lp.IsCompleted && lp.CompletedAt >= weekStart && lp.CompletedAt < weekEnd)
            .Select(lp => lp.CompletedAt!.Value.Date)
            .ToListAsync(cancellationToken);

        var quizDates = await _context.QuizAttempts
            .Where(qa => qa.LearnerProfileId == profileId && qa.AttemptedAt >= weekStart && qa.AttemptedAt < weekEnd)
            .Select(qa => qa.AttemptedAt.Date)
            .ToListAsync(cancellationToken);

        var allDates = progressDates.Concat(quizDates).Distinct().OrderByDescending(d => d).ToList();
        if (!allDates.Any()) return 0;

        int maxStreak = 0;
        int currentStreak = 0;
        DateTime? previousDate = null;

        foreach (var date in allDates)
        {
            if (previousDate == null)
            {
                currentStreak = 1;
            }
            else if ((previousDate.Value - date).TotalDays == 1)
            {
                currentStreak++;
            }
            else
            {
                if (currentStreak > maxStreak) maxStreak = currentStreak;
                currentStreak = 1;
            }
            previousDate = date;
        }

        if (currentStreak > maxStreak) maxStreak = currentStreak;
        return maxStreak;
    }
}
