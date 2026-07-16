using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Infrastructure.Persistence;
using CoreLearningSystem.Infrastructure.Services;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Application.DTOs.Common;
using AdaptiveLearning.Worker.Handlers;
using AdaptiveLearning.Contracts.Events;

namespace AdaptiveLearning.Tests;

public class NotificationEventFlowTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly NotificationService _notificationService;
    private readonly MockEmailSender _emailSender;

    public NotificationEventFlowTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var mockSignalR = new Moq.Mock<ISignalRService>().Object;
        _notificationService = new NotificationService(_context, new MockKafkaPublisher(), mockSignalR, new NullLogger<NotificationService>());
        _emailSender = new MockEmailSender();
    }

    [Fact]
    public async Task EventReplay_ShouldNotResend_IfAlreadySent()
    {
        // Arrange
        var user = new User { Id = 1, Username = "test", Email = "test@test.com" };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var notification = new Notification
        {
            UserId = 1,
            Title = "Welcome",
            Message = "Hello!",
            Type = NotificationType.System,
            Channel = NotificationChannel.Email,
            IdempotencyKey = "replay-key",
            Status = NotificationStatus.Sent,
            SentAt = DateTime.UtcNow
        };
        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        // Record a successful attempt
        _context.NotificationDeliveryAttempts.Add(new NotificationDeliveryAttempt
        {
            NotificationId = notification.Id,
            Channel = NotificationChannel.Email,
            AttemptNumber = 1,
            Status = NotificationStatus.Sent,
            AttemptedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var handler = new NotificationCreatedEventHandler(_context, _notificationService, _emailSender, new NullLogger<NotificationCreatedEventHandler>());
        var ev = new NotificationCreatedEvent
        {
            NotificationId = notification.Id,
            UserId = 1,
            NotificationType = "System",
            Channel = "Email",
            Title = "Welcome",
            Message = "Hello!"
        };

        // Act
        await handler.HandleAsync(ev);

        // Assert
        Assert.Equal(0, _emailSender.SendCount); // Should not call send again
        var attemptsCount = await _context.NotificationDeliveryAttempts.CountAsync(a => a.NotificationId == notification.Id);
        Assert.Equal(1, attemptsCount); // No new attempts added
    }

    [Fact]
    public async Task FailedEmail_ShouldThrowUntilMaxAttempts_ThenRemainFailedWithoutThrowing()
    {
        // Arrange
        var user = new User { Id = 2, Username = "test2", Email = "test2@test.com" };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var created = await _notificationService.CreateNotificationAsync(new CreateNotificationRequest
        {
            UserId = 2,
            Type = NotificationType.System,
            Channel = NotificationChannel.Email,
            Title = "Alert",
            Message = "Failed mail test",
            IdempotencyKey = "fail-retry-key"
        });
        Assert.NotNull(created);

        _emailSender.ShouldSucceed = false; // Program email sender to fail
        var handler = new NotificationCreatedEventHandler(_context, _notificationService, _emailSender, new NullLogger<NotificationCreatedEventHandler>());
        var ev = new NotificationCreatedEvent
        {
            NotificationId = created.Id,
            UserId = 2,
            NotificationType = "System",
            Channel = "Email",
            Title = "Alert",
            Message = "Failed mail test"
        };

        // Act & Assert

        // 1st Attempt: Should fail and throw exception
        var ex1 = await Assert.ThrowsAsync<Exception>(() => handler.HandleAsync(ev));
        Assert.Contains("Email delivery failed", ex1.Message);

        var attemptsAfter1 = await _context.NotificationDeliveryAttempts.CountAsync(a => a.NotificationId == created.Id);
        Assert.Equal(1, attemptsAfter1);

        // 2nd Attempt: Should fail and throw exception
        var ex2 = await Assert.ThrowsAsync<Exception>(() => handler.HandleAsync(ev));
        Assert.Contains("Email delivery failed", ex2.Message);

        var attemptsAfter2 = await _context.NotificationDeliveryAttempts.CountAsync(a => a.NotificationId == created.Id);
        Assert.Equal(2, attemptsAfter2);

        // 3rd Attempt: Should fail and NOT throw exception (reaches max attempts)
        await handler.HandleAsync(ev);

        var attemptsAfter3 = await _context.NotificationDeliveryAttempts.CountAsync(a => a.NotificationId == created.Id);
        Assert.Equal(3, attemptsAfter3);

        // DB Status should be Failed
        var finalNotif = await _context.Notifications.FindAsync(created.Id);
        Assert.NotNull(finalNotif);
        Assert.Equal(NotificationStatus.Failed, finalNotif.Status);
        Assert.Equal(2, finalNotif.RetryCount); // 2 retries, 3 total attempts
    }

    [Fact]
    public async Task InAppAndEmail_WithDisabledEmail_ShouldRecordInAppAsSent_AndEmailAsFailed()
    {
        // Arrange
        var user = new User { Id = 3, Username = "test3", Email = "test3@test.com" };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var created = await _notificationService.CreateNotificationAsync(new CreateNotificationRequest
        {
            UserId = 3,
            Type = NotificationType.System,
            Channel = NotificationChannel.InAppAndEmail,
            Title = "Alert In-app + email",
            Message = "Email disabled test",
            IdempotencyKey = "inapp-email-key"
        });
        Assert.NotNull(created);

        // Simulate SMTP being disabled by having it fail
        _emailSender.ShouldSucceed = false;

        var handler = new NotificationCreatedEventHandler(_context, _notificationService, _emailSender, new NullLogger<NotificationCreatedEventHandler>());
        var ev = new NotificationCreatedEvent
        {
            NotificationId = created.Id,
            UserId = 3,
            NotificationType = "System",
            Channel = "InAppAndEmail",
            Title = "Alert In-app + email",
            Message = "Email disabled test"
        };

        // Act & Assert
        // First run: Should record In-App as Sent, Email as Failed, and throw for Email retry
        await Assert.ThrowsAsync<Exception>(() => handler.HandleAsync(ev));

        var attempts = await _context.NotificationDeliveryAttempts.Where(a => a.NotificationId == created.Id).ToListAsync();
        Assert.Equal(2, attempts.Count); // 1 InApp attempt + 1 Email attempt

        var inAppAttempt = attempts.FirstOrDefault(a => a.Channel == NotificationChannel.InApp);
        Assert.NotNull(inAppAttempt);
        Assert.Equal(NotificationStatus.Sent, inAppAttempt.Status); // In-app delivery succeeds

        var emailAttempt = attempts.FirstOrDefault(a => a.Channel == NotificationChannel.Email);
        Assert.NotNull(emailAttempt);
        Assert.Equal(NotificationStatus.Failed, emailAttempt.Status); // Email delivery fails
    }

    private class MockEmailSender : IEmailSender
    {
        public int SendCount { get; private set; }
        public bool ShouldSucceed { get; set; } = true;
        public string? ErrorMessage { get; set; } = "SMTP Error";

        public Task<EmailSendResult> SendEmailAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            SendCount++;
            if (ShouldSucceed)
            {
                return Task.FromResult(new EmailSendResult { Success = true, MessageId = Guid.NewGuid().ToString() });
            }
            return Task.FromResult(new EmailSendResult { Success = false, ErrorMessage = ErrorMessage });
        }
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
