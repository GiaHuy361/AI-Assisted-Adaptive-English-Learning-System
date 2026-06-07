using System;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Domain.Entities;

public class NotificationDeliveryAttempt
{
    public int Id { get; set; }
    public int NotificationId { get; set; }
    public NotificationChannel Channel { get; set; }
    public int AttemptNumber { get; set; }
    public NotificationStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    // Navigation Properties
    public Notification Notification { get; set; } = null!;
}
