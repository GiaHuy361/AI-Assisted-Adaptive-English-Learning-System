namespace CoreLearningSystem.Application.Options;

public class JobScheduleOptions
{
    public const string Position = "JobSchedule";

    // Defaults represent typical intervals (standard Cron format)
    public string LearningReminderCron { get; set; } = "0 9 * * *"; // Daily at 9:00 AM UTC
    public string WeeklyReportCron { get; set; } = "0 10 * * 1"; // Monday at 10:00 AM UTC
    public string GoalTrackingCron { get; set; } = "0 0 * * *"; // Daily at midnight UTC
    public string AchievementCheckingCron { get; set; } = "0 */4 * * *"; // Every 4 hours UTC
    public string SkillDecayCron { get; set; } = "0 2 * * *"; // Daily at 2:00 AM UTC
    public string CleanupCron { get; set; } = "0 3 * * 0"; // Sunday at 3:00 AM UTC

    public bool EnableLearningReminder { get; set; } = true;
    public bool EnableWeeklyReport { get; set; } = true;
    public bool EnableGoalTracking { get; set; } = true;
    public bool EnableAchievementChecking { get; set; } = true;
    public bool EnableSkillDecay { get; set; } = true;
    public bool EnableCleanup { get; set; } = true;
}
