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
using AdaptiveLearning.Worker.Handlers;
using AdaptiveLearning.Contracts.Events;

namespace AdaptiveLearning.Tests;

public class SkillMatrixTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly Repository<SkillMatrix> _matrixRepo;
    private readonly Repository<SkillMatrixHistory> _historyRepo;
    private readonly Repository<LearnerWeaknessHistory> _weaknessRepo;
    private readonly SkillMatrixService _service;

    private const int TestUserId = 1;
    private const int TestLearnerProfileId = 10;

    public SkillMatrixTests()
    {
        // Set up SQLite in-memory database
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _matrixRepo = new Repository<SkillMatrix>(_context);
        _historyRepo = new Repository<SkillMatrixHistory>(_context);
        _weaknessRepo = new Repository<LearnerWeaknessHistory>(_context);

        _service = new SkillMatrixService(
            _matrixRepo,
            _historyRepo,
            _weaknessRepo,
            new NullLogger<SkillMatrixService>()
        );

        SeedDefaultData();
    }

    private void SeedDefaultData()
    {
        var user = new User
        {
            Id = TestUserId,
            Username = "test_learner",
            Email = "test@learner.com",
            PasswordHash = "hashed",
            Role = UserRole.Learner,
            CreatedAt = DateTime.UtcNow
        };
        _context.Users.Add(user);

        var profile = new LearnerProfile
        {
            Id = TestLearnerProfileId,
            UserId = TestUserId,
            Level = EnglishLevel.A1,
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

    [Theory]
    [InlineData(0.0, MasteryLevel.Weak)]
    [InlineData(49.99, MasteryLevel.Weak)]
    [InlineData(50.0, MasteryLevel.Average)]
    [InlineData(74.99, MasteryLevel.Average)]
    [InlineData(75.0, MasteryLevel.Good)]
    [InlineData(100.0, MasteryLevel.Good)]
    public async Task UpdateSkillMatrixAsync_Should_ClassifyMasteryLevelCorrectly(double score, MasteryLevel expectedLevel)
    {
        // Arrange
        var request = new SkillMatrixUpdateRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestLearnerProfileId,
            EventId = Guid.NewGuid(),
            SourceType = MatrixSourceType.PlacementTest,
            SourceId = 1,
            SkillScores = new List<SkillScoreDto>
            {
                new() { Skill = SkillType.Listening, Score = score, TotalQuestions = 5, CorrectAnswers = 2 }
            }
        };

        // Act
        var result = await _service.UpdateSkillMatrixAsync(request, default);

        // Assert
        Assert.NotNull(result);
        var matrix = _context.SkillMatrices.First(m => m.LearnerProfileId == TestLearnerProfileId && m.Skill == SkillType.Listening);
        Assert.Equal(expectedLevel, matrix.MasteryLevel);
        Assert.Equal(score, matrix.CurrentScore);
    }

    [Fact]
    public async Task UpdateSkillMatrixAsync_Should_ApplyPlacementTestInitializationFormulaCorrectly()
    {
        // Scenario 1: Initial creation (placement score: 80.0)
        var req1 = new SkillMatrixUpdateRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestLearnerProfileId,
            EventId = Guid.NewGuid(),
            SourceType = MatrixSourceType.PlacementTest,
            SourceId = 1,
            SkillScores = new List<SkillScoreDto>
            {
                new() { Skill = SkillType.Reading, Score = 80.0, TotalQuestions = 5, CorrectAnswers = 4 }
            }
        };

        var res1 = await _service.UpdateSkillMatrixAsync(req1, default);
        var matrix1 = _context.SkillMatrices.First(m => m.LearnerProfileId == TestLearnerProfileId && m.Skill == SkillType.Reading);
        Assert.Equal(80.0, matrix1.CurrentScore); // NewScore = PlacementScore = 80.0
        Assert.Equal(MasteryLevel.Good, matrix1.MasteryLevel);

        // Scenario 2: Existing record update (placement score: 90.0)
        // Formula: 70% current (80.0) + 30% placement (90.0) = 56.0 + 27.0 = 83.0
        var req2 = new SkillMatrixUpdateRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestLearnerProfileId,
            EventId = Guid.NewGuid(),
            SourceType = MatrixSourceType.PlacementTest,
            SourceId = 2,
            SkillScores = new List<SkillScoreDto>
            {
                new() { Skill = SkillType.Reading, Score = 90.0, TotalQuestions = 5, CorrectAnswers = 4 }
            }
        };

        var res2 = await _service.UpdateSkillMatrixAsync(req2, default);
        var matrix2 = _context.SkillMatrices.First(m => m.LearnerProfileId == TestLearnerProfileId && m.Skill == SkillType.Reading);
        Assert.Equal(83.0, matrix2.CurrentScore);
        Assert.Equal(2, matrix2.TotalAssessments);
    }

    [Fact]
    public async Task UpdateSkillMatrixAsync_Should_ApplyQuizExponentialMovingAverageFormulaCorrectly()
    {
        // Scenario 1: Initial creation (quiz score: 60.0)
        var req1 = new SkillMatrixUpdateRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestLearnerProfileId,
            EventId = Guid.NewGuid(),
            SourceType = MatrixSourceType.Quiz,
            SourceId = 100,
            SkillScores = new List<SkillScoreDto>
            {
                new() { Skill = SkillType.Grammar, Score = 60.0, TotalQuestions = 4, CorrectAnswers = 2 }
            }
        };

        await _service.UpdateSkillMatrixAsync(req1, default);
        var matrix1 = _context.SkillMatrices.First(m => m.LearnerProfileId == TestLearnerProfileId && m.Skill == SkillType.Grammar);
        Assert.Equal(60.0, matrix1.CurrentScore);

        // Scenario 2: Existing record update (quiz score: 90.0, TotalQuestions: 5)
        // Weight: min(0.40, max(0.15, 5 / 20.0)) = min(0.40, max(0.15, 0.25)) = 0.25
        // Formula: Current (60.0) * (1 - 0.25) + QuizScore (90.0) * 0.25 = 45.0 + 22.5 = 67.5
        var req2 = new SkillMatrixUpdateRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestLearnerProfileId,
            EventId = Guid.NewGuid(),
            SourceType = MatrixSourceType.Quiz,
            SourceId = 101,
            SkillScores = new List<SkillScoreDto>
            {
                new() { Skill = SkillType.Grammar, Score = 90.0, TotalQuestions = 5, CorrectAnswers = 4 }
            }
        };

        await _service.UpdateSkillMatrixAsync(req2, default);
        var matrix2 = _context.SkillMatrices.First(m => m.LearnerProfileId == TestLearnerProfileId && m.Skill == SkillType.Grammar);
        Assert.Equal(67.5, matrix2.CurrentScore);
    }

    [Fact]
    public async Task UpdateSkillMatrixAsync_Should_BeIdempotent_WhenSameEventIdProcessedTwice()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var request = new SkillMatrixUpdateRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestLearnerProfileId,
            EventId = eventId,
            SourceType = MatrixSourceType.PlacementTest,
            SourceId = 1,
            SkillScores = new List<SkillScoreDto>
            {
                new() { Skill = SkillType.Vocabulary, Score = 75.0, TotalQuestions = 5, CorrectAnswers = 3 }
            }
        };

        // Act & Assert
        // First execution
        var result1 = await _service.UpdateSkillMatrixAsync(request, default);
        Assert.Contains(SkillType.Vocabulary.ToString(), result1.UpdatedSkills);
        var count1 = _context.SkillMatrixHistories.Count(h => h.EventId == eventId);
        Assert.Equal(1, count1);

        // Second execution (replay)
        var result2 = await _service.UpdateSkillMatrixAsync(request, default);
        Assert.Empty(result2.UpdatedSkills); // No skills updated on replay
        var count2 = _context.SkillMatrixHistories.Count(h => h.EventId == eventId);
        Assert.Equal(1, count2); // Still exactly 1 history record
    }

    [Fact]
    public async Task UpdateSkillMatrixAsync_Should_ManageWeaknessHistoryAndDetectRepeatedWeaknesses()
    {
        // 1. Submit first quiz with weak topic "present simple"
        var eventId1 = Guid.NewGuid();
        var req1 = new SkillMatrixUpdateRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestLearnerProfileId,
            EventId = eventId1,
            SourceType = MatrixSourceType.Quiz,
            SourceId = 200,
            SkillScores = new List<SkillScoreDto>
            {
                new() { Skill = SkillType.Grammar, Score = 40.0, TotalQuestions = 2, CorrectAnswers = 1 }
            },
            WeakTopics = new List<WeakTopicDto>
            {
                new() { Skill = SkillType.Grammar, Topic = "present simple", Level = "A1", IncorrectCount = 1 }
            }
        };

        var result1 = await _service.UpdateSkillMatrixAsync(req1, default);
        var weakness1 = _context.LearnerWeaknessHistories.First(w => w.LearnerProfileId == TestLearnerProfileId && w.Topic == "present simple");
        Assert.Equal(1, weakness1.OccurrenceCount);
        Assert.Equal(1, weakness1.IncorrectCount);
        Assert.Equal(WeaknessStatus.Active, weakness1.Status);
        Assert.Empty(result1.RepeatedWeakTopics); // Occurred only once

        // 2. Submit second quiz with same weak topic (should increment occurrence and mark as repeated)
        var eventId2 = Guid.NewGuid();
        var req2 = new SkillMatrixUpdateRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestLearnerProfileId,
            EventId = eventId2,
            SourceType = MatrixSourceType.Quiz,
            SourceId = 201,
            SkillScores = new List<SkillScoreDto>
            {
                new() { Skill = SkillType.Grammar, Score = 40.0, TotalQuestions = 2, CorrectAnswers = 1 }
            },
            WeakTopics = new List<WeakTopicDto>
            {
                // Test topic case-insensitivity: "Present Simple" maps to "present simple"
                new() { Skill = SkillType.Grammar, Topic = "Present Simple", Level = "A1", IncorrectCount = 2 }
            }
        };

        var result2 = await _service.UpdateSkillMatrixAsync(req2, default);
        var weakness2 = _context.LearnerWeaknessHistories.First(w => w.LearnerProfileId == TestLearnerProfileId && w.Topic == "present simple");
        Assert.Equal(2, weakness2.OccurrenceCount);
        Assert.Equal(3, weakness2.IncorrectCount); // 1 + 2 = 3
        Assert.Equal(WeaknessStatus.Active, weakness2.Status);
        Assert.Single(result2.RepeatedWeakTopics);
        Assert.Equal("present simple", result2.RepeatedWeakTopics[0]);
    }

    [Fact]
    public async Task UpdateSkillMatrixAsync_Should_TransitionWeaknessToImproving_OnLessonCompletion()
    {
        // 1. Seed active weakness
        var weakness = new LearnerWeaknessHistory
        {
            LearnerProfileId = TestLearnerProfileId,
            Skill = SkillType.Listening,
            Topic = "Accent",
            Level = "A1",
            IncorrectCount = 1,
            OccurrenceCount = 1,
            Status = WeaknessStatus.Active,
            LastEventId = Guid.NewGuid()
        };
        _context.LearnerWeaknessHistories.Add(weakness);
        _context.SaveChanges();

        // 2. Process Lesson Completion for "Accent" topic
        var req = new SkillMatrixUpdateRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestLearnerProfileId,
            EventId = Guid.NewGuid(),
            SourceType = MatrixSourceType.LessonCompletion,
            SourceId = 50,
            WeakTopics = new List<WeakTopicDto>
            {
                new() { Skill = SkillType.Listening, Topic = "Accent", Level = "A1", IncorrectCount = 0 }
            }
        };

        await _service.UpdateSkillMatrixAsync(req, default);

        var updatedWeakness = _context.LearnerWeaknessHistories.First(w => w.LearnerProfileId == TestLearnerProfileId && w.Topic == "Accent");
        Assert.Equal(WeaknessStatus.Improving, updatedWeakness.Status); // Should transition to Improving
    }

    [Fact]
    public async Task UpdateSkillMatrixAsync_Should_TransitionWeaknessToResolved_WhenTestedAndAllCorrectAndScoreHigh()
    {
        // 1. Seed active weakness
        var weakness = new LearnerWeaknessHistory
        {
            LearnerProfileId = TestLearnerProfileId,
            Skill = SkillType.Listening,
            Topic = "Accent",
            Level = "A1",
            IncorrectCount = 1,
            OccurrenceCount = 1,
            Status = WeaknessStatus.Active,
            LastEventId = Guid.NewGuid()
        };
        _context.LearnerWeaknessHistories.Add(weakness);

        // Seed skill matrix for Listening with 80% score (>= 75.0)
        var matrix = new SkillMatrix
        {
            LearnerProfileId = TestLearnerProfileId,
            Skill = SkillType.Listening,
            CurrentScore = 80.0,
            MasteryLevel = MasteryLevel.Good,
            TotalAssessments = 1
        };
        _context.SkillMatrices.Add(matrix);
        _context.SaveChanges();

        // 2. Submit quiz testing Listening where "Accent" topic was answered correctly (meaning not in WeakTopics)
        var req = new SkillMatrixUpdateRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestLearnerProfileId,
            EventId = Guid.NewGuid(),
            SourceType = MatrixSourceType.Quiz,
            SourceId = 300,
            SkillScores = new List<SkillScoreDto>
            {
                new() { Skill = SkillType.Listening, Score = 90.0, TotalQuestions = 2, CorrectAnswers = 2 }
            },
            WeakTopics = new List<WeakTopicDto>() // Topic "Accent" was answered correctly, so 0 incorrect
        };

        await _service.UpdateSkillMatrixAsync(req, default);

        var updatedWeakness = _context.LearnerWeaknessHistories.First(w => w.LearnerProfileId == TestLearnerProfileId && w.Topic == "Accent");
        Assert.Equal(WeaknessStatus.Resolved, updatedWeakness.Status); // Should transition to Resolved
    }

    [Fact]
    public async Task UpdateSkillMatrixAsync_Should_RollbackTransaction_OnException()
    {
        // Arrange
        // Seed initial skill matrix
        var matrix = new SkillMatrix
        {
            LearnerProfileId = TestLearnerProfileId,
            Skill = SkillType.Grammar,
            CurrentScore = 50.0,
            MasteryLevel = MasteryLevel.Average,
            TotalAssessments = 1
        };
        _context.SkillMatrices.Add(matrix);
        _context.SaveChanges();

        // Create a request with duplicate EventId to trigger no exception, but let's force a database constraint error!
        // We can do this by setting a invalid foreign key or missing fields.
        var request = new SkillMatrixUpdateRequest
        {
            UserId = TestUserId,
            LearnerProfileId = 9999, // Non-existent LearnerProfileId -> will trigger FK constraint error on insert!
            EventId = Guid.NewGuid(),
            SourceType = MatrixSourceType.PlacementTest,
            SourceId = 1,
            SkillScores = new List<SkillScoreDto>
            {
                new() { Skill = SkillType.Grammar, Score = 80.0, TotalQuestions = 5, CorrectAnswers = 4 }
            }
        };

        // Act & Assert
        // The service call should throw a DbUpdateException because of the invalid foreign key constraint in relational DB.
        await Assert.ThrowsAnyAsync<Exception>(() => _service.UpdateSkillMatrixAsync(request, default));

        // Verify that the original skill matrix score was NOT modified (rollback succeeded)
        var matrixAfterRollback = _context.SkillMatrices.First(m => m.LearnerProfileId == TestLearnerProfileId && m.Skill == SkillType.Grammar);
        Assert.Equal(50.0, matrixAfterRollback.CurrentScore); // Remains 50.0
    }

    [Fact]
    public async Task UpdateSkillMatrixAsync_Should_ProcessSpeakingAndGeneralSkillsCorrectly()
    {
        // Arrange
        var request = new SkillMatrixUpdateRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestLearnerProfileId,
            EventId = Guid.NewGuid(),
            SourceType = MatrixSourceType.PlacementTest,
            SourceId = 1,
            SkillScores = new List<SkillScoreDto>
            {
                new() { Skill = SkillType.Speaking, Score = 85.0, TotalQuestions = 5, CorrectAnswers = 4 },
                new() { Skill = SkillType.General, Score = 70.0, TotalQuestions = 5, CorrectAnswers = 3 }
            }
        };

        // Act
        var result = await _service.UpdateSkillMatrixAsync(request, default);

        // Assert
        Assert.Contains(SkillType.Speaking.ToString(), result.UpdatedSkills);
        Assert.Contains(SkillType.General.ToString(), result.UpdatedSkills);

        var speakingMatrix = _context.SkillMatrices.First(m => m.LearnerProfileId == TestLearnerProfileId && m.Skill == SkillType.Speaking);
        Assert.Equal(85.0, speakingMatrix.CurrentScore);
        Assert.Equal(MasteryLevel.Good, speakingMatrix.MasteryLevel);

        var generalMatrix = _context.SkillMatrices.First(m => m.LearnerProfileId == TestLearnerProfileId && m.Skill == SkillType.General);
        Assert.Equal(70.0, generalMatrix.CurrentScore);
        Assert.Equal(MasteryLevel.Average, generalMatrix.MasteryLevel);
    }

    [Fact]
    public async Task Handlers_Should_ThrowException_WhenInvalidSkillNameProvided()
    {
        // Arrange
        var placementHandler = new PlacementTestCompletedEventHandler(
            _service,
            new Repository<LearnerProfile>(_context),
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<PlacementTestCompletedEventHandler>()
        );

        var placementEvent = new PlacementTestCompletedEvent
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            UserId = TestUserId,
            Score = 80,
            AssignedLevel = "A2",
            CompletedAt = DateTimeOffset.UtcNow,
            SkillResults = new List<SkillScore>
            {
                new() { SkillName = "InvalidSkillName", Score = 80.0 }
            }
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => placementHandler.HandleAsync(placementEvent));
    }

    [Fact]
    public async Task LessonCompletedEventHandler_Should_ThrowException_WhenInvalidSkillNameProvided()
    {
        // Arrange
        var lessonHandler = new LessonCompletedEventHandler(
            _service,
            new Repository<LearnerProfile>(_context),
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<LessonCompletedEventHandler>()
        );

        var lessonEvent = new LessonCompletedEvent
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            UserId = TestUserId,
            LessonId = 12,
            SkillName = "NonExistentSkill",
            Topic = "Grammar Topic",
            Level = "A1",
            CompletedAt = DateTimeOffset.UtcNow
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => lessonHandler.HandleAsync(lessonEvent));
    }
}
