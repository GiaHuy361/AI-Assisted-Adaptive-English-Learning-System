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
    public string SkillMatrixRecalculationCron { get; set; } = "0 4 * * 1"; // Weekly on Mondays at 4:00 AM UTC
    public string SessionCleanupCron { get; set; } = "*/15 * * * *"; // Every 15 minutes
    public string RecommendationEffectivenessCron { get; set; } = "0 */2 * * *"; // Every 2 hours
    public string RecommendationRegenerationCron { get; set; } = "0 */3 * * *"; // Every 3 hours
    public string RecommendationStatisticsCron { get; set; } = "0 0 * * *"; // Daily at midnight UTC
    public string OutboxPublisherCron { get; set; } = "* * * * *"; // Every minute (reliable polling)

    public bool EnableLearningReminder { get; set; } = true;
    public bool EnableWeeklyReport { get; set; } = true;
    public bool EnableGoalTracking { get; set; } = true;
    public bool EnableAchievementChecking { get; set; } = true;
    public bool EnableSkillDecay { get; set; } = true;
    public bool EnableCleanup { get; set; } = true;
    public bool EnableSkillMatrixRecalculation { get; set; } = true;
    public bool EnableSessionCleanup { get; set; } = true;
    public bool EnableRecommendationEffectiveness { get; set; } = true;
    public bool EnableRecommendationRegeneration { get; set; } = true;
    public bool EnableRecommendationStatistics { get; set; } = true;
    public bool EnableOutboxPublisher { get; set; } = true;
}
