using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Infrastructure.Persistence;
using CoreLearningSystem.Infrastructure.Services;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.Interfaces;
using Moq;

namespace AdaptiveLearning.Tests;

public class NotificationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly NotificationService _service;

    public NotificationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        // Seed users to satisfy foreign key constraints
        _context.Users.Add(new User { Id = 1, Username = "test1", Email = "test1@test.com" });
        _context.Users.Add(new User { Id = 10, Username = "test10", Email = "test10@test.com" });
        _context.Users.Add(new User { Id = 20, Username = "test20", Email = "test20@test.com" });
        
        _context.LearnerProfiles.Add(new LearnerProfile { Id = 1, UserId = 1, Level = EnglishLevel.A1, ActivityStatus = ActivityStatus.Active });
        _context.LearnerProfiles.Add(new LearnerProfile { Id = 10, UserId = 10, Level = EnglishLevel.A1, ActivityStatus = ActivityStatus.Active });
        _context.LearnerProfiles.Add(new LearnerProfile { Id = 20, UserId = 20, Level = EnglishLevel.A1, ActivityStatus = ActivityStatus.Active });
        
        _context.SaveChanges();

        var mockPublisher = new MockKafkaPublisher();
        var mockSignalR = new Mock<ISignalRService>().Object;
        _service = new NotificationService(_context, mockPublisher, mockSignalR, new NullLogger<NotificationService>());
    }

    [Fact]
    public async Task CreateNotification_WithNewIdempotencyKey_ShouldSaveToDatabase()
    {
        // Arrange
        var req = new CreateNotificationRequest
        {
            UserId = 1,
            LearnerProfileId = 1,
            Type = NotificationType.LearningReminder,
            Channel = NotificationChannel.InApp,
            Title = "Reminder",
            Message = "Don't forget to study",
            IdempotencyKey = "key1",
            SourceType = "Test",
            SourceId = "1"
        };

        // Act
        var result = await _service.CreateNotificationAsync(req);

        // Assert
        Assert.NotNull(result);

        var persisted = await _context.Notifications.FindAsync(result.Id);
        Assert.NotNull(persisted);
        Assert.Equal("key1", persisted.IdempotencyKey);
        Assert.Equal(NotificationStatus.Pending, persisted.Status);
        Assert.False(persisted.IsRead);
        Assert.Null(persisted.ReadAt);
    }

    [Fact]
    public async Task CreateNotification_WithDuplicateIdempotencyKey_ShouldReturnExistingRecord()
    {
        // Arrange
        var req1 = new CreateNotificationRequest
        {
            UserId = 1,
            Type = NotificationType.LearningReminder,
            Channel = NotificationChannel.InApp,
            Title = "Reminder 1",
            Message = "Msg 1",
            IdempotencyKey = "dup-key"
        };
        var req2 = new CreateNotificationRequest
        {
            UserId = 1,
            Type = NotificationType.LearningReminder,
            Channel = NotificationChannel.InApp,
            Title = "Reminder 2",
            Message = "Msg 2",
            IdempotencyKey = "dup-key"
        };

        // Act
        var res1 = await _service.CreateNotificationAsync(req1);
        var res2 = await _service.CreateNotificationAsync(req2);

        // Assert
        Assert.NotNull(res1);
        Assert.NotNull(res2);
        Assert.Equal(res1.Id, res2.Id);
        Assert.Equal("Reminder 1", res2.Title); // First one wins
        
        var count = await _context.Notifications.CountAsync(n => n.IdempotencyKey == "dup-key");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Concurrency_CreateNotification_ConcurrentRequests_ShouldSucceedAndCreateOnlyOne()
    {
        // Arrange
        var key = "concurrent-key-" + Guid.NewGuid();
        var tasks = new List<Task<NotificationDetailsDto?>>();

        for (int i = 0; i < 10; i++)
        {
            var req = new CreateNotificationRequest
            {
                UserId = 1,
                Type = NotificationType.LearningReminder,
                Channel = NotificationChannel.InApp,
                Title = $"Title {i}",
                Message = $"Msg {i}",
                IdempotencyKey = key
            };
            tasks.Add(_service.CreateNotificationAsync(req));
        }

        // Act
        var results = await Task.WhenAll(tasks);

        // Assert
        var distinctIds = results.Where(r => r != null).Select(r => r!.Id).Distinct().ToList();
        Assert.Single(distinctIds); // All should map to the exact same notification ID

        var dbCount = await _context.Notifications.CountAsync(n => n.IdempotencyKey == key);
        Assert.Equal(1, dbCount);
    }

    [Fact]
    public async Task MarkAsRead_ShouldSyncIsReadAndReadAt_AndNotChangeDeliveryStatus()
    {
        // Arrange
        var req = new CreateNotificationRequest
        {
            UserId = 1,
            Type = NotificationType.LearningReminder,
            Channel = NotificationChannel.InApp,
            Title = "Title",
            Message = "Msg",
            IdempotencyKey = "read-test-key"
        };
        var created = await _service.CreateNotificationAsync(req);
        Assert.NotNull(created);

        // Update delivery status manually
        await _service.RecordDeliveryAttemptAsync(created.Id, NotificationChannel.InApp, NotificationStatus.Sent, null);

        // Act
        var success = await _service.MarkAsReadAsync(created.Id, 1);

        // Assert
        Assert.True(success);
        var db = await _context.Notifications.FindAsync(created.Id);
        Assert.NotNull(db);
        Assert.True(db.IsRead);
        Assert.NotNull(db.ReadAt);
        Assert.Equal(NotificationStatus.Sent, db.Status); // Delivery status remains Sent, not overwritten by Read
    }

    [Fact]
    public async Task MarkAllAsRead_ShouldSetAllUserNotificationsToRead()
    {
        // Arrange
        var req1 = new CreateNotificationRequest { UserId = 10, Type = NotificationType.System, Channel = NotificationChannel.InApp, Title = "T1", Message = "M1", IdempotencyKey = "k1" };
        var req2 = new CreateNotificationRequest { UserId = 10, Type = NotificationType.System, Channel = NotificationChannel.InApp, Title = "T2", Message = "M2", IdempotencyKey = "k2" };
        var req3 = new CreateNotificationRequest { UserId = 20, Type = NotificationType.System, Channel = NotificationChannel.InApp, Title = "T3", Message = "M3", IdempotencyKey = "k3" };

        await _service.CreateNotificationAsync(req1);
        await _service.CreateNotificationAsync(req2);
        await _service.CreateNotificationAsync(req3);

        // Act
        await _service.MarkAllAsReadAsync(10);

        // Assert
        var u10Notifications = await _context.Notifications.Where(n => n.UserId == 10).ToListAsync();
        Assert.All(u10Notifications, n => Assert.True(n.IsRead));
        Assert.All(u10Notifications, n => Assert.NotNull(n.ReadAt));

        var u20Notifications = await _context.Notifications.Where(n => n.UserId == 20).ToListAsync();
        Assert.All(u20Notifications, n => Assert.False(n.IsRead));
        Assert.All(u20Notifications, n => Assert.Null(n.ReadAt));
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
