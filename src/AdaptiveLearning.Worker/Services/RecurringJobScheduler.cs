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
    }
}
