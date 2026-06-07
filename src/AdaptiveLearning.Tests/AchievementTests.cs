using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Infrastructure.Persistence;
using CoreLearningSystem.Infrastructure.Persistence.Repositories;
using CoreLearningSystem.Infrastructure.Services;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.Options;
using CoreLearningSystem.Application.Interfaces;
using AdaptiveLearning.Contracts.Events;
using QuizSubmittedEvent = CoreLearningSystem.Application.DTOs.Events.QuizSubmittedEvent;
using PlacementTestCompletedEvent = CoreLearningSystem.Application.DTOs.Events.PlacementTestCompletedEvent;
using GoalCompletedEvent = CoreLearningSystem.Application.DTOs.Events.GoalCompletedEvent;
using LessonCompletedEvent = CoreLearningSystem.Application.DTOs.Events.LessonCompletedEvent;
using FeedbackSubmittedEvent = CoreLearningSystem.Application.DTOs.Events.FeedbackSubmittedEvent;

namespace AdaptiveLearning.Tests;

public class AchievementTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly AchievementEngine _engine;
    private readonly AchievementService _service;
    private readonly TestKafkaPublisher _publisher;

    private const int TestProfileId = 700;
    private const int TestUserId = 800;

    public AchievementTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _engine = new AchievementEngine();
        _publisher = new TestKafkaPublisher();

        var optionsWrapper = Options.Create(new AchievementOptions
        {
            HighScoreThresholdPercent = 80.0,
            SkillImprovementThreshold = 15
        });

        _service = new AchievementService(
            _context,
            _engine,
            _publisher,
            optionsWrapper,
            new NullLogger<AchievementService>()
        );

        SeedDefaultData();
    }

    private void SeedDefaultData()
    {
        var user = new User
        {
            Id = TestUserId,
            Username = "badge_test_learner",
            Email = "badge_test@learner.com",
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

        // Seed lessons (Ids will be 1 to 10)
        for (int i = 1; i <= 10; i++)
        {
            _context.Lessons.Add(new Lesson
            {
                Title = $"Lesson {i}",
                Content = $"Content {i}",
                Skill = SkillType.General,
                Topic = $"Topic {i}",
                Level = EnglishLevel.B1,
                DurationInMinutes = 15,
                Status = LessonStatus.Published
            });
        }

        // Seed quizzes (Ids will be 1 to 10)
        for (int i = 1; i <= 10; i++)
        {
            _context.Quizzes.Add(new Quiz
            {
                Title = $"Quiz {i}",
                Description = $"Description {i}",
                DurationMinutes = 10,
                PassingScore = 70.0,
                MaxScore = 100.0,
                Level = EnglishLevel.B1,
                IsPlacementTest = false
            });
        }

        // Seed 8 active badges
        var badges = new List<AchievementBadge>
        {
            new() { Id = 1, Code = "FIRST_LESSON", Name = "First Step", Criteria = "Complete first lesson", AchievementType = AchievementType.FirstLesson, Threshold = 1, IsActive = true },
            new() { Id = 2, Code = "FIRST_QUIZ", Name = "Quiz Starter", Criteria = "Complete first quiz", AchievementType = AchievementType.FirstQuiz, Threshold = 1, IsActive = true },
            new() { Id = 3, Code = "LESSONS_10", Name = "Dedicated Learner", Criteria = "Complete 10 lessons", AchievementType = AchievementType.LessonCount, Threshold = 10, IsActive = true },
            new() { Id = 4, Code = "QUIZZES_HIGH_SCORE_5", Name = "High Achiever", Criteria = "5 High Score Quizzes", AchievementType = AchievementType.QuizHighScoreCount, Threshold = 5, IsActive = true },
            new() { Id = 5, Code = "STREAK_3", Name = "3-Day Streak", Criteria = "3-day streak", AchievementType = AchievementType.LearningStreak, Threshold = 3, IsActive = true },
            new() { Id = 6, Code = "STREAK_7", Name = "Week Warrior", Criteria = "7-day streak", AchievementType = AchievementType.LearningStreak, Threshold = 7, IsActive = true },
            new() { Id = 7, Code = "FIRST_GOAL_COMPLETED", Name = "Goal Getter", Criteria = "1 completed goal", AchievementType = AchievementType.GoalCompletionCount, Threshold = 1, IsActive = true },
            new() { Id = 8, Code = "SKILL_IMPROVED_15", Name = "Skill Builder", Criteria = "Improve skill by 15", AchievementType = AchievementType.SkillImprovement, Threshold = 15, IsActive = true }
        };
        _context.AchievementBadges.AddRange(badges);
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    [Fact]
    public async Task LessonCompleted_ShouldAward_FirstLessonBadge()
    {
        // Arrange
        // Add 1 completed lesson
        _context.LearnerProgresses.Add(new LearnerProgress
        {
            LearnerProfileId = TestProfileId,
            LessonId = 1,
            IsCompleted = true,
            CompletedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var request = new AchievementEvaluationRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-first-lesson",
            Trigger = AchievementTrigger.LessonCompleted,
            OccurredAt = DateTime.UtcNow
        };

        // Act
        var result = await _service.EvaluateAndAwardAsync(request);

        // Assert
        Assert.Single(result.AwardedBadges);
        Assert.Equal("FIRST_LESSON", result.AwardedBadges[0].Code);
        Assert.Single(_publisher.Events);
        var evt = (BadgeAwardedEvent)_publisher.Events[0];
        Assert.Equal("FIRST_LESSON", evt.AchievementCode);
        Assert.Equal(TestUserId, evt.UserId);
    }

    [Fact]
    public async Task QuizSubmitted_ShouldAward_FirstQuizBadge()
    {
        // Arrange
        _context.QuizAttempts.Add(new QuizAttempt
        {
            LearnerProfileId = TestProfileId,
            QuizId = 1,
            Score = 70.0,
            AttemptedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var request = new AchievementEvaluationRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-first-quiz",
            Trigger = AchievementTrigger.QuizSubmitted,
            OccurredAt = DateTime.UtcNow
        };

        // Act
        var result = await _service.EvaluateAndAwardAsync(request);

        // Assert
        Assert.Single(result.AwardedBadges);
        Assert.Equal("FIRST_QUIZ", result.AwardedBadges[0].Code);
    }

    [Fact]
    public async Task LessonCompleted_ShouldAward_Lessons10Badge_WhenCount10()
    {
        // Arrange
        for (int i = 1; i <= 10; i++)
        {
            _context.LearnerProgresses.Add(new LearnerProgress
            {
                LearnerProfileId = TestProfileId,
                LessonId = i,
                IsCompleted = true,
                CompletedAt = DateTime.UtcNow
            });
        }
        await _context.SaveChangesAsync();

        var request = new AchievementEvaluationRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-lessons-10",
            Trigger = AchievementTrigger.LessonCompleted,
            OccurredAt = DateTime.UtcNow
        };

        // Act
        var result = await _service.EvaluateAndAwardAsync(request);

        // Assert - should award both FIRST_LESSON and LESSONS_10
        Assert.Equal(2, result.AwardedBadges.Count);
        Assert.Contains(result.AwardedBadges, b => b.Code == "LESSONS_10");
        Assert.Contains(result.AwardedBadges, b => b.Code == "FIRST_LESSON");
    }

    [Fact]
    public async Task QuizSubmitted_ShouldAward_QuizzesHighScore5Badge_WhenPassedThreshold()
    {
        // Arrange
        for (int i = 1; i <= 5; i++)
        {
            _context.QuizAttempts.Add(new QuizAttempt
            {
                LearnerProfileId = TestProfileId,
                QuizId = i,
                Score = 85.0, // above 80%
                AttemptedAt = DateTime.UtcNow
            });
        }
        await _context.SaveChangesAsync();

        var request = new AchievementEvaluationRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-high-scores",
            Trigger = AchievementTrigger.QuizSubmitted,
            OccurredAt = DateTime.UtcNow
        };

        // Act
        var result = await _service.EvaluateAndAwardAsync(request);

        // Assert
        Assert.Contains(result.AwardedBadges, b => b.Code == "QUIZZES_HIGH_SCORE_5");
    }

    [Fact]
    public async Task LearningStreak_ShouldAward_StreakBadges()
    {
        // Arrange - activities on 3 consecutive days
        var now = DateTime.UtcNow;
        _context.LearnerProgresses.Add(new LearnerProgress { LearnerProfileId = TestProfileId, LessonId = 1, IsCompleted = true, CompletedAt = now });
        _context.LearnerProgresses.Add(new LearnerProgress { LearnerProfileId = TestProfileId, LessonId = 2, IsCompleted = true, CompletedAt = now.AddDays(-1) });
        _context.LearnerProgresses.Add(new LearnerProgress { LearnerProfileId = TestProfileId, LessonId = 3, IsCompleted = true, CompletedAt = now.AddDays(-2) });
        await _context.SaveChangesAsync();

        var request = new AchievementEvaluationRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-streak-3",
            Trigger = AchievementTrigger.LessonCompleted,
            OccurredAt = now
        };

        // Act
        var result = await _service.EvaluateAndAwardAsync(request);

        // Assert
        Assert.Contains(result.AwardedBadges, b => b.Code == "STREAK_3");
    }

    [Fact]
    public async Task GoalCompleted_ShouldAward_GoalGetterBadge()
    {
        // Arrange
        _context.GoalSettings.Add(new GoalSetting
        {
            LearnerProfileId = TestProfileId,
            Target = "Any goal",
            Status = GoalStatus.Completed,
            IsCompleted = true,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var request = new AchievementEvaluationRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-goal-done",
            Trigger = AchievementTrigger.GoalCompleted,
            OccurredAt = DateTime.UtcNow
        };

        // Act
        var result = await _service.EvaluateAndAwardAsync(request);

        // Assert
        Assert.Contains(result.AwardedBadges, b => b.Code == "FIRST_GOAL_COMPLETED");
    }

    [Fact]
    public async Task SkillMatrixHistory_ShouldAward_SkillBuilderBadge()
    {
        // Arrange
        _context.SkillMatrices.Add(new SkillMatrix
        {
            LearnerProfileId = TestProfileId,
            Skill = SkillType.Grammar,
            CurrentScore = 75,
            MasteryLevel = MasteryLevel.Average
        });
        _context.SkillMatrixHistories.Add(new SkillMatrixHistory
        {
            LearnerProfileId = TestProfileId,
            Skill = SkillType.Grammar,
            PreviousScore = 55, // 20 points improvement
            AssessmentScore = 75,
            NewScore = 75,
            RecordedAt = DateTime.UtcNow.AddDays(-1)
        });
        await _context.SaveChangesAsync();

        var request = new AchievementEvaluationRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-skill-improve",
            Trigger = AchievementTrigger.QuizSubmitted,
            OccurredAt = DateTime.UtcNow
        };

        // Act
        var result = await _service.EvaluateAndAwardAsync(request);

        // Assert
        Assert.Contains(result.AwardedBadges, b => b.Code == "SKILL_IMPROVED_15");
    }

    [Fact]
    public async Task AwardBadge_ShouldBeIdempotent_NotAwardTwice()
    {
        // Arrange
        _context.LearnerProgresses.Add(new LearnerProgress
        {
            LearnerProfileId = TestProfileId,
            LessonId = 1,
            IsCompleted = true,
            CompletedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var request = new AchievementEvaluationRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-first-lesson-idem",
            Trigger = AchievementTrigger.LessonCompleted,
            OccurredAt = DateTime.UtcNow
        };

        // Act - Call twice
        var result1 = await _service.EvaluateAndAwardAsync(request);
        var result2 = await _service.EvaluateAndAwardAsync(request);

        // Assert
        Assert.Single(result1.AwardedBadges);
        Assert.Empty(result2.AwardedBadges); // second award returns empty because it's already awarded
        Assert.Equal(1, result2.SkippedDuplicates);
    }

    [Fact]
    public async Task ReplayedEvent_Should_Not_AwardDuplicateBadges()
    {
        // Arrange
        _context.LearnerProgresses.Add(new LearnerProgress
        {
            LearnerProfileId = TestProfileId,
            LessonId = 1,
            IsCompleted = true,
            CompletedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var request1 = new AchievementEvaluationRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-play-1",
            Trigger = AchievementTrigger.LessonCompleted,
            OccurredAt = DateTime.UtcNow
        };

        // Act - run first
        await _service.EvaluateAndAwardAsync(request1);

        // Simulating replay with same event ID, but maybe different lesson progress (e.g. 2nd lesson completed but event has same SourceEventId)
        var request2 = new AchievementEvaluationRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-play-1", // same EventId
            Trigger = AchievementTrigger.LessonCompleted,
            OccurredAt = DateTime.UtcNow
        };
        var result = await _service.EvaluateAndAwardAsync(request2);

        // Assert
        Assert.Empty(result.AwardedBadges); // already earned, skipped
    }

    [Fact]
    public async Task InactiveBadge_Should_Not_Be_Awarded()
    {
        // Arrange
        var inactiveBadge = new AchievementBadge
        {
            Code = "INACTIVE_TEST",
            Name = "Inactive Step",
            Criteria = "Complete 1 lesson",
            AchievementType = AchievementType.FirstLesson,
            Threshold = 1,
            IsActive = false // INACTIVE
        };
        _context.AchievementBadges.Add(inactiveBadge);

        _context.LearnerProgresses.Add(new LearnerProgress
        {
            LearnerProfileId = TestProfileId,
            LessonId = 1,
            IsCompleted = true,
            CompletedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var request = new AchievementEvaluationRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-inactive-test",
            Trigger = AchievementTrigger.LessonCompleted,
            OccurredAt = DateTime.UtcNow
        };

        // Act
        var result = await _service.EvaluateAndAwardAsync(request);

        // Assert
        Assert.DoesNotContain(result.AwardedBadges, b => b.Code == "INACTIVE_TEST");
    }

    [Fact]
    public async Task BadgeAwardedEvent_Should_Have_CorrectPayload()
    {
        // Arrange
        _context.LearnerProgresses.Add(new LearnerProgress
        {
            LearnerProfileId = TestProfileId,
            LessonId = 1,
            IsCompleted = true,
            CompletedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var request = new AchievementEvaluationRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-payload-test",
            Trigger = AchievementTrigger.LessonCompleted,
            OccurredAt = DateTime.UtcNow
        };

        // Act
        await _service.EvaluateAndAwardAsync(request);

        // Assert
        Assert.Single(_publisher.Events);
        var evt = (BadgeAwardedEvent)_publisher.Events[0];
        Assert.Equal(TestUserId, evt.UserId);
        Assert.Equal(TestProfileId, evt.LearnerProfileId);
        Assert.Equal("FIRST_LESSON", evt.AchievementCode);
        Assert.Equal("First Step", evt.AchievementName);
        Assert.Equal(1.0, evt.ProgressValue);
    }

    private class TestKafkaPublisher : IKafkaPublisher
    {
        public List<object> Events { get; } = new();

        public Task PublishQuizSubmittedAsync(QuizSubmittedEvent ev) => Task.CompletedTask;
        public Task PublishPlacementTestCompletedAsync(PlacementTestCompletedEvent ev) => Task.CompletedTask;
        public Task PublishGoalCompletedAsync(GoalCompletedEvent ev) => Task.CompletedTask;
        public Task PublishLessonCompletedAsync(LessonCompletedEvent ev) => Task.CompletedTask;
        public Task PublishFeedbackSubmittedAsync(FeedbackSubmittedEvent ev) => Task.CompletedTask;

        public Task PublishAsync(string topic, string key, object message)
        {
            Events.Add(message);
            return Task.CompletedTask;
        }
    }
}
