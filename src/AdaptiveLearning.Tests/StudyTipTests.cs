using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Infrastructure.Persistence;
using CoreLearningSystem.Infrastructure.Persistence.Repositories;
using CoreLearningSystem.Application.Features.Adaptive;
using CoreLearningSystem.Application.Interfaces;

namespace AdaptiveLearning.Tests;

/// <summary>
/// Unit tests for the AI Study Tip feature.
/// Uses SQLite in-memory database (same pattern as existing tests).
/// NullCacheService avoids Redis dependency.
/// </summary>
public class StudyTipTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly GetStudyTipQueryHandler _handler;

    private const int TestUserId = 9100;
    private const int TestProfileId = 9200;

    public StudyTipTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(dbOptions);
        _context.Database.EnsureCreated();

        _handler = new GetStudyTipQueryHandler(
            new Repository<LearnerProfile>(_context),
            new Repository<SkillMatrix>(_context),
            new Repository<LearnerWeaknessHistory>(_context),
            new Repository<Recommendation>(_context),
            new Repository<Lesson>(_context),
            new Repository<GoalSetting>(_context),
            new NullCacheService(),
            new TestCacheKeyBuilder());
    }

    // ─────────────────────────────────────────────────────────────────────
    // 1. Fallback tip – no learner profile
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStudyTip_NoProfile_ReturnsFallbackTip()
    {
        // Act – use a userId that has no profile
        var result = await _handler.Handle(new GetStudyTipQuery(UserId: 99999), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Contains("bài học ngắn", result.Data!.TipText);
        Assert.Null(result.Data.WeakSkill);
        Assert.Empty(result.Data.RecommendedLessonIds);
        Assert.Equal(0, result.Data.LearnerId);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 2. Weak skill tip – has Skill Matrix, no active recommendation
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStudyTip_WithSkillMatrix_ReturnsWeakSkillTip()
    {
        // Arrange
        await SeedUserAndProfile();

        _context.SkillMatrices.AddRange(
            new SkillMatrix { LearnerProfileId = TestProfileId, Skill = SkillType.Grammar,    CurrentScore = 80, MasteryLevel = MasteryLevel.Good },
            new SkillMatrix { LearnerProfileId = TestProfileId, Skill = SkillType.Vocabulary, CurrentScore = 30, MasteryLevel = MasteryLevel.Weak }
        );
        await _context.SaveChangesAsync();

        // Act
        var result = await _handler.Handle(new GetStudyTipQuery(TestUserId), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("Vocabulary", result.Data!.WeakSkill);
        Assert.Contains("Vocabulary", result.Data.TipText);
        Assert.Contains("yếu", result.Data.TipText);
        Assert.Equal("Review weak skill lessons", result.Data.RecommendedAction);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 3. Full tip – weak skill + active recommendation → lesson tip
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStudyTip_WithWeakSkillAndRecommendation_ReturnsLessonTip()
    {
        // Arrange
        await SeedUserAndProfile();

        _context.SkillMatrices.Add(new SkillMatrix
        {
            LearnerProfileId = TestProfileId,
            Skill = SkillType.Vocabulary,
            CurrentScore = 25,
            MasteryLevel = MasteryLevel.Weak
        });

        var lesson = new Lesson
        {
            Id = 9001,
            Title = "Travel Vocabulary",
            Skill = SkillType.Vocabulary,
            Topic = "Travel",
            Level = EnglishLevel.A1,
            Content = "...",
            Status = LessonStatus.Published
        };
        _context.Lessons.Add(lesson);

        _context.Recommendations.Add(new Recommendation
        {
            LearnerProfileId = TestProfileId,
            LessonId = 9001,
            Skill = SkillType.Vocabulary,
            Topic = "Travel",
            Level = EnglishLevel.A1,
            PriorityScore = 0.95,
            Reason = "Weak in Vocabulary",
            Status = RecommendationStatus.Active,
            SourceEventId = "test-event-studytip-1",
            GeneratedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });

        await _context.SaveChangesAsync();

        // Act
        var result = await _handler.Handle(new GetStudyTipQuery(TestUserId), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("Vocabulary", result.Data!.WeakSkill);
        Assert.Contains("Travel Vocabulary", result.Data.TipText);
        Assert.Contains(9001, result.Data.RecommendedLessonIds);
        Assert.Equal("Start recommended lesson", result.Data.RecommendedAction);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 4. Goal-based tip – no skill matrix, goal near completion
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStudyTip_NearCompletionGoal_ReturnsGoalTip()
    {
        // Arrange
        await SeedUserAndProfile();

        _context.GoalSettings.Add(new GoalSetting
        {
            LearnerProfileId = TestProfileId,
            Target = "Hoàn thành 5 bài học Ngữ pháp",
            Type = GoalType.LessonsPerWeek,
            ProgressPercentage = 80.0,
            IsCompleted = false,
            Deadline = DateTime.UtcNow.AddDays(3),
            StartDate = DateTime.UtcNow.AddDays(-4),
            Status = GoalStatus.Active
        });
        await _context.SaveChangesAsync();

        // Act
        var result = await _handler.Handle(new GetStudyTipQuery(TestUserId), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Contains("mục tiêu", result.Data!.TipText);
        Assert.Null(result.Data.WeakSkill);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private async Task SeedUserAndProfile()
    {
        if (await _context.Users.FindAsync(TestUserId) != null) return;

        _context.Users.Add(new User
        {
            Id = TestUserId,
            Username = "studytip_tester",
            Email = "studytip@test.com",
            PasswordHash = "hash",
            Role = UserRole.Learner,
            CreatedAt = DateTime.UtcNow
        });

        _context.LearnerProfiles.Add(new LearnerProfile
        {
            Id = TestProfileId,
            UserId = TestUserId,
            Level = EnglishLevel.A1,
            LastActiveAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Test doubles
    // ─────────────────────────────────────────────────────────────────────

    private sealed class NullCacheService : ICacheService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
            => Task.FromResult<T?>(null);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default) where T : class
            => Task.CompletedTask;
        public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(false);
        public Task AddKeyToSetAsync(string setKey, string memberKey, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveKeysBySetAsync(string setKey, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> IncrementVersionAsync(string versionKey, CancellationToken ct = default) => Task.FromResult(1L);
        public Task<long> GetVersionAsync(string versionKey, CancellationToken ct = default) => Task.FromResult(1L);
    }

    private sealed class TestCacheKeyBuilder : ICacheKeyBuilder
    {
        private const string Ns = "test:v1";
        public string LessonListVersion() => $"{Ns}:lessons:list-version";
        public string LessonList(long v, string? s = null, string? l = null, string? r = null) => $"{Ns}:lessons:list:v{v}";
        public string LessonDetail(int id) => $"{Ns}:lessons:detail:{id}";
        public string LessonDetailSet() => $"{Ns}:lessons:detail-keyset";
        public string SkillMatrix(int id) => $"{Ns}:skill-matrix:{id}";
        public string ActiveRecommendations(int id) => $"{Ns}:recommendations:active:{id}";
        public string ProgressSummary(int id) => $"{Ns}:progress:summary:{id}";
        public string ProgressDetails(int id) => $"{Ns}:progress:details:{id}";
        public string StudyTip(int id) => $"{Ns}:study-tip:{id}";
        public string ProcessedEventProcessing(string eid) => $"{Ns}:processed-event:processing:{eid}";
        public string ProcessedEventCompleted(string eid) => $"{Ns}:processed-event:completed:{eid}";
    }
}
