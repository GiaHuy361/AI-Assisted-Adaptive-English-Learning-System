using System;
using System.Threading;
using CoreLearningSystem.Application.Options;
using CoreLearningSystem.Infrastructure.Services;
using Hangfire;

namespace AdaptiveLearning.Worker.Services;

public static class RecurringJobScheduler
{
    public static void ScheduleJobs(IRecurringJobManager jobManager, JobScheduleOptions options)
    {
        if (jobManager == null) throw new ArgumentNullException(nameof(jobManager));
        if (options == null) throw new ArgumentNullException(nameof(options));

        // 1. Learning Reminder
        if (options.EnableLearningReminder)
        {
            jobManager.AddOrUpdate<LearningReminderJob>(
                "learning-reminder", 
                job => job.RunAsync(CancellationToken.None), 
                options.LearningReminderCron);
        }
        else
        {
            jobManager.RemoveIfExists("learning-reminder");
        }

        // 2. Weekly Report
        if (options.EnableWeeklyReport)
        {
            jobManager.AddOrUpdate<WeeklyLearningReportJob>(
                "weekly-learning-report", 
                job => job.RunAsync(CancellationToken.None), 
                options.WeeklyReportCron);
        }
        else
        {
            jobManager.RemoveIfExists("weekly-learning-report");
        }

        // 3. Goal Status Tracking
        if (options.EnableGoalTracking)
        {
            jobManager.AddOrUpdate<GoalStatusTrackingJob>(
                "goal-status-tracking", 
                job => job.RunAsync(CancellationToken.None), 
                options.GoalTrackingCron);
        }
        else
        {
            jobManager.RemoveIfExists("goal-status-tracking");
        }

        // 4. Achievement Checking
        if (options.EnableAchievementChecking)
        {
            jobManager.AddOrUpdate<AchievementCheckingJob>(
                "achievement-checking", 
                job => job.RunAsync(CancellationToken.None), 
                options.AchievementCheckingCron);
        }
        else
        {
            jobManager.RemoveIfExists("achievement-checking");
        }

        // 5. Skill Decay
        if (options.EnableSkillDecay)
        {
            jobManager.AddOrUpdate<SkillDecayJob>(
                "skill-decay", 
                job => job.RunAsync(CancellationToken.None), 
                options.SkillDecayCron);
        }
        else
        {
            jobManager.RemoveIfExists("skill-decay");
        }

        // 6. Cleanup
        if (options.EnableCleanup)
        {
            jobManager.AddOrUpdate<CleanupJob>(
                "cleanup", 
                job => job.RunAsync(CancellationToken.None), 
                options.CleanupCron);
        }
        else
        {
            jobManager.RemoveIfExists("cleanup");
        }

        // 7. Skill Matrix Recalculation
        if (options.EnableSkillMatrixRecalculation)
        {
            jobManager.AddOrUpdate<SkillMatrixRecalculationJob>(
                "skill-matrix-recalculation",
                job => job.RunAsync(CancellationToken.None),
                options.SkillMatrixRecalculationCron);
        }
        else
        {
            jobManager.RemoveIfExists("skill-matrix-recalculation");
        }

        // 8. Session Cleanup
        if (options.EnableSessionCleanup)
        {
            jobManager.AddOrUpdate<UserSessionCleanupJob>(
                "session-cleanup",
                job => job.RunAsync(CancellationToken.None),
                options.SessionCleanupCron);
        }
        else
        {
            jobManager.RemoveIfExists("session-cleanup");
        }

        // 9. Recommendation Effectiveness
        if (options.EnableRecommendationEffectiveness)
        {
            jobManager.AddOrUpdate<RecommendationEffectivenessJob>(
                "recommendation-effectiveness",
                job => job.RunAsync(CancellationToken.None),
                options.RecommendationEffectivenessCron);
        }
        else
        {
            jobManager.RemoveIfExists("recommendation-effectiveness");
        }

        // 10. Recommendation Regeneration
        if (options.EnableRecommendationRegeneration)
        {
            jobManager.AddOrUpdate<RecommendationRegenerationJob>(
                "recommendation-regeneration",
                job => job.RunAsync(CancellationToken.None),
                options.RecommendationRegenerationCron);
        }
        else
        {
            jobManager.RemoveIfExists("recommendation-regeneration");
        }

        // 11. Recommendation Statistics
        if (options.EnableRecommendationStatistics)
        {
            jobManager.AddOrUpdate<RecommendationStatisticsJob>(
                "recommendation-statistics",
                job => job.RunAsync(CancellationToken.None),
                options.RecommendationStatisticsCron);
        }
        else
        {
            jobManager.RemoveIfExists("recommendation-statistics");
        }

        // 12. Outbox Publisher
        if (options.EnableOutboxPublisher)
        {
            jobManager.AddOrUpdate<OutboxPublisherJob>(
                "outbox-publisher",
                job => job.RunAsync(CancellationToken.None),
                options.OutboxPublisherCron);
        }
        else
        {
            jobManager.RemoveIfExists("outbox-publisher");
        }
    }
}
