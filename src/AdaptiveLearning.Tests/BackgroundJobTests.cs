using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Infrastructure.Persistence;
using CoreLearningSystem.Infrastructure.Services;
using CoreLearningSystem.Application.Options;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.Interfaces;
using Hangfire;

namespace AdaptiveLearning.Tests;

public class BackgroundJobTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly BackgroundJobExecutor _executor;
    private readonly NotificationService _notificationService;

    public BackgroundJobTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _executor = new BackgroundJobExecutor(_context, new NullLogger<BackgroundJobExecutor>());
        _notificationService = new NotificationService(_context, new MockKafkaPublisher(), new NullLogger<NotificationService>());
    }

    [Fact]
    public async Task WeeklyReportJob_BoundaryTest_ShouldOnlyAggregatePreviousCompletedWeek()
    {
        // Arrange
        var now = new DateTime(2026, 6, 7, 12, 0, 0, DateTimeKind.Utc); // Sunday
        var currentMonday = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var prevMonday = currentMonday.AddDays(-7); // 2026-05-25
        
        var profile = new LearnerProfile
        {
            Id = 1,
            UserId = 1,
            Level = EnglishLevel.A1,
            ActivityStatus = ActivityStatus.Active,
            User = new User { Id = 1, Username = "test", Email = "test@test.com" }
        };
        _context.LearnerProfiles.Add(profile);

        // Seed lessons to satisfy foreign keys
        _context.Lessons.Add(new Lesson { Id = 201, Title = "Lesson 201", Level = EnglishLevel.A1 });
        _context.Lessons.Add(new Lesson { Id = 202, Title = "Lesson 202", Level = EnglishLevel.A1 });
        _context.Lessons.Add(new Lesson { Id = 203, Title = "Lesson 203", Level = EnglishLevel.A1 });
        await _context.SaveChangesAsync();

        var testNow = DateTime.UtcNow;
        var testCurrentMonday = testNow.Date.AddDays(-((int)testNow.DayOfWeek == 0 ? 6 : (int)testNow.DayOfWeek - 1));
        var testPrevMonday = testCurrentMonday.AddDays(-7);

        // Add relative to actual now
        _context.LearnerProgresses.Add(new LearnerProgress
        {
            LearnerProfileId = 1,
            LessonId = 201,
            IsCompleted = true,
            CompletedAt = testPrevMonday.AddDays(2) // Within prev week
        });
        _context.LearnerProgresses.Add(new LearnerProgress
        {
            LearnerProfileId = 1,
            LessonId = 202,
            IsCompleted = true,
            CompletedAt = testPrevMonday.AddDays(-2) // Before prev week
        });
        _context.LearnerProgresses.Add(new LearnerProgress
        {
            LearnerProfileId = 1,
            LessonId = 203,
            IsCompleted = true,
            CompletedAt = testCurrentMonday.AddDays(1) // Current week
        });

        await _context.SaveChangesAsync();

        var job = new WeeklyLearningReportJob(_context, _notificationService, _executor, new NullLogger<WeeklyLearningReportJob>());

        // Run the job
        await job.RunAsync(CancellationToken.None);

        // Assert
        var report = await _context.WeeklyLearningReports.FirstOrDefaultAsync(r => r.LearnerProfileId == 1 && r.WeekStart == testPrevMonday);
        Assert.NotNull(report);
        Assert.Equal(1, report.LessonsCompleted); // Only the 1 within the prev week
        Assert.Equal(testPrevMonday, report.WeekStart);
        Assert.Equal(testCurrentMonday, report.WeekEnd);
    }

    [Fact]
    public async Task SkillDecayJob_Idempotency_ShouldNotDecaySkillTwiceInSamePeriod()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var lastActivity = now.AddDays(-40); // Inactive for 40 days

        var profile = new LearnerProfile
        {
            Id = 1,
            UserId = 1,
            Level = EnglishLevel.A1,
            ActivityStatus = ActivityStatus.Active,
            User = new User { Id = 1, Username = "test", Email = "test@test.com" }
        };
        _context.LearnerProfiles.Add(profile);

        var matrix = new SkillMatrix
        {
            Id = 1,
            LearnerProfileId = 1,
            Skill = SkillType.Listening,
            CurrentScore = 80.0,
            MasteryLevel = MasteryLevel.Good,
            CreatedAt = lastActivity
        };
        _context.SkillMatrices.Add(matrix);

        // Add history record for last activity
        _context.SkillMatrixHistories.Add(new SkillMatrixHistory
        {
            SkillMatrixId = 1,
            LearnerProfileId = 1,
            Skill = SkillType.Listening,
            PreviousScore = 80.0,
            AssessmentScore = 80.0,
            NewScore = 80.0,
            SourceType = MatrixSourceType.PlacementTest,
            SourceId = 1,
            RecordedAt = lastActivity
        });

        await _context.SaveChangesAsync();

        var job = new SkillDecayJob(_context, _notificationService, _executor, new NullLogger<SkillDecayJob>());

        // Act - Run 1
        await job.RunAsync(CancellationToken.None);

        var scoreAfterFirstRun = (await _context.SkillMatrices.FindAsync(1))!.CurrentScore;

        // Act - Run 2 (Same day/period key)
        await job.RunAsync(CancellationToken.None);

        var scoreAfterSecondRun = (await _context.SkillMatrices.FindAsync(1))!.CurrentScore;

        // Assert
        Assert.Equal(78.0, scoreAfterFirstRun); // 80 - 2 (First decay)
        Assert.Equal(78.0, scoreAfterSecondRun); // Should NOT decay again, idempotency key checks blocked it

        var historyCount = await _context.SkillMatrixHistories.CountAsync(h => h.SkillMatrixId == 1 && h.SourceType == MatrixSourceType.SkillDecay);
        Assert.Equal(1, historyCount); // Only 1 history record written
    }

    [Fact]
    public async Task CleanupJob_ShouldRemoveJobLogsAndAttempts_ButNeverDeleteCoreHistory()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var options = Options.Create(new CleanupOptions
        {
            NotificationAttemptRetentionDays = 1,
            JobLogRetentionDays = 1,
            FailedNotificationRetentionDays = 1
        });

        // Seed user first to prevent foreign key errors
        var user = new User { Id = 1, Username = "test", Email = "test@test.com" };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // 1. Add Cleanup Candidates
        var oldDate = now.AddDays(-5);
        _context.BackgroundJobExecutions.Add(new BackgroundJobExecution
        {
            JobName = "cleanup-test-job",
            ExecutionId = "job1",
            StartedAt = oldDate,
            CompletedAt = oldDate,
            Status = JobStatus.Succeeded,
            TriggerType = "cron",
            CreatedAt = oldDate
        });

        var notification = new Notification
        {
            UserId = 1,
            Title = "Notif",
            Message = "Msg",
            Type = NotificationType.System,
            Channel = NotificationChannel.InApp,
            IdempotencyKey = "key1",
            Status = NotificationStatus.Sent
        };
        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        _context.NotificationDeliveryAttempts.Add(new NotificationDeliveryAttempt
        {
            NotificationId = notification.Id,
            Channel = NotificationChannel.InApp,
            AttemptNumber = 1,
            Status = NotificationStatus.Sent,
            AttemptedAt = oldDate,
            CompletedAt = oldDate
        });

        // 2. Add Protected Core History
        var profile = new LearnerProfile { Id = 1, UserId = 1, Level = EnglishLevel.A1, ActivityStatus = ActivityStatus.Active };
        _context.LearnerProfiles.Add(profile);

        _context.WeeklyLearningReports.Add(new WeeklyLearningReport
        {
            LearnerProfileId = 1,
            WeekStart = oldDate,
            WeekEnd = oldDate.AddDays(7),
            LessonsCompleted = 5,
            QuizzesCompleted = 2,
            AverageScore = 90.0,
            StrongestSkill = "Reading",
            WeakestSkill = "Listening",
            GoalProgressSummary = "[]",
            BadgesEarned = "[]",
            GeneratedAt = oldDate
        });

        await _context.SaveChangesAsync();

        var job = new CleanupJob(_context, options, _executor, new NullLogger<CleanupJob>());

        // Act
        await job.RunAsync(CancellationToken.None);

        // Assert
        // Safe candidate job log deleted
        var jobLogs = await _context.BackgroundJobExecutions.ToListAsync();
        Assert.DoesNotContain(jobLogs, e => e.ExecutionId == "job1");

        // Safe candidate attempt deleted
        var attempts = await _context.NotificationDeliveryAttempts.ToListAsync();
        Assert.Empty(attempts);

        // Core protected history remains intact
        var weeklyReports = await _context.WeeklyLearningReports.ToListAsync();
        Assert.Single(weeklyReports);
        Assert.Equal(1, weeklyReports[0].LearnerProfileId);
    }

    [Fact]
    public void ScheduleJobs_WithAllJobsEnabled_ShouldRegisterWithStableIds()
    {
        // Arrange
        var fakeJobManager = new FakeRecurringJobManager();
        var options = new JobScheduleOptions
        {
            EnableLearningReminder = true,
            LearningReminderCron = "0 0 * * *",
            EnableWeeklyReport = true,
            WeeklyReportCron = "0 0 * * *",
            EnableGoalTracking = true,
            GoalTrackingCron = "0 0 * * *",
            EnableAchievementChecking = true,
            AchievementCheckingCron = "0 0 * * *",
            EnableSkillDecay = true,
            SkillDecayCron = "0 0 * * *",
            EnableCleanup = true,
            CleanupCron = "0 0 * * *"
        };

        // Act
        AdaptiveLearning.Worker.Services.RecurringJobScheduler.ScheduleJobs(fakeJobManager, options);

        // Assert
        var expectedJobIds = new[]
        {
            "learning-reminder",
            "weekly-learning-report",
            "goal-status-tracking",
            "achievement-checking",
            "skill-decay",
            "cleanup"
        };

        Assert.Equal(expectedJobIds.Length, fakeJobManager.AddedOrUpdatedJobIds.Count);
        foreach (var id in expectedJobIds)
        {
            Assert.Contains(id, fakeJobManager.AddedOrUpdatedJobIds);
        }
        Assert.Empty(fakeJobManager.RemovedJobIds);
    }

    [Fact]
    public void ScheduleJobs_WithDisabledJobs_ShouldRemoveUnregisteredJobs()
    {
        // Arrange
        var fakeJobManager = new FakeRecurringJobManager();
        var options = new JobScheduleOptions
        {
            EnableLearningReminder = false,
            EnableWeeklyReport = true,
            WeeklyReportCron = "0 0 * * *",
            EnableGoalTracking = false,
            EnableAchievementChecking = true,
            AchievementCheckingCron = "0 0 * * *",
            EnableSkillDecay = false,
            EnableCleanup = true,
            CleanupCron = "0 0 * * *"
        };

        // Act
        AdaptiveLearning.Worker.Services.RecurringJobScheduler.ScheduleJobs(fakeJobManager, options);

        // Assert
        Assert.Contains("learning-reminder", fakeJobManager.RemovedJobIds);
        Assert.Contains("goal-status-tracking", fakeJobManager.RemovedJobIds);
        Assert.Contains("skill-decay", fakeJobManager.RemovedJobIds);

        Assert.Contains("weekly-learning-report", fakeJobManager.AddedOrUpdatedJobIds);
        Assert.Contains("achievement-checking", fakeJobManager.AddedOrUpdatedJobIds);
        Assert.Contains("cleanup", fakeJobManager.AddedOrUpdatedJobIds);
    }

    private class FakeRecurringJobManager : IRecurringJobManager
    {
        public List<string> AddedOrUpdatedJobIds { get; } = new();
        public List<string> RemovedJobIds { get; } = new();

        public void AddOrUpdate(string recurringJobId, Hangfire.Common.Job job, string cronExpression, RecurringJobOptions options)
        {
            AddedOrUpdatedJobIds.Add(recurringJobId);
        }

        public void RemoveIfExists(string recurringJobId)
        {
            RemovedJobIds.Add(recurringJobId);
        }

        public void Trigger(string recurringJobId)
        {
        }
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
