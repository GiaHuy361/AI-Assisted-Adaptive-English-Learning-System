using System;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Application.DTOs.Common;

public class CreateNotificationRequest
{
    public int UserId { get; set; }
    public int? LearnerProfileId { get; set; }
    public NotificationType Type { get; set; }
    public NotificationChannel Channel { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? SourceType { get; set; }
    public string? SourceId { get; set; }
    public string? SourceEventId { get; set; }
}

public class NotificationDetailsDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public NotificationType Type { get; set; }
    public NotificationStatus Status { get; set; }
    public NotificationChannel Channel { get; set; }
    public string? SourceType { get; set; }
    public string? SourceId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime? FailedAt { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
}
