using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using AdaptiveLearning.Contracts.Events;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Infrastructure.Persistence;

namespace AdaptiveLearning.Worker.Handlers;

public class NotificationCreatedEventHandler : IEventHandler<NotificationCreatedEvent>
{
    private readonly AppDbContext _context;
    private readonly INotificationService _notificationService;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<NotificationCreatedEventHandler> _logger;

    public NotificationCreatedEventHandler(
        AppDbContext context,
        INotificationService notificationService,
        IEmailSender emailSender,
        ILogger<NotificationCreatedEventHandler> logger)
    {
        _context = context;
        _notificationService = notificationService;
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task HandleAsync(NotificationCreatedEvent ev)
    {
        if (ev == null) throw new ArgumentNullException(nameof(ev));

        _logger.LogInformation("NotificationCreatedEventHandler: Handling notification {NotificationId} for user {UserId}",
            ev.NotificationId, ev.UserId);

        // 1. Load the notification from the database to check current status
        var notification = await _context.Notifications.FindAsync(ev.NotificationId);
        if (notification == null)
        {
            _logger.LogWarning("NotificationCreatedEventHandler: Notification {NotificationId} not found in database.", ev.NotificationId);
            return;
        }

        // Replay safety: if already Sent, do not resend
        if (notification.Status == NotificationStatus.Sent)
        {
            _logger.LogInformation("NotificationCreatedEventHandler: Notification {NotificationId} is already marked as Sent. Skipping delivery.", ev.NotificationId);
            return;
        }

        bool hasInApp = notification.Channel == NotificationChannel.InApp || notification.Channel == NotificationChannel.InAppAndEmail;
        bool hasEmail = notification.Channel == NotificationChannel.Email || notification.Channel == NotificationChannel.InAppAndEmail;

        // 2. Deliver In-App channel (immediate, local DB only)
        if (hasInApp)
        {
            var alreadySentInApp = await _context.NotificationDeliveryAttempts
                .AnyAsync(a => a.NotificationId == ev.NotificationId && a.Channel == NotificationChannel.InApp && a.Status == NotificationStatus.Sent);

            if (!alreadySentInApp)
            {
                await _notificationService.RecordDeliveryAttemptAsync(
                    ev.NotificationId,
                    NotificationChannel.InApp,
                    NotificationStatus.Sent,
                    null);
                _logger.LogInformation("NotificationCreatedEventHandler: In-app delivery recorded for notification {NotificationId}", ev.NotificationId);
            }
        }

        // 3. Deliver Email channel
        if (hasEmail)
        {
            var emailAttempts = await _context.NotificationDeliveryAttempts
                .Where(a => a.NotificationId == ev.NotificationId && a.Channel == NotificationChannel.Email)
                .ToListAsync();

            var alreadySentEmail = emailAttempts.Any(a => a.Status == NotificationStatus.Sent);
            if (alreadySentEmail)
            {
                _logger.LogInformation("NotificationCreatedEventHandler: Email was already successfully sent for notification {NotificationId}.", ev.NotificationId);
                return;
            }

            var attemptCount = emailAttempts.Count;
            if (attemptCount >= 3)
            {
                _logger.LogWarning("NotificationCreatedEventHandler: Email delivery reached max attempts (3) for notification {NotificationId}.", ev.NotificationId);
                await _notificationService.RecordDeliveryAttemptAsync(
                    ev.NotificationId,
                    NotificationChannel.Email,
                    NotificationStatus.Failed,
                    "Reached maximum email delivery attempts (3).",
                    default);
                return;
            }

            // Load user's email address
            var user = await _context.Users.FindAsync(ev.UserId);
            if (user == null || string.IsNullOrWhiteSpace(user.Email))
            {
                _logger.LogWarning("NotificationCreatedEventHandler: User {UserId} or email not found for notification {NotificationId}.", ev.UserId, ev.NotificationId);
                await _notificationService.RecordDeliveryAttemptAsync(
                    ev.NotificationId,
                    NotificationChannel.Email,
                    NotificationStatus.Failed,
                    "Recipient user not found or email address is empty.",
                    default);
                return;
            }

            // Attempt email delivery
            var emailMsg = new EmailMessage
            {
                ToAddress = user.Email,
                Subject = ev.Title,
                Body = ev.Message,
                IsHtml = true
            };

            var sendResult = await _emailSender.SendEmailAsync(emailMsg, default);

            if (sendResult.Success)
            {
                await _notificationService.RecordDeliveryAttemptAsync(
                    ev.NotificationId,
                    NotificationChannel.Email,
                    NotificationStatus.Sent,
                    null,
                    default);
                _logger.LogInformation("NotificationCreatedEventHandler: Email sent successfully for notification {NotificationId}.", ev.NotificationId);
            }
            else
            {
                // Record failed attempt
                await _notificationService.RecordDeliveryAttemptAsync(
                    ev.NotificationId,
                    NotificationChannel.Email,
                    NotificationStatus.Failed,
                    sendResult.ErrorMessage ?? "Unknown SMTP error",
                    default);

                // Throw if we can retry
                if (attemptCount + 1 < 3)
                {
                    _logger.LogWarning("NotificationCreatedEventHandler: Email delivery failed (attempt {Attempt}). Throwing exception to trigger retry.", attemptCount + 1);
                    throw new Exception($"Email delivery failed: {sendResult.ErrorMessage}. Retrying...");
                }
                else
                {
                    _logger.LogError("NotificationCreatedEventHandler: Email delivery failed on final attempt (3) for notification {NotificationId}.", ev.NotificationId);
                }
            }
        }
    }
}
