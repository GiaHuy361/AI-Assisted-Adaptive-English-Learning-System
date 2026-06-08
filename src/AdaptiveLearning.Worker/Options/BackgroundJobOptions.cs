namespace AdaptiveLearning.Worker.Options;

public class BackgroundJobOptions
{
    public const string Position = "BackgroundJob";

    public bool EnableJobs { get; set; } = true;
    public double ReminderIntervalHours { get; set; } = 24.0;
    public string WeeklyReportCron { get; set; } = "0 0 * * 0"; // Sunday at midnight
    public string CleanupCron { get; set; } = "0 0 1 * *"; // First day of the month at midnight
}
