using System;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Domain.Entities;

public class BackgroundJobExecution
{
    public int Id { get; set; }
    public string JobName { get; set; } = string.Empty;
    public string ExecutionId { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Running;
    public int ProcessedCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public int SkippedCount { get; set; }
    public string? ErrorMessage { get; set; }
    public double DurationMilliseconds { get; set; }
    public string TriggerType { get; set; } = "Scheduled"; // Scheduled or Manual
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
