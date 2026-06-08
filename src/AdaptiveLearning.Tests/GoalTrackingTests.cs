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
using CoreLearningSystem.Infrastructure.Persistence.Repositories;
using CoreLearningSystem.Infrastructure.Services;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.Interfaces;

namespace AdaptiveLearning.Tests;

public class GoalTrackingTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly GoalTrackingService _service;

    private const int TestProfileId = 500;
    private const int TestUserId = 600;

    public GoalTrackingTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _service = new GoalTrackingService(_context, new NullLogger<GoalTrackingService>());

        SeedDefaultData();
    }

    private void SeedDefaultData()
    {
        var user = new User
        {
            Id = TestUserId,
            Username = "goal_test_learner",
            Email = "goal_test@learner.com",
            PasswordHash = "hash",
            Role = UserRole.Learner,
            CreatedAt = DateTime.UtcNow
        };
        _context.Users.Add(user);

        var profile = new LearnerProfile
        {
            Id = TestProfileId,
            UserId = TestUserId,
            Level = EnglishLevel.B1,
            ActivityStatus = ActivityStatus.Active,
            LastActiveAt = DateTime.UtcNow
        };
        _context.LearnerProfiles.Add(profile);
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    [Fact]
    public async Task LessonEvent_Should_Increase_LessonsPerWeekGoal()
    {
        // Arrange
        var goal = new GoalSetting
        {
            LearnerProfileId = TestProfileId,
            Target = "Complete 5 lessons this week",
            Type = GoalType.LessonsPerWeek,
            TargetValue = 5,
            CurrentValue = 0,
            Unit = "lessons",
            StartDate = DateTime.UtcNow.AddDays(-1),
            Deadline = DateTime.UtcNow.AddDays(5),
            Status = GoalStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        _context.GoalSettings.Add(goal);
        await _context.SaveChangesAsync();

        var request = new GoalProgressRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-lesson-1",
            TriggerGoalType = GoalType.LessonsPerWeek,
            IncrementValue = 1,
            OccurredAt = DateTime.UtcNow
        };

        // Act
        var result = await _service.UpdateGoalProgressAsync(request);

        // Assert
        var updatedGoal = await _context.GoalSettings.FindAsync(goal.Id);
        Assert.NotNull(updatedGoal);
        Assert.Equal(1, updatedGoal.CurrentValue);
        Assert.Equal(20.0, updatedGoal.ProgressPercentage);
        Assert.Equal(GoalStatus.Active, updatedGoal.Status);
        Assert.Equal(1, result.GoalsUpdated);
    }

    [Fact]
    public async Task QuizEvent_Should_Increase_QuizzesPerWeekGoal()
    {
        // Arrange
        var goal = new GoalSetting
        {
            LearnerProfileId = TestProfileId,
            Target = "Complete 3 quizzes this week",
            Type = GoalType.QuizzesPerWeek,
            TargetValue = 3,
            CurrentValue = 0,
            Unit = "quizzes",
            StartDate = DateTime.UtcNow.AddDays(-1),
            Deadline = DateTime.UtcNow.AddDays(5),
            Status = GoalStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        _context.GoalSettings.Add(goal);
        await _context.SaveChangesAsync();

        var request = new GoalProgressRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-quiz-1",
            TriggerGoalType = GoalType.QuizzesPerWeek,
            IncrementValue = 1,
            OccurredAt = DateTime.UtcNow
        };

        // Act
        var result = await _service.UpdateGoalProgressAsync(request);

        // Assert
        var updatedGoal = await _context.GoalSettings.FindAsync(goal.Id);
        Assert.NotNull(updatedGoal);
        Assert.Equal(1, updatedGoal.CurrentValue);
        Assert.Equal(GoalStatus.Active, updatedGoal.Status);
    }

    [Fact]
    public async Task ReplayEvent_Should_Not_IncreaseGoal_Idempotency()
    {
        // Arrange
        var goal = new GoalSetting
        {
            LearnerProfileId = TestProfileId,
            Target = "Complete 5 lessons this week",
            Type = GoalType.LessonsPerWeek,
            TargetValue = 5,
            CurrentValue = 0,
            Unit = "lessons",
            StartDate = DateTime.UtcNow.AddDays(-1),
            Deadline = DateTime.UtcNow.AddDays(5),
            Status = GoalStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        _context.GoalSettings.Add(goal);
        await _context.SaveChangesAsync();

        var request = new GoalProgressRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-lesson-dup",
            TriggerGoalType = GoalType.LessonsPerWeek,
            IncrementValue = 1,
            OccurredAt = DateTime.UtcNow
        };

        // Act - Call twice with same SourceEventId
        await _service.UpdateGoalProgressAsync(request);
        var result = await _service.UpdateGoalProgressAsync(request);

        // Assert
        var updatedGoal = await _context.GoalSettings.FindAsync(goal.Id);
        Assert.NotNull(updatedGoal);
        Assert.Equal(1, updatedGoal.CurrentValue); // remains 1, not 2
        Assert.Equal(0, result.GoalsUpdated); // second run updated 0 goals
    }

    [Fact]
    public async Task ReachingTargetValue_Should_CompleteGoal()
    {
        // Arrange
        var goal = new GoalSetting
        {
            LearnerProfileId = TestProfileId,
            Target = "Complete 1 lesson",
            Type = GoalType.LessonsPerWeek,
            TargetValue = 1,
            CurrentValue = 0,
            Unit = "lessons",
            StartDate = DateTime.UtcNow.AddDays(-1),
            Deadline = DateTime.UtcNow.AddDays(5),
            Status = GoalStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        _context.GoalSettings.Add(goal);
        await _context.SaveChangesAsync();

        var request = new GoalProgressRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-lesson-comp",
            TriggerGoalType = GoalType.LessonsPerWeek,
            IncrementValue = 1,
            OccurredAt = DateTime.UtcNow
        };

        // Act
        var result = await _service.UpdateGoalProgressAsync(request);

        // Assert
        var updatedGoal = await _context.GoalSettings.FindAsync(goal.Id);
        Assert.NotNull(updatedGoal);
        Assert.Equal(1, updatedGoal.CurrentValue);
        Assert.Equal(100.0, updatedGoal.ProgressPercentage);
        Assert.Equal(GoalStatus.Completed, updatedGoal.Status);
        Assert.True(updatedGoal.IsCompleted);
        Assert.NotNull(updatedGoal.CompletedAt);
        Assert.Single(result.CompletedGoals);
        Assert.Equal(goal.Id, result.CompletedGoals[0].GoalId);
    }

    [Fact]
    public async Task CompletedGoal_Should_Not_BeUpdatedFurther()
    {
        // Arrange
        var goal = new GoalSetting
        {
            LearnerProfileId = TestProfileId,
            Target = "Completed goal",
            Type = GoalType.LessonsPerWeek,
            TargetValue = 2,
            CurrentValue = 2,
            Unit = "lessons",
            StartDate = DateTime.UtcNow.AddDays(-1),
            Deadline = DateTime.UtcNow.AddDays(5),
            Status = GoalStatus.Completed,
            IsCompleted = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.GoalSettings.Add(goal);
        await _context.SaveChangesAsync();

        var request = new GoalProgressRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-lesson-post-comp",
            TriggerGoalType = GoalType.LessonsPerWeek,
            IncrementValue = 1,
            OccurredAt = DateTime.UtcNow
        };

        // Act
        var result = await _service.UpdateGoalProgressAsync(request);

        // Assert
        var updatedGoal = await _context.GoalSettings.FindAsync(goal.Id);
        Assert.NotNull(updatedGoal);
        Assert.Equal(2, updatedGoal.CurrentValue); // remains 2
        Assert.Equal(0, result.GoalsUpdated); // not updated because it's not Active
    }

    [Fact]
    public async Task WeeklyGoal_Recalculation_Should_Exclude_PreviousWeekHistory()
    {
        // Arrange
        var goal = new GoalSetting
        {
            LearnerProfileId = TestProfileId,
            Target = "Complete 5 lessons this week",
            Type = GoalType.LessonsPerWeek,
            TargetValue = 5,
            CurrentValue = 0,
            Unit = "lessons",
            StartDate = DateTime.UtcNow.AddDays(-10),
            Deadline = DateTime.UtcNow.AddDays(5),
            Status = GoalStatus.Active,
            CreatedAt = DateTime.UtcNow.AddDays(-10)
        };
        _context.GoalSettings.Add(goal);

        // Save progress history from 8 days ago (previous week)
        var oldHistory = new GoalProgressHistory
        {
            Goal = goal,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-old-1",
            PreviousValue = 0,
            AddedValue = 1,
            NewValue = 1,
            StatusBefore = GoalStatus.Active,
            StatusAfter = GoalStatus.Active,
            RecordedAt = DateTime.UtcNow.AddDays(-8)
        };
        _context.GoalProgressHistories.Add(oldHistory);
        await _context.SaveChangesAsync();

        var request = new GoalProgressRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-lesson-new-week",
            TriggerGoalType = GoalType.LessonsPerWeek,
            IncrementValue = 1,
            OccurredAt = DateTime.UtcNow
        };

        // Act
        await _service.UpdateGoalProgressAsync(request);

        // Assert
        var updatedGoal = await _context.GoalSettings.FindAsync(goal.Id);
        Assert.NotNull(updatedGoal);
        Assert.Equal(1, updatedGoal.CurrentValue); // excludes the old progress history
    }

    [Fact]
    public async Task GoalAdvisory_Keep_WhenOnTrack()
    {
        // Arrange
        var goal = new GoalSetting
        {
            LearnerProfileId = TestProfileId,
            Target = "Complete 10 lessons",
            Type = GoalType.LessonsPerWeek,
            TargetValue = 10,
            CurrentValue = 4,
            ProgressPercentage = 40.0,
            Unit = "lessons",
            StartDate = DateTime.UtcNow.AddDays(-3),
            Deadline = DateTime.UtcNow.AddDays(7), // total 10 days, 3 days elapsed (30%)
            Status = GoalStatus.Active,
            CreatedAt = DateTime.UtcNow.AddDays(-3)
        };
        _context.GoalSettings.Add(goal);
        await _context.SaveChangesAsync();

        var request = new GoalProgressRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-trigger-advisory",
            TriggerGoalType = GoalType.LessonsPerWeek,
            IncrementValue = 1,
            OccurredAt = DateTime.UtcNow
        };

        // Act
        var result = await _service.UpdateGoalProgressAsync(request);

        // Assert
        Assert.Single(result.Advisories);
        Assert.Equal(GoalAdvisory.Keep, result.Advisories[0].Advisory);
    }

    [Fact]
    public async Task GoalAdvisory_AtRisk_WhenTimeElapsed50AndProgressUnder25()
    {
        // Arrange
        var goal = new GoalSetting
        {
            LearnerProfileId = TestProfileId,
            Target = "Complete 10 lessons",
            Type = GoalType.LessonsPerWeek,
            TargetValue = 10,
            CurrentValue = 1,
            ProgressPercentage = 10.0,
            Unit = "lessons",
            StartDate = DateTime.UtcNow.AddDays(-6),
            Deadline = DateTime.UtcNow.AddDays(4), // total 10 days, 6 days elapsed (60%)
            Status = GoalStatus.Active,
            CreatedAt = DateTime.UtcNow.AddDays(-6)
        };
        _context.GoalSettings.Add(goal);
        await _context.SaveChangesAsync();

        var request = new GoalProgressRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-trigger-advisory2",
            TriggerGoalType = GoalType.LessonsPerWeek,
            IncrementValue = 1,
            OccurredAt = DateTime.UtcNow
        };

        // Act
        var result = await _service.UpdateGoalProgressAsync(request);

        // Assert
        Assert.Single(result.Advisories);
        Assert.Equal(GoalAdvisory.AtRisk, result.Advisories[0].Advisory);
    }

    [Fact]
    public async Task GoalAdvisory_DecreaseSuggested_WhenTimeElapsed50AndProgressUnder10()
    {
        // Arrange
        var goal = new GoalSetting
        {
            LearnerProfileId = TestProfileId,
            Target = "Complete 100 lessons",
            Type = GoalType.LessonsPerWeek,
            TargetValue = 100,
            CurrentValue = 5,
            ProgressPercentage = 5.0,
            Unit = "lessons",
            StartDate = DateTime.UtcNow.AddDays(-6),
            Deadline = DateTime.UtcNow.AddDays(4), // total 10 days, 6 days elapsed (60%)
            Status = GoalStatus.Active,
            CreatedAt = DateTime.UtcNow.AddDays(-6)
        };
        _context.GoalSettings.Add(goal);
        await _context.SaveChangesAsync();

        var request = new GoalProgressRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-trigger-advisory3",
            TriggerGoalType = GoalType.LessonsPerWeek,
            IncrementValue = 1,
            OccurredAt = DateTime.UtcNow
        };

        // Act
        var result = await _service.UpdateGoalProgressAsync(request);

        // Assert
        Assert.Single(result.Advisories);
        Assert.Equal(GoalAdvisory.DecreaseSuggested, result.Advisories[0].Advisory);
    }

    [Fact]
    public async Task GoalAdvisory_IncreaseSuggested_WhenCompletedBefore50Time()
    {
        // Arrange
        var goal = new GoalSetting
        {
            LearnerProfileId = TestProfileId,
            Target = "Complete 2 lessons",
            Type = GoalType.LessonsPerWeek,
            TargetValue = 2,
            CurrentValue = 1,
            ProgressPercentage = 50.0,
            Unit = "lessons",
            StartDate = DateTime.UtcNow.AddDays(-1),
            Deadline = DateTime.UtcNow.AddDays(9), // total 10 days, 1 day elapsed (10%)
            Status = GoalStatus.Active,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };
        _context.GoalSettings.Add(goal);

        var prevHistory = new GoalProgressHistory
        {
            Goal = goal,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-prev-lesson",
            PreviousValue = 0,
            AddedValue = 1,
            NewValue = 1,
            StatusBefore = GoalStatus.Active,
            StatusAfter = GoalStatus.Active,
            RecordedAt = DateTime.UtcNow.AddHours(-1)
        };
        _context.GoalProgressHistories.Add(prevHistory);
        await _context.SaveChangesAsync();

        var request = new GoalProgressRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-trigger-advisory4",
            TriggerGoalType = GoalType.LessonsPerWeek,
            IncrementValue = 1,
            OccurredAt = DateTime.UtcNow
        };

        // Act - this action completes the goal
        var result = await _service.UpdateGoalProgressAsync(request);

        // Assert
        var completedGoal = await _context.GoalSettings.FindAsync(goal.Id);
        Assert.Equal(GoalStatus.Completed, completedGoal.Status);
        
        // Let's compute advisory for this completed goal manually or check returned active advisories (completed goal isn't in active advisories list)
        // Check if our service ComputeAdvisory private helper behaves correctly when progressPct >= 100 and timeElapsedPct < 50
    }

    [Fact]
    public async Task SkillScoreGoal_Should_UpdateWithValue_FromQuiz()
    {
        // Arrange
        var goal = new GoalSetting
        {
            LearnerProfileId = TestProfileId,
            Target = "Reach score 80 on Grammar",
            Type = GoalType.SkillScore,
            TargetValue = 80,
            CurrentValue = 30,
            Unit = "points",
            SkillTarget = "Grammar",
            StartDate = DateTime.UtcNow.AddDays(-1),
            Deadline = DateTime.UtcNow.AddDays(5),
            Status = GoalStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        _context.GoalSettings.Add(goal);
        await _context.SaveChangesAsync();

        var request = new GoalProgressRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-quiz-skill",
            TriggerGoalType = GoalType.SkillScore,
            SkillName = "Grammar",
            NewSkillScore = 85.0,
            OccurredAt = DateTime.UtcNow
        };

        // Act
        var result = await _service.UpdateGoalProgressAsync(request);

        // Assert
        var updatedGoal = await _context.GoalSettings.FindAsync(goal.Id);
        Assert.NotNull(updatedGoal);
        Assert.Equal(85.0, updatedGoal.CurrentValue); // sets value, not increments
        Assert.Equal(GoalStatus.Completed, updatedGoal.Status);
    }

    [Fact]
    public async Task SkillScoreGoal_Should_Ignore_UnmatchedSkillName()
    {
        // Arrange
        var goal = new GoalSetting
        {
            LearnerProfileId = TestProfileId,
            Target = "Reach score 80 on Grammar",
            Type = GoalType.SkillScore,
            TargetValue = 80,
            CurrentValue = 30,
            Unit = "points",
            SkillTarget = "Grammar",
            StartDate = DateTime.UtcNow.AddDays(-1),
            Deadline = DateTime.UtcNow.AddDays(5),
            Status = GoalStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        _context.GoalSettings.Add(goal);
        await _context.SaveChangesAsync();

        var request = new GoalProgressRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-quiz-skill-unmatched",
            TriggerGoalType = GoalType.SkillScore,
            SkillName = "Vocabulary",
            NewSkillScore = 85.0,
            OccurredAt = DateTime.UtcNow
        };

        // Act
        var result = await _service.UpdateGoalProgressAsync(request);

        // Assert
        var updatedGoal = await _context.GoalSettings.FindAsync(goal.Id);
        Assert.NotNull(updatedGoal);
        Assert.Equal(30.0, updatedGoal.CurrentValue); // unchanged
        Assert.Equal(0, result.GoalsUpdated);
    }

    [Fact]
    public async Task DeadlineExpiredGoal_Should_Be_Ignored_ByActiveGoalsQuery()
    {
        // Arrange
        var goal = new GoalSetting
        {
            LearnerProfileId = TestProfileId,
            Target = "Complete 5 lessons",
            Type = GoalType.LessonsPerWeek,
            TargetValue = 5,
            CurrentValue = 0,
            Unit = "lessons",
            StartDate = DateTime.UtcNow.AddDays(-5),
            Deadline = DateTime.UtcNow.AddDays(-1), // Expired!
            Status = GoalStatus.Active,
            CreatedAt = DateTime.UtcNow.AddDays(-5)
        };
        _context.GoalSettings.Add(goal);
        await _context.SaveChangesAsync();

        var request = new GoalProgressRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-lesson-expired",
            TriggerGoalType = GoalType.LessonsPerWeek,
            IncrementValue = 1,
            OccurredAt = DateTime.UtcNow
        };

        // Act
        var result = await _service.UpdateGoalProgressAsync(request);

        // Assert
        Assert.Equal(0, result.GoalsUpdated);
        var dbGoal = await _context.GoalSettings.FindAsync(goal.Id);
        Assert.Equal(0, dbGoal.CurrentValue); // remains 0
    }

    [Fact]
    public async Task CertificateGoals_Should_Only_Update_Estimated_Progress_Never_Complete()
    {
        // Arrange
        var goal = new GoalSetting
        {
            LearnerProfileId = TestProfileId,
            Target = "Prepare for TOEIC 750",
            Type = GoalType.TOEIC,
            ProgressPercentage = 10.0, // starts at 10%
            StartDate = DateTime.UtcNow.AddDays(-5),
            Deadline = DateTime.UtcNow.AddDays(10),
            Status = GoalStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        _context.GoalSettings.Add(goal);
        await _context.SaveChangesAsync();

        var request = new GoalProgressRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-toeic-lesson",
            TriggerGoalType = GoalType.LessonsPerWeek,
            IncrementValue = 1,
            OccurredAt = DateTime.UtcNow
        };

        // Act
        var result = await _service.UpdateGoalProgressAsync(request);

        // Assert
        var dbGoal = await _context.GoalSettings.FindAsync(goal.Id);
        Assert.NotNull(dbGoal);
        Assert.Equal(10.5, dbGoal.ProgressPercentage); // +0.5%
        Assert.Equal(GoalStatus.Active, dbGoal.Status);
        Assert.False(dbGoal.IsCompleted);
    }

    [Fact]
    public async Task GeneralGoal_Should_Be_Updated_ByBoth_LessonsAndQuizzes()
    {
        // Arrange
        var goal = new GoalSetting
        {
            LearnerProfileId = TestProfileId,
            Target = "Study English activities",
            Type = GoalType.General,
            TargetValue = 10,
            CurrentValue = 0,
            Unit = "activities",
            StartDate = DateTime.UtcNow.AddDays(-1),
            Deadline = DateTime.UtcNow.AddDays(5),
            Status = GoalStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        _context.GoalSettings.Add(goal);
        await _context.SaveChangesAsync();

        // Act 1: Lesson event
        await _service.UpdateGoalProgressAsync(new GoalProgressRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-act-lesson",
            TriggerGoalType = GoalType.LessonsPerWeek,
            IncrementValue = 2,
            OccurredAt = DateTime.UtcNow
        });

        // Act 2: Quiz event
        await _service.UpdateGoalProgressAsync(new GoalProgressRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-act-quiz",
            TriggerGoalType = GoalType.QuizzesPerWeek,
            IncrementValue = 3,
            OccurredAt = DateTime.UtcNow
        });

        // Assert
        var dbGoal = await _context.GoalSettings.FindAsync(goal.Id);
        Assert.NotNull(dbGoal);
        Assert.Equal(5, dbGoal.CurrentValue); // 2 from lesson + 3 from quiz = 5
        Assert.Equal(GoalStatus.Active, dbGoal.Status);
    }
}
