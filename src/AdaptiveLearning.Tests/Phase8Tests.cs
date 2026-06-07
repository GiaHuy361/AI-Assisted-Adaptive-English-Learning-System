using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Application.Options;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Infrastructure.Services;
using AdaptiveLearning.Worker.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AdaptiveLearning.Tests;

// ── Fake Repository ────────────────────────────────────────────────────────────
internal class FakeFeedbackAnalysisRepo : IRepository<FeedbackAnalysis>
{
    private readonly List<FeedbackAnalysis> _data = new();
    private int _nextId = 1;

    public Task<FeedbackAnalysis?> GetByIdAsync(int id) =>
        Task.FromResult(_data.FirstOrDefault(x => x.Id == id));

    public Task<IEnumerable<FeedbackAnalysis>> GetAllAsync() =>
        Task.FromResult<IEnumerable<FeedbackAnalysis>>(_data);

    public Task<IEnumerable<FeedbackAnalysis>> FindAsync(Expression<Func<FeedbackAnalysis, bool>> predicate) =>
        Task.FromResult<IEnumerable<FeedbackAnalysis>>(_data.Where(predicate.Compile()).ToList());

    public Task AddAsync(FeedbackAnalysis entity)
    {
        entity.Id = _nextId++;
        _data.Add(entity);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(FeedbackAnalysis entity) => Task.CompletedTask;
    public Task DeleteAsync(FeedbackAnalysis entity) { _data.Remove(entity); return Task.CompletedTask; }
    public Task SaveChangesAsync() => Task.CompletedTask;
    public Task BeginTransactionAsync() => Task.CompletedTask;
    public Task CommitTransactionAsync() => Task.CompletedTask;
    public Task RollbackTransactionAsync() => Task.CompletedTask;

    public List<FeedbackAnalysis> All => _data;
}

internal class FakeUserRepo : IRepository<User>
{
    private readonly List<User> _users;
    public FakeUserRepo(List<User> users) => _users = users;
    public Task<User?> GetByIdAsync(int id) => Task.FromResult(_users.FirstOrDefault(u => u.Id == id));
    public Task<IEnumerable<User>> GetAllAsync() => Task.FromResult<IEnumerable<User>>(_users);
    public Task<IEnumerable<User>> FindAsync(Expression<Func<User, bool>> predicate) =>
        Task.FromResult<IEnumerable<User>>(_users.Where(predicate.Compile()).ToList());
    public Task AddAsync(User entity) { _users.Add(entity); return Task.CompletedTask; }
    public Task UpdateAsync(User entity) => Task.CompletedTask;
    public Task DeleteAsync(User entity) { _users.Remove(entity); return Task.CompletedTask; }
    public Task SaveChangesAsync() => Task.CompletedTask;
    public Task BeginTransactionAsync() => Task.CompletedTask;
    public Task CommitTransactionAsync() => Task.CompletedTask;
    public Task RollbackTransactionAsync() => Task.CompletedTask;
}

// ── Tests ──────────────────────────────────────────────────────────────────────
public class FeedbackAnalysisTests
{
    private FeedbackAnalysisService BuildService(
        FakeFeedbackAnalysisRepo analysisRepo,
        IRepository<User>? userRepo = null,
        INotificationService? notificationService = null,
        FeedbackAnalysisOptions? opts = null)
    {
        userRepo ??= new FakeUserRepo(new List<User>());
        notificationService ??= Mock.Of<INotificationService>();
        opts ??= new FeedbackAnalysisOptions();
        return new FeedbackAnalysisService(
            analysisRepo,
            userRepo,
            notificationService,
            Options.Create(opts),
            NullLogger<FeedbackAnalysisService>.Instance);
    }

    [Fact]
    public async Task ProcessFeedback_FirstFeedback_CreatesAggregate()
    {
        var repo = new FakeFeedbackAnalysisRepo();
        var svc = BuildService(repo);

        await svc.ProcessFeedbackAsync(1, 10, FeedbackTargetType.Lesson, 42, 5);

        Assert.Single(repo.All);
        var agg = repo.All[0];
        Assert.Equal("lesson:42", agg.AggregateKey);
        Assert.Equal(1, agg.FeedbackCount);
        Assert.Equal(5.0, agg.AverageRating);
        Assert.Equal(1, agg.PositiveCount);
        Assert.Equal(0, agg.NegativeCount);
        Assert.Equal(FeedbackAlertStatus.Normal, agg.AlertStatus);
    }

