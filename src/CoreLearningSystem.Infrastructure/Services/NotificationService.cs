using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Infrastructure.Persistence;
using AdaptiveLearning.Contracts.Events;
using AdaptiveLearning.Contracts.Topics;

namespace CoreLearningSystem.Infrastructure.Services;

/// <summary>
/// Service to manage notifications, delivery history, and Kafka event publishing.
/// </summary>
/// <remarks>
/// Note: This service uses a dual-write pattern (DB insert followed by Kafka event dispatch).
/// Due to the absence of a transactional Outbox pattern, a failure in publishing the event
/// will log an error but will not roll back the database commit, leaving recommendations valid.
/// </remarks>
public class NotificationService : INotificationService
{
    private readonly AppDbContext _context;
    private readonly IKafkaPublisher _kafkaPublisher;
    private readonly ISignalRService _signalRService;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        AppDbContext context,
        IKafkaPublisher kafkaPublisher,
        ISignalRService signalRService,
        ILogger<NotificationService> logger)
    {
        _context = context;
        _kafkaPublisher = kafkaPublisher;
        _signalRService = signalRService;
        _logger = logger;
    }

    public async Task<NotificationDetailsDto?> CreateNotificationAsync(CreateNotificationRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        // 1. Idempotency Check
        var existing = await _context.Notifications
            .FirstOrDefaultAsync(n => n.IdempotencyKey == request.IdempotencyKey, cancellationToken);

        if (existing != null)
        {
            _logger.LogInformation("Notification already exists for IdempotencyKey: {Key}. Returning existing record.", request.IdempotencyKey);
            return MapToDetailsDto(existing);
        }

        // 2. Insert new notification
        var notification = new Notification
        {
            UserId = request.UserId,
            Title = request.Title,
            Message = request.Message,
            IsRead = false,
            Type = request.Type,
            Status = NotificationStatus.Pending,
            Channel = request.Channel,
            IdempotencyKey = request.IdempotencyKey,
            SourceType = request.SourceType,
            SourceId = request.SourceId,
            SourceEventId = request.SourceEventId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync(cancellationToken);

        // 3. Publish NotificationCreatedEvent
        var eventObj = new NotificationCreatedEvent
        {
            NotificationId = notification.Id,
            UserId = notification.UserId,
            LearnerProfileId = request.LearnerProfileId,
            NotificationType = notification.Type.ToString(),
            Channel = notification.Channel.ToString(),
            Title = notification.Title,
            Message = notification.Message,
            SourceType = notification.SourceType,
            SourceId = notification.SourceId,
            CreatedAt = notification.CreatedAt
        };

        try
        {
            await _kafkaPublisher.PublishAsync(TopicNames.NotificationCreated, notification.Id.ToString(), eventObj);
            _logger.LogInformation("NotificationCreatedEvent published for NotificationId: {Id}.", notification.Id);
        }
        catch (Exception ex)
        {
            // Logging failure safely without failing the core business recommendation or notification save.
            _logger.LogError(ex, "Failed to publish NotificationCreatedEvent for NotificationId: {Id} (Dual-write limitation).", notification.Id);
        }

        var dto = MapToDetailsDto(notification);

        // Send via SignalR
        try
        {
            await _signalRService.SendNotificationAsync(notification.UserId, dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send real-time notification via SignalR for NotificationId: {Id}.", notification.Id);
        }

        return dto;
    }

    public async Task<bool> MarkAsReadAsync(int notificationId, int userId, CancellationToken cancellationToken = default)
    {
        var notification = await _context.Notifications.FindAsync(new object[] { notificationId }, cancellationToken);
        if (notification == null || notification.UserId != userId)
        {
            return false;
        }

        notification.IsRead = true;
        notification.ReadAt = DateTime.UtcNow;
        notification.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> MarkAllAsReadAsync(int userId, CancellationToken cancellationToken = default)
    {
        var unread = await _context.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync(cancellationToken);

        foreach (var n in unread)
        {
            n.IsRead = true;
            n.ReadAt = DateTime.UtcNow;
            n.UpdatedAt = DateTime.UtcNow;
        }

        if (unread.Any())
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task<int> GetUnreadCountAsync(int userId, CancellationToken cancellationToken = default)
    {
        return await _context.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, cancellationToken);
    }

    public async Task<IEnumerable<NotificationDetailsDto>> GetUserNotificationsAsync(int userId, int page = 1, int pageSize = 10, CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;

        var list = await _context.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return list.Select(MapToDetailsDto);
    }

    public async Task<bool> RecordDeliveryAttemptAsync(int notificationId, NotificationChannel channel, NotificationStatus status, string? errorMessage, CancellationToken cancellationToken = default)
    {
        var notification = await _context.Notifications.FindAsync(new object[] { notificationId }, cancellationToken);
        if (notification == null) return false;

        var attemptNumber = await _context.NotificationDeliveryAttempts
            .CountAsync(a => a.NotificationId == notificationId, cancellationToken) + 1;

        var attempt = new NotificationDeliveryAttempt
        {
            NotificationId = notificationId,
            Channel = channel,
            AttemptNumber = attemptNumber,
            Status = status,
            ErrorMessage = errorMessage,
            AttemptedAt = DateTime.UtcNow,
            CompletedAt = status == NotificationStatus.Sent || status == NotificationStatus.Failed ? DateTime.UtcNow : null
        };

        _context.NotificationDeliveryAttempts.Add(attempt);

        // Update notification status & logs
        notification.Status = status;
        notification.UpdatedAt = DateTime.UtcNow;
        notification.RetryCount = attemptNumber - 1; // RetryCount starts at 0 for first attempt

        if (status == NotificationStatus.Sent)
        {
            notification.SentAt = DateTime.UtcNow;
            notification.LastError = null;
        }
        else if (status == NotificationStatus.Failed)
        {
            notification.FailedAt = DateTime.UtcNow;
            notification.LastError = errorMessage;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static NotificationDetailsDto MapToDetailsDto(Notification n)
    {
        return new NotificationDetailsDto
        {
            Id = n.Id,
            UserId = n.UserId,
            Title = n.Title,
            Message = n.Message,
            IsRead = n.IsRead,
            Type = n.Type,
            Status = n.Status,
            Channel = n.Channel,
            SourceType = n.SourceType,
            SourceId = n.SourceId,
            CreatedAt = n.CreatedAt,
            SentAt = n.SentAt,
            ReadAt = n.ReadAt,
            FailedAt = n.FailedAt,
            RetryCount = n.RetryCount,
            LastError = n.LastError
        };
    }
}
