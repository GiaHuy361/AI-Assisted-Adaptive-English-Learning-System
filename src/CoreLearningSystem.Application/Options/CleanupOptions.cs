namespace CoreLearningSystem.Application.Options;

public class CleanupOptions
{
    public const string Position = "Cleanup";

    public int NotificationAttemptRetentionDays { get; set; } = 30;
    public int EventLogRetentionDays { get; set; } = 30;
    public int JobLogRetentionDays { get; set; } = 30;
    public int FailedNotificationRetentionDays { get; set; } = 90;
}