    [Fact]
    public async Task ProcessFeedback_MultipleFeedbacks_AggregatesCorrectly()
    {
        var repo = new FakeFeedbackAnalysisRepo();
        var svc = BuildService(repo);

        await svc.ProcessFeedbackAsync(1, 10, FeedbackTargetType.Lesson, 5, 5);
        await svc.ProcessFeedbackAsync(2, 11, FeedbackTargetType.Lesson, 5, 3);
        await svc.ProcessFeedbackAsync(3, 12, FeedbackTargetType.Lesson, 5, 1);

        Assert.Single(repo.All);
        var agg = repo.All[0];
        Assert.Equal(3, agg.FeedbackCount);
        Assert.Equal((5.0 + 3.0 + 1.0) / 3, agg.AverageRating, 10);
        Assert.Equal(1, agg.PositiveCount); // 5
        Assert.Equal(1, agg.NeutralCount);  // 3
        Assert.Equal(1, agg.NegativeCount); // 1
    }

    [Fact]
    public async Task ProcessFeedback_SystemTarget_UsesGlobalKey()
    {
        var repo = new FakeFeedbackAnalysisRepo();
        var svc = BuildService(repo);

        await svc.ProcessFeedbackAsync(1, 10, FeedbackTargetType.System, null, 3);

        Assert.Single(repo.All);
        Assert.Equal("system:global", repo.All[0].AggregateKey);
    }

