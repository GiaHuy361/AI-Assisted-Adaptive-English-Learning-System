using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Grpc.Core;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Infrastructure.Persistence;
using CoreLearningSystem.Infrastructure.Persistence.Repositories;
using CoreLearningSystem.Infrastructure.Services;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Application.Options;
using CoreLearningSystem.Application.DTOs.Common;
using AdaptiveLearning.GrpcService.Services;
using AdaptiveLearning.GrpcService;
using StackExchange.Redis;
using Confluent.Kafka;

namespace AdaptiveLearning.Tests;

public class Phase10Tests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;

    public Phase10Tests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    // ── 1. CERTIFICATE GOAL VERIFICATION ───────────────────────────────────────────
    [Fact]
    public async Task CertificateService_Should_Complete_Active_Goal_When_Passed()
    {
        // Arrange
        var mockKafka = new Mock<IKafkaPublisher>();
        var service = new CertificateService(_context, mockKafka.Object);

        var user = new User { Id = 1, Username = "test1", Email = "test1@mail.com", PasswordHash = "hash" };
        _context.Users.Add(user);

        var profile = new LearnerProfile { Id = 10, UserId = 1, Level = EnglishLevel.B1, ActivityStatus = ActivityStatus.Active };
        _context.LearnerProfiles.Add(profile);

        var goal = new GoalSetting
        {
            Id = 100,
            LearnerProfileId = 10,
            Target = "Pass TOEIC 700",
            Type = GoalType.TOEIC,
            TargetValue = 700,
            CurrentValue = 0,
            Status = GoalStatus.Active,
            StartDate = DateTime.UtcNow.AddDays(-1),
            Deadline = DateTime.UtcNow.AddDays(5),
            CreatedAt = DateTime.UtcNow
        };
        _context.GoalSettings.Add(goal);
        await _context.SaveChangesAsync();

        var result = new CertificateTestResult
        {
            LearnerProfileId = 10,
            CertificateType = CertificateType.TOEIC,
            Score = 750,
            TargetScore = 700,
            TakenAt = DateTime.UtcNow,
            SourceQuizAttemptId = 1
        };

        // Act
        var savedResult = await service.RecordResultAsync(result);

        // Assert
        Assert.NotNull(savedResult);
        Assert.True(savedResult.Passed);

        var updatedGoal = await _context.GoalSettings.FindAsync(100);
        Assert.NotNull(updatedGoal);
        Assert.Equal(GoalStatus.Completed, updatedGoal.Status);
        Assert.True(updatedGoal.IsCompleted);
        Assert.Equal(750, updatedGoal.CurrentValue);

        var history = await _context.GoalProgressHistories.FirstOrDefaultAsync(h => h.GoalId == 100);
        Assert.NotNull(history);
        Assert.Equal(0.0, history.PreviousValue);
        Assert.Equal(750.0, history.NewValue);
        Assert.Equal(750.0, history.AddedValue);

        mockKafka.Verify(k => k.PublishGoalCompletedAsync(It.Is<CoreLearningSystem.Application.DTOs.Events.GoalCompletedEvent>(
            e => e.GoalId == 100 && e.LearnerProfileId == 10)), Times.Once);
    }

    // ── 2. FULL PERIODIC SKILL MATRIX RECALCULATION ───────────────────────────────
    [Fact]
    public async Task SkillMatrixRecalculationJob_Should_Recalculate_From_History()
    {
        // Arrange
        var executor = new BackgroundJobExecutor(_context, NullLogger<BackgroundJobExecutor>.Instance);
        var options = Options.Create(new SkillMatrixRecalculationOptions
        {
            Enabled = true,
            DifferenceThreshold = 5.0,
            PeriodKey = "weekly"
        });

        var user = new User { Id = 1, Username = "test", Email = "test@mail.com", PasswordHash = "hash" };
        _context.Users.Add(user);

        var profile = new LearnerProfile { Id = 10, UserId = 1, Level = EnglishLevel.B1, ActivityStatus = ActivityStatus.Active };
        _context.LearnerProfiles.Add(profile);

        // Add a placement result
        var placement = new PlacementTestResult
        {
            LearnerProfileId = 10,
            Score = 5, // Will be scaled to 50
            RecommendedLevel = EnglishLevel.B1,
            TakenAt = DateTime.UtcNow.AddDays(-5)
        };
        _context.PlacementTestResults.Add(placement);

        // Add a quiz attempt with details for Listening skill
        var quiz = new Quiz { Id = 100, Title = "Quiz 1", Level = EnglishLevel.B1 };
        _context.Quizzes.Add(quiz);

        var attempt = new QuizAttempt
        {
            Id = 500,
            QuizId = 100,
            LearnerProfileId = 10,
            Score = 100,
            CorrectAnswersCount = 2,
            IncorrectAnswersCount = 0,
            AttemptedAt = DateTime.UtcNow.AddDays(-2),
            IsPassed = true
        };
        _context.QuizAttempts.Add(attempt);

        var q1 = new Question { Id = 1, QuizId = 100, Skill = SkillType.Listening, Topic = "Intro", Level = EnglishLevel.B1, Content = "Q1" };
        var q2 = new Question { Id = 2, QuizId = 100, Skill = SkillType.Listening, Topic = "Intro", Level = EnglishLevel.B1, Content = "Q2" };
        _context.Questions.AddRange(q1, q2);

        var detail1 = new QuizAttemptDetail { QuizAttemptId = 500, QuestionId = 1, IsCorrect = true };
        var detail2 = new QuizAttemptDetail { QuizAttemptId = 500, QuestionId = 2, IsCorrect = true };
        _context.QuizAttemptDetails.AddRange(detail1, detail2);

        // Initial skill matrix
        var matrix = new SkillMatrix
        {
            LearnerProfileId = 10,
            Skill = SkillType.Listening,
            CurrentScore = 40.0,
            MasteryLevel = MasteryLevel.Weak,
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow
        };
        _context.SkillMatrices.Add(matrix);

        await _context.SaveChangesAsync();

        var job = new SkillMatrixRecalculationJob(_context, executor, options, NullLogger<SkillMatrixRecalculationJob>.Instance);

        // Act
        await job.RunAsync(CancellationToken.None);

        // Assert
        var updatedMatrix = await _context.SkillMatrices.FirstOrDefaultAsync(sm => sm.LearnerProfileId == 10 && sm.Skill == SkillType.Listening);
        Assert.NotNull(updatedMatrix);
        // Placement score: 50. Quiz score: 100.
        // Formula: weight = 2/20 = 0.10 => clamped to 0.15.
        // Next score = (50 * 0.85) + (100 * 0.15) = 42.5 + 15 = 57.5.
        Assert.Equal(57.5, updatedMatrix.CurrentScore);
        Assert.Equal(MasteryLevel.Average, updatedMatrix.MasteryLevel);

        var history = await _context.SkillMatrixHistories
            .FirstOrDefaultAsync(h => h.LearnerProfileId == 10 && h.SourceType == MatrixSourceType.PeriodicRecalculation);
        Assert.NotNull(history);
        Assert.Equal(40.0, history.PreviousScore);
        Assert.Equal(57.5, history.NewScore);
    }

    // ── 3. SESSION CLEANUP ──────────────────────────────────────────────────────────
    [Fact]
    public async Task UserSessionCleanupJob_Should_Mark_Expired_Sessions()
    {
        // Arrange
        var executor = new BackgroundJobExecutor(_context, NullLogger<BackgroundJobExecutor>.Instance);
        
        var user = new User { Id = 1, Username = "test", Email = "test@mail.com", PasswordHash = "hash" };
        _context.Users.Add(user);

        var activeSession = new UserSession
        {
            UserId = 1,
            JwtId = "session-active",
            SessionTokenHash = "hash1",
            Status = SessionStatus.Active,
            ExpiresAt = DateTime.UtcNow.AddHours(2),
            CreatedAt = DateTime.UtcNow
        };

        var expiredSession = new UserSession
        {
            UserId = 1,
            JwtId = "session-expired",
            SessionTokenHash = "hash2",
            Status = SessionStatus.Active,
            ExpiresAt = DateTime.UtcNow.AddMinutes(-5),
            CreatedAt = DateTime.UtcNow.AddHours(-2)
        };

        _context.UserSessions.AddRange(activeSession, expiredSession);
        await _context.SaveChangesAsync();

        var job = new UserSessionCleanupJob(_context, executor, NullLogger<UserSessionCleanupJob>.Instance);

        // Act
        await job.RunAsync(CancellationToken.None);

        // Assert
        var activeInDb = await _context.UserSessions.FirstOrDefaultAsync(s => s.JwtId == "session-active");
        Assert.NotNull(activeInDb);
        Assert.Equal(SessionStatus.Active, activeInDb.Status);

        var expiredInDb = await _context.UserSessions.FirstOrDefaultAsync(s => s.JwtId == "session-expired");
        Assert.NotNull(expiredInDb);
        Assert.Equal(SessionStatus.Expired, expiredInDb.Status);
    }

    // ── 4. SESSION/TOKEN CACHE & REVOCATION ─────────────────────────────────────────
    [Fact]
    public async Task TokenRevocationValidator_Should_Identify_Revoked_Tokens()
    {
        // Arrange
        var mockMux = new Mock<IConnectionMultiplexer>();
        mockMux.Setup(m => m.IsConnected).Returns(true);

        var mockCache = new Mock<ICacheService>();
        mockCache.Setup(c => c.ExistsAsync("adaptive:v1:token-revoked:revoked-jid", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        mockCache.Setup(c => c.ExistsAsync("adaptive:v1:token-revoked:active-jid", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var sessionRepoMock = new Mock<IRepository<UserSession>>();
        
        var activeSessionInDb = new UserSession
        {
            UserId = 1,
            JwtId = "active-jid",
            SessionTokenHash = "hash",
            Status = SessionStatus.Active,
            ExpiresAt = DateTime.UtcNow.AddHours(2)
        };
        sessionRepoMock.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<UserSession, bool>>>()))
            .ReturnsAsync(new List<UserSession> { activeSessionInDb });

        var validator = new TokenRevocationValidator(mockMux.Object, mockCache.Object, sessionRepoMock.Object);

        // Act & Assert
        // Case A: Token is in Redis blacklist => Revoked
        var res1 = await validator.IsTokenRevokedAsync("revoked-jid");
        Assert.True(res1);

        // Case B: Token is not in Redis, but Active in database => Not Revoked
        var res2 = await validator.IsTokenRevokedAsync("active-jid");
        Assert.False(res2);
    }

    // ── 5. DIRECT GRPC LESSON RECOMMENDATION RESPONSE ──────────────────────────────
    [Fact]
    public async Task GrpcService_GenerateRecommendations_Should_Return_Mapped_Lessons()
    {
        // Arrange
        var mockAnalyzer = new Mock<IQuizWeaknessAnalyzer>();
        var mockRecommendation = new Mock<IRecommendationService>();

        var mockResponse = new CoreLearningSystem.Application.DTOs.Common.RecommendationResponse
        {
            OverallReason = "You need grammar improvement.",
            RecommendedLessons = new List<CoreLearningSystem.Application.DTOs.Common.RecommendedLessonDto>
            {
                new() { LessonId = 42, Title = "Advanced Grammar Rules", PriorityScore = 95.0, Reason = "Topic weakness" }
            }
        };

        mockRecommendation.Setup(r => r.GenerateRecommendationsAsync(It.IsAny<CoreLearningSystem.Application.DTOs.Common.RecommendationRequest>()))
            .ReturnsAsync(mockResponse);

        var grpcService = new RecommendationGrpcService(
            mockAnalyzer.Object,
            mockRecommendation.Object,
            NullLogger<RecommendationGrpcService>.Instance);

        var request = new GenerateRecommendationsRequest
        {
            EventId = Guid.NewGuid().ToString(),
            UserId = 1,
            LearnerProfileId = 10,
            WeakestSkill = "Grammar",
            CurrentLevel = "B1",
            MaxRecommendations = 5
        };
        request.WeakTopics.Add("Tense");

        var context = new Mock<ServerCallContext>().Object;

        // Act
        var response = await grpcService.GenerateRecommendations(request, context);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Equal("You need grammar improvement.", response.OverallReason);
        Assert.Single(response.Recommendations);
        Assert.Equal(42, response.Recommendations[0].LessonId);
        Assert.Equal("Advanced Grammar Rules", response.Recommendations[0].Title);
        Assert.Equal(95.0, response.Recommendations[0].PriorityScore);
    }

    // ── 6. RECOMMENDATION EFFECTIVENESS EVALUATION ─────────────────────────────────
    [Fact]
    public async Task RecommendationEffectivenessJob_Should_Evaluate_Recommendations()
    {
        // Arrange
        var executor = new BackgroundJobExecutor(_context, NullLogger<BackgroundJobExecutor>.Instance);
        var options = Options.Create(new RecommendationEffectivenessOptions
        {
            EvaluationWindowDays = 7,
            MinimumImprovementPoints = 10.0
        });

        var user = new User { Id = 1, Username = "test", Email = "test@mail.com", PasswordHash = "hash" };
        _context.Users.Add(user);

        var profile = new LearnerProfile { Id = 10, UserId = 1, Level = EnglishLevel.B1, ActivityStatus = ActivityStatus.Active };
        _context.LearnerProfiles.Add(profile);

        var lesson = new Lesson { Id = 100, Title = "Grammar 1", Skill = SkillType.Grammar, Topic = "Verbs", Level = EnglishLevel.B1 };
        _context.Lessons.Add(lesson);

        // Completed recommendation
        var rec = new Recommendation
        {
            Id = 50,
            LearnerProfileId = 10,
            LessonId = 100,
            Skill = SkillType.Grammar,
            Topic = "Verbs",
            Status = RecommendationStatus.Completed,
            GeneratedAt = DateTime.UtcNow.AddDays(-3),
            CompletedAt = DateTime.UtcNow.AddDays(-2)
        };
        _context.Recommendations.Add(rec);

        // Subsequent quiz attempt containing matching skill/topic
        var quiz = new Quiz { Id = 200, Title = "Quiz 1", Level = EnglishLevel.B1 };
        _context.Quizzes.Add(quiz);

        var attempt = new QuizAttempt
        {
            Id = 600,
            QuizId = 200,
            LearnerProfileId = 10,
            Score = 90.0,
            CorrectAnswersCount = 1,
            IncorrectAnswersCount = 0,
            AttemptedAt = DateTime.UtcNow.AddDays(-1),
            IsPassed = true
        };
        _context.QuizAttempts.Add(attempt);

        var q = new Question { Id = 1, QuizId = 200, Skill = SkillType.Grammar, Topic = "Verbs", Level = EnglishLevel.B1, Content = "Q" };
        _context.Questions.Add(q);

        var detail = new QuizAttemptDetail { QuizAttemptId = 600, QuestionId = 1, IsCorrect = true };
        _context.QuizAttemptDetails.Add(detail);

        // Matrix histories
        var historyBefore = new SkillMatrixHistory
        {
            LearnerProfileId = 10,
            Skill = SkillType.Grammar,
            PreviousScore = 0,
            NewScore = 50.0, // Score before = 50
            RecordedAt = DateTime.UtcNow.AddDays(-4)
        };

        var historyAfter = new SkillMatrixHistory
        {
            LearnerProfileId = 10,
            Skill = SkillType.Grammar,
            PreviousScore = 50.0,
            NewScore = 75.0, // Score after = 75
            SourceType = MatrixSourceType.Quiz,
            SourceId = 600,
            RecordedAt = DateTime.UtcNow.AddDays(-1)
        };

        _context.SkillMatrixHistories.AddRange(historyBefore, historyAfter);
        await _context.SaveChangesAsync();

        var job = new RecommendationEffectivenessJob(_context, executor, options, NullLogger<RecommendationEffectivenessJob>.Instance);

        // Act
        await job.RunAsync(CancellationToken.None);

        // Assert
        var effectiveness = await _context.RecommendationEffectivenesses.FirstOrDefaultAsync(e => e.RecommendationId == 50);
        Assert.NotNull(effectiveness);
        Assert.Equal(50.0, effectiveness.ScoreBefore);
        Assert.Equal(75.0, effectiveness.ScoreAfter);
        Assert.Equal(25.0, effectiveness.Improvement);
        Assert.True(effectiveness.WasEffective);
    }

    // ── 7. REGENERATE RECOMMENDATIONS IF LEARNER DOES NOT IMPROVE ──────────────────
    [Fact]
    public async Task RecommendationRegenerationJob_Should_Trigger_Regeneration()
    {
        // Arrange
        var executor = new BackgroundJobExecutor(_context, NullLogger<BackgroundJobExecutor>.Instance);
        var mockRecService = new Mock<IRecommendationService>();

        var user = new User { Id = 1, Username = "test", Email = "test@mail.com", PasswordHash = "hash" };
        _context.Users.Add(user);

        var profile = new LearnerProfile { Id = 10, UserId = 1, Level = EnglishLevel.B1, ActivityStatus = ActivityStatus.Active };
        _context.LearnerProfiles.Add(profile);

        var lesson = new Lesson { Id = 100, Title = "Grammar 1", Skill = SkillType.Grammar, Topic = "Verbs", Level = EnglishLevel.B1 };
        _context.Lessons.Add(lesson);
        await _context.SaveChangesAsync(); // Save Lesson first to satisfy FK

        var rec = new Recommendation
        {
            Id = 50,
            LearnerProfileId = 10,
            LessonId = 100,
            Skill = SkillType.Grammar,
            Topic = "Verbs",
            Status = RecommendationStatus.Completed,
            GeneratedAt = DateTime.UtcNow.AddDays(-3),
            CompletedAt = DateTime.UtcNow.AddDays(-2)
        };
        _context.Recommendations.Add(rec);

        // Ineffective evaluation
        var effectiveness = new RecommendationEffectiveness
        {
            Id = 1,
            RecommendationId = 50,
            LearnerProfileId = 10,
            LessonId = 100,
            Skill = "Grammar",
            Topic = "Verbs",
            ScoreBefore = 50.0,
            ScoreAfter = 52.0,
            Improvement = 2.0,
            WasEffective = false,
            EvaluatedAt = DateTime.UtcNow.AddDays(-1),
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };
        _context.RecommendationEffectivenesses.Add(effectiveness);

        // Still weak in the skill
        var weakness = new LearnerWeaknessHistory
        {
            LearnerProfileId = 10,
            Skill = SkillType.Grammar,
            Topic = "Verbs",
            Status = WeaknessStatus.Active,
            LastOccurredAt = DateTime.UtcNow.AddDays(-1)
        };
        _context.LearnerWeaknessHistories.Add(weakness);

        await _context.SaveChangesAsync();

        var job = new RecommendationRegenerationJob(_context, mockRecService.Object, executor, NullLogger<RecommendationRegenerationJob>.Instance);

        // Act
        await job.RunAsync(CancellationToken.None);

        // Assert
        var replacedLog = await _context.RecommendationHistories
            .FirstOrDefaultAsync(h => h.RecommendationId == 50 && h.Action == RecommendationAction.Replaced);
        Assert.NotNull(replacedLog);

        mockRecService.Verify(r => r.GenerateRecommendationsAsync(It.Is<RecommendationRequest>(
            req => req.LearnerProfileId == 10 && req.WeakestSkill == SkillType.Grammar && req.WeakTopics.Contains("Verbs"))), Times.Once);
    }

    // ── 8. STATISTICS FOR MOST EFFECTIVE RECOMMENDATIONS ──────────────────────────
    [Fact]
    public async Task RecommendationStatisticsJob_Should_Save_Snapshots()
    {
        // Arrange
        var executor = new BackgroundJobExecutor(_context, NullLogger<BackgroundJobExecutor>.Instance);
        var analyticsService = new RecommendationAnalyticsService(_context);

        var user = new User { Id = 1, Username = "test", Email = "test@mail.com", PasswordHash = "hash" };
        _context.Users.Add(user);

        var profile = new LearnerProfile { Id = 10, UserId = 1, Level = EnglishLevel.B1, ActivityStatus = ActivityStatus.Active };
        _context.LearnerProfiles.Add(profile);

        var lesson = new Lesson { Id = 100, Title = "Grammar 1", Skill = SkillType.Grammar, Topic = "Verbs", Level = EnglishLevel.B1 };
        _context.Lessons.Add(lesson);

        var rec = new Recommendation
        {
            Id = 50,
            LearnerProfileId = 10,
            LessonId = 100,
            Skill = SkillType.Grammar,
            Topic = "Verbs",
            Status = RecommendationStatus.Completed,
            GeneratedAt = DateTime.UtcNow.AddDays(-3),
            CompletedAt = DateTime.UtcNow.AddDays(-2)
        };
        _context.Recommendations.Add(rec);

        var effectiveness = new RecommendationEffectiveness
        {
            Id = 1,
            RecommendationId = 50,
            LearnerProfileId = 10,
            LessonId = 100,
            Skill = "Grammar",
            Topic = "Verbs",
            ScoreBefore = 50.0,
            ScoreAfter = 70.0,
            Improvement = 20.0,
            WasEffective = true,
            EvaluatedAt = DateTime.UtcNow.AddDays(-1),
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };
        _context.RecommendationEffectivenesses.Add(effectiveness);
        await _context.SaveChangesAsync();

        var job = new RecommendationStatisticsJob(analyticsService, executor, NullLogger<RecommendationStatisticsJob>.Instance);

        // Act
        await job.RunAsync(CancellationToken.None);

        // Assert
        var snapshot = await _context.RecommendationStatisticSnapshots.FirstOrDefaultAsync();
        Assert.NotNull(snapshot);
        Assert.Equal(1, snapshot.RecommendationCount);
        Assert.Equal(1, snapshot.CompletionCount);
        Assert.Equal(1, snapshot.EffectiveCount);
        Assert.Equal(1.0, snapshot.EffectivenessRate);
        Assert.Equal(20.0, snapshot.AverageImprovement);
        Assert.Equal(100, snapshot.LessonId);
        Assert.Equal("Grammar", snapshot.Skill);
        Assert.Equal("Verbs", snapshot.Topic);
    }

    // ── 9. OUTBOX PATTERN ──────────────────────────────────────────────────────────
    [Fact]
    public async Task Outbox_Flow_Should_Write_And_Publish_Pending_Messages()
    {
        // Arrange
        var mockProducer = new Mock<IProducer<string, string>>();
        mockProducer.Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeliveryResult<string, string> { Status = PersistenceStatus.Persisted });

        var executor = new BackgroundJobExecutor(_context, NullLogger<BackgroundJobExecutor>.Instance);

        var user = new User { Id = 1, Username = "test", Email = "test@mail.com", PasswordHash = "hash" };
        _context.Users.Add(user);

        var profile = new LearnerProfile { Id = 10, UserId = 1, Level = EnglishLevel.B1, ActivityStatus = ActivityStatus.Active };
        _context.LearnerProfiles.Add(profile);
        await _context.SaveChangesAsync();

        var kafkaPublisher = new KafkaPublisher(mockProducer.Object, _context, NullLogger<KafkaPublisher>.Instance);

        var ev = new CoreLearningSystem.Application.DTOs.Events.LessonCompletedEvent(
            10,
            100,
            "Reading",
            "News",
            "B1",
            DateTime.UtcNow
        );

        // Act 1: Publish event (should write to OutboxMessage table)
        await kafkaPublisher.PublishLessonCompletedAsync(ev);

        // Assert 1: Written to DB as Pending
        var outboxInDb = await _context.OutboxMessages.FirstOrDefaultAsync();
        Assert.NotNull(outboxInDb);
        Assert.Equal(OutboxStatus.Pending, outboxInDb.Status);
        Assert.Contains("News", outboxInDb.Payload);

        var job = new OutboxPublisherJob(_context, mockProducer.Object, executor, NullLogger<OutboxPublisherJob>.Instance);

        // Act 2: Run OutboxPublisherJob to deliver
        await job.RunAsync(CancellationToken.None);

        // Assert 2: Status updated to Published
        var publishedInDb = await _context.OutboxMessages.FirstOrDefaultAsync();
        Assert.NotNull(publishedInDb);
        Assert.Equal(OutboxStatus.Published, publishedInDb.Status);
        Assert.NotNull(publishedInDb.ProcessedAt);

        mockProducer.Verify(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
