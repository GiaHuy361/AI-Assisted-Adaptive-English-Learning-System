using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Application.Interfaces;

public interface INotificationService
{
    Task<NotificationDetailsDto?> CreateNotificationAsync(CreateNotificationRequest request, CancellationToken cancellationToken = default);
    Task<bool> MarkAsReadAsync(int notificationId, int userId, CancellationToken cancellationToken = default);
    Task<bool> MarkAllAsReadAsync(int userId, CancellationToken cancellationToken = default);
    Task<int> GetUnreadCountAsync(int userId, CancellationToken cancellationToken = default);
    Task<IEnumerable<NotificationDetailsDto>> GetUserNotificationsAsync(int userId, int page = 1, int pageSize = 10, CancellationToken cancellationToken = default);
    Task<bool> RecordDeliveryAttemptAsync(int notificationId, NotificationChannel channel, NotificationStatus status, string? errorMessage, CancellationToken cancellationToken = default);
}