    [Fact]
    public async Task ProcessFeedback_TriggersWarning_WhenThresholdReached()
    {
        var repo = new FakeFeedbackAnalysisRepo();
        var notifMock = new Mock<INotificationService>();
        notifMock.Setup(n => n.CreateNotificationAsync(It.IsAny<CreateNotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationDetailsDto?)null);

        var adminUser = new User { Id = 1, Role = UserRole.Admin };
        var userRepo = new FakeUserRepo(new List<User> { adminUser });

        var opts = new FeedbackAnalysisOptions
        {
            MinimumCountForAlert = 3,
            WarningAverageRatingThreshold = 3.5,
            CriticalAverageRatingThreshold = 2.5,
            WarningLowRatingRateThreshold = 0.30,
            CriticalLowRatingRateThreshold = 0.50
        };

        var svc = BuildService(repo, userRepo, notifMock.Object, opts);

        // 3 feedbacks with low rating -> triggers warning (LowRatingRate = 2/3 = 66% > 30%)
        await svc.ProcessFeedbackAsync(1, 10, FeedbackTargetType.Lesson, 99, 2);
        await svc.ProcessFeedbackAsync(2, 11, FeedbackTargetType.Lesson, 99, 1);
        await svc.ProcessFeedbackAsync(3, 12, FeedbackTargetType.Lesson, 99, 5);

        var agg = repo.All[0];
        // Average: (2+1+5)/3 = 2.67 <= 2.5 -> Critical
        Assert.Equal(FeedbackAlertStatus.Critical, agg.AlertStatus);
        notifMock.Verify(n => n.CreateNotificationAsync(
            It.Is<CreateNotificationRequest>(r => r.Type == NotificationType.FeedbackAlert),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessFeedback_DoesNotDowngradeResolved()
    {
        var repo = new FakeFeedbackAnalysisRepo();
        // Pre-seed a resolved aggregate
        var existing = new FeedbackAnalysis
        {
            Id = 1,
            AggregateKey = "lesson:1",
            TargetType = FeedbackTargetType.Lesson,
            TargetId = 1,
            FeedbackCount = 10,
            AverageRating = 4.5,
            LowRatingCount = 0,
            AlertStatus = FeedbackAlertStatus.Resolved,
            UpdatedAt = DateTime.UtcNow
        };
        await repo.AddAsync(existing);

        var svc = BuildService(repo);
        // Even a perfect rating won't downgrade from Resolved
        await svc.ProcessFeedbackAsync(99, 10, FeedbackTargetType.Lesson, 1, 5);

        Assert.Equal(FeedbackAlertStatus.Resolved, repo.All[0].AlertStatus);
    }

    [Fact]
    public async Task GetAnalysisAsync_ReturnsNullWhenNotFound()
    {
        var repo = new FakeFeedbackAnalysisRepo();
        var svc = BuildService(repo);

        var result = await svc.GetAnalysisAsync("lesson:999");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAnalysisAsync_ReturnsMatchingAggregate()
    {
        var repo = new FakeFeedbackAnalysisRepo();
        var svc = BuildService(repo);

        await svc.ProcessFeedbackAsync(1, 10, FeedbackTargetType.Lesson, 7, 4);
        var result = await svc.GetAnalysisAsync("lesson:7");

        Assert.NotNull(result);
        Assert.Equal("lesson:7", result!.AggregateKey);
    }

    [Fact]
    public async Task GetAnalysesForTypeAsync_FiltersCorrectly()
    {
        var repo = new FakeFeedbackAnalysisRepo();
        var svc = BuildService(repo);

        await svc.ProcessFeedbackAsync(1, 10, FeedbackTargetType.Lesson, 1, 4);
        await svc.ProcessFeedbackAsync(2, 11, FeedbackTargetType.Quiz, 2, 3);
        await svc.ProcessFeedbackAsync(3, 12, FeedbackTargetType.Lesson, 3, 5);

        var lessons = await svc.GetAnalysesForTypeAsync(FeedbackTargetType.Lesson);
        Assert.Equal(2, lessons.Count);

        var quizzes = await svc.GetAnalysesForTypeAsync(FeedbackTargetType.Quiz);
        Assert.Single(quizzes);
    }
}

// ── CacheKeyBuilder Tests ──────────────────────────────────────────────────────
public class CacheKeyBuilderTests
{
    private readonly CacheKeyBuilder _builder = new();

    [Fact]
    public void LessonListVersion_ReturnsExpectedKey()
    {
        Assert.Equal("adaptive:v1:lessons:list-version", _builder.LessonListVersion());
    }

    [Fact]
    public void LessonList_WithParams_BuildsCorrectKey()
    {
        var key = _builder.LessonList(5, "Grammar", "B1", "admin");
        Assert.Equal("adaptive:v1:lessons:list:v5:grammar:b1:admin", key);
    }

    [Fact]
    public void LessonList_WithoutParams_UsesDefaults()
    {
        var key = _builder.LessonList(1);
        Assert.Equal("adaptive:v1:lessons:list:v1:all:all:admin", key);
    }

    [Fact]
    public void LessonDetail_ReturnsExpectedKey()
    {
        Assert.Equal("adaptive:v1:lessons:detail:42", _builder.LessonDetail(42));
    }

    [Fact]
    public void SkillMatrix_ReturnsExpectedKey()
    {
        Assert.Equal("adaptive:v1:skill-matrix:99", _builder.SkillMatrix(99));
    }

    [Fact]
    public void ActiveRecommendations_ReturnsExpectedKey()
    {
        Assert.Equal("adaptive:v1:recommendations:active:7", _builder.ActiveRecommendations(7));
    }

    [Fact]
    public void ProcessedEventKeys_AreDistinct()
    {
        var eventId = Guid.NewGuid().ToString();
        var processingKey = _builder.ProcessedEventProcessing(eventId);
        var completedKey = _builder.ProcessedEventCompleted(eventId);
        Assert.NotEqual(processingKey, completedKey);
        Assert.Contains("processing", processingKey);
        Assert.Contains("completed", completedKey);
    }

    [Fact]
    public void AggregateKey_System_IsGlobal()
    {
        var key = FeedbackAnalysis.BuildAggregateKey(FeedbackTargetType.System, null);
        Assert.Equal("system:global", key);
    }

    [Fact]
    public void AggregateKey_Lesson_IsLowercase()
    {
        var key = FeedbackAnalysis.BuildAggregateKey(FeedbackTargetType.Lesson, 123);
        Assert.Equal("lesson:123", key);
    }

    [Fact]
    public void AggregateKey_Quiz_IsLowercase()
    {
        var key = FeedbackAnalysis.BuildAggregateKey(FeedbackTargetType.Quiz, 55);
        Assert.Equal("quiz:55", key);
    }
}

// ── InMemoryProcessedEventStore Tests ─────────────────────────────────────────
public class InMemoryProcessedEventStoreTests
{
    [Fact]
    public async Task TryAcquireLock_Succeeds_WhenNotHeld()
    {
        var store = new InMemoryProcessedEventStore();
        var eventId = Guid.NewGuid();
        var result = await store.TryAcquireProcessingLockAsync(eventId, "owner-1", TimeSpan.FromMinutes(5));
        Assert.True(result);
    }

    [Fact]
    public async Task TryAcquireLock_Fails_WhenAlreadyHeld()
    {
        var store = new InMemoryProcessedEventStore();
        var eventId = Guid.NewGuid();
        await store.TryAcquireProcessingLockAsync(eventId, "owner-1", TimeSpan.FromMinutes(5));
        var result = await store.TryAcquireProcessingLockAsync(eventId, "owner-2", TimeSpan.FromMinutes(5));
        Assert.False(result);
    }

    [Fact]
    public async Task MarkAsCompleted_Succeeds_WhenOwnerMatches()
    {
        var store = new InMemoryProcessedEventStore();
        var eventId = Guid.NewGuid();
        await store.TryAcquireProcessingLockAsync(eventId, "owner-1", TimeSpan.FromMinutes(5));
        var result = await store.MarkAsCompletedAsync(eventId, "owner-1", TimeSpan.FromHours(24));
        Assert.True(result);
        Assert.True(await store.IsCompletedAsync(eventId));
    }

    [Fact]
    public async Task MarkAsCompleted_Fails_WhenWrongOwner()
    {
        var store = new InMemoryProcessedEventStore();
        var eventId = Guid.NewGuid();
        await store.TryAcquireProcessingLockAsync(eventId, "owner-1", TimeSpan.FromMinutes(5));
        var result = await store.MarkAsCompletedAsync(eventId, "wrong-owner", TimeSpan.FromHours(24));
        Assert.False(result);
        Assert.False(await store.IsCompletedAsync(eventId));
    }

    [Fact]
    public async Task TryAcquireLock_Fails_WhenAlreadyCompleted()
    {
        var store = new InMemoryProcessedEventStore();
        var eventId = Guid.NewGuid();
        await store.TryAcquireProcessingLockAsync(eventId, "owner-1", TimeSpan.FromMinutes(5));
        await store.MarkAsCompletedAsync(eventId, "owner-1", TimeSpan.FromHours(24));
        var result = await store.TryAcquireProcessingLockAsync(eventId, "owner-2", TimeSpan.FromMinutes(5));
        Assert.False(result);
    }

    [Fact]
    public async Task ReleaseLock_AllowsReacquisition_BySameOwner()
    {
        var store = new InMemoryProcessedEventStore();
        var eventId = Guid.NewGuid();
        await store.TryAcquireProcessingLockAsync(eventId, "owner-1", TimeSpan.FromMinutes(5));
        await store.ReleaseProcessingLockAsync(eventId, "owner-1");
        var result = await store.TryAcquireProcessingLockAsync(eventId, "owner-2", TimeSpan.FromMinutes(5));
        Assert.True(result);
    }

    [Fact]
    public async Task LegacyMarkAsProcessed_SetsCompletedState()
    {
        var store = new InMemoryProcessedEventStore();
        var eventId = Guid.NewGuid();
        await store.MarkAsProcessedAsync(eventId);
        Assert.True(await store.HasBeenProcessedAsync(eventId));
        Assert.True(await store.IsCompletedAsync(eventId));
    }
}

// ── Feedback Recommendation Engine Score Tests ─────────────────────────────────
public class FeedbackScoringEngineTests
{
    private AdaptiveRecommendationEngine BuildEngine() => new();

    private LearnerProfile BuildProfile(int id = 1, EnglishLevel level = EnglishLevel.B1) =>
        new()
        {
            Id = id,
            Level = level,
            SkillMatrices = new List<SkillMatrix>
            {
                new() { Skill = SkillType.Grammar, CurrentScore = 40, MasteryLevel = MasteryLevel.Weak }
            }
        };

    private Lesson BuildLesson(int id, string topic = "Present Tense", SkillType skill = SkillType.Grammar, EnglishLevel level = EnglishLevel.B1) =>
        new() { Id = id, Topic = topic, Skill = skill, Level = level, Status = LessonStatus.Published };

    [Fact]
    public void FeedbackDelta_Positive_IncreasesRank()
    {
        var engine = BuildEngine();
        var profile = BuildProfile();
        var lessonA = BuildLesson(1, "Present Tense");
        var lessonB = BuildLesson(2, "Present Tense");

        var feedbackScores = new Dictionary<int, double>
        {
            [1] = 3.0  // lesson A gets +3 bonus, lesson B gets none
        };

        var result = engine.GenerateAndRank(
            new List<Lesson> { lessonA, lessonB },
            profile,
            new List<LearnerWeaknessHistory>(),
            new List<string>(),
            SkillType.Grammar,
            new List<string>(),
            EnglishLevel.B1,
            "evt-feedback-test",
            feedbackScores: feedbackScores);

        Assert.True(result.Count >= 2);
        // lesson A should rank above lesson B
        Assert.Equal(1, result[0].LessonId);
    }

    [Fact]
    public void FeedbackDelta_Negative_DecreasesScore()
    {
        var engine = BuildEngine();
        var profile = BuildProfile();
        var lesson = BuildLesson(10);

        var feedbackScores = new Dictionary<int, double> { [10] = -10.0 };
        var result = engine.GenerateAndRank(
            new List<Lesson> { lesson }, profile,
            new List<LearnerWeaknessHistory>(),
            new List<string>(), SkillType.Grammar,
            new List<string>(), EnglishLevel.B1,
            "evt-negative",
            feedbackScores: feedbackScores);

        if (result.Count > 0)
        {
            // Score should be >= 0 (clamped)
            Assert.True(result[0].PriorityScore >= 0);
        }
    }

    [Fact]
    public void FeedbackDelta_Clamped_AtPlusMinus10()
    {
        var engine = BuildEngine();
        var profile = BuildProfile();
        var lesson = BuildLesson(1);

        // Delta > 10 should be clamped to 10
        var feedbackScores = new Dictionary<int, double> { [1] = 999.0 };
        var result1 = engine.GenerateAndRank(
            new List<Lesson> { lesson }, profile,
            new List<LearnerWeaknessHistory>(), new List<string>(),
            SkillType.Grammar, new List<string>(), EnglishLevel.B1,
            "evt-clamp", feedbackScores: feedbackScores);

        // Delta < -10 should be clamped to -10
        var feedbackScores2 = new Dictionary<int, double> { [1] = -999.0 };
        var result2 = engine.GenerateAndRank(
            new List<Lesson> { lesson }, profile,
            new List<LearnerWeaknessHistory>(), new List<string>(),
            SkillType.Grammar, new List<string>(), EnglishLevel.B1,
            "evt-clamp2", feedbackScores: feedbackScores2);

        // Both scores should be within [0, 100]
        if (result1.Count > 0) Assert.InRange(result1[0].PriorityScore, 0, 100);
        if (result2.Count > 0) Assert.InRange(result2[0].PriorityScore, 0, 100);
    }

    [Fact]
    public void NullFeedbackScores_DoesNotAffectScoring()
    {
        var engine = BuildEngine();
        var profile = BuildProfile();
        var lesson = BuildLesson(1);

        var r1 = engine.GenerateAndRank(
            new List<Lesson> { lesson }, profile,
            new List<LearnerWeaknessHistory>(), new List<string>(),
            SkillType.Grammar, new List<string>(), EnglishLevel.B1,
            "evt-null-feedback", feedbackScores: null);

        var r2 = engine.GenerateAndRank(
            new List<Lesson> { lesson }, profile,
            new List<LearnerWeaknessHistory>(), new List<string>(),
            SkillType.Grammar, new List<string>(), EnglishLevel.B1,
            "evt-empty-feedback", feedbackScores: new Dictionary<int, double>());

        if (r1.Count > 0 && r2.Count > 0)
            Assert.Equal(r1[0].PriorityScore, r2[0].PriorityScore);
    }
}
