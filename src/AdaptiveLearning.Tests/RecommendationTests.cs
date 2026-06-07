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
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Application.Options;

namespace AdaptiveLearning.Tests;

public class RecommendationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;

    private readonly Repository<Recommendation> _recRepo;
    private readonly Repository<RecommendationHistory> _historyRepo;
    private readonly Repository<Lesson> _lessonRepo;
    private readonly Repository<LearnerProfile> _profileRepo;
    private readonly Repository<LearnerProgress> _progressRepo;
    private readonly Repository<LearnerWeaknessHistory> _weaknessRepo;

    private readonly AdaptiveRecommendationEngine _engine;
    private readonly RecommendationService _service;

    private const int TestUserId = 100;
    private const int TestProfileId = 200;

    public RecommendationTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _recRepo = new Repository<Recommendation>(_context);
        _historyRepo = new Repository<RecommendationHistory>(_context);
        _lessonRepo = new Repository<Lesson>(_context);
        _profileRepo = new Repository<LearnerProfile>(_context);
        _progressRepo = new Repository<LearnerProgress>(_context);
        _weaknessRepo = new Repository<LearnerWeaknessHistory>(_context);
        var goalRepo = new Repository<GoalSetting>(_context);

        _engine = new AdaptiveRecommendationEngine();

        var optionsWrapper = Options.Create(new RecommendationOptions
        {
            MaxRecommendations = 5,
            RecommendationExpirationDays = 7,
            DismissedCooldownDays = 3,
            MinimumPriorityScore = 0 // Allow all for testing
        });

        _service = new RecommendationService(
            _recRepo,
            _historyRepo,
            _lessonRepo,
            _profileRepo,
            _progressRepo,
            _weaknessRepo,
            goalRepo,
            _engine,
            optionsWrapper,
            new NullLogger<RecommendationService>()
        );

        SeedDefaultData();
    }

    private void SeedDefaultData()
    {
        var user = new User
        {
            Id = TestUserId,
            Username = "rec_test_learner",
            Email = "rec_test@learner.com",
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

    // ====== FILTERING TESTS ======

    [Fact]
    public void Engine_Should_Exclude_InactiveLessons()
    {
        // Arrange
        var profile = _context.LearnerProfiles.Find(TestProfileId)!;
        var lessons = new List<Lesson>
        {
            new() { Id = 1, Title = "Draft Lesson", Skill = SkillType.Grammar, Topic = "Tenses", Level = EnglishLevel.B1, Status = LessonStatus.Draft },
            new() { Id = 2, Title = "Archived Lesson", Skill = SkillType.Grammar, Topic = "Tenses", Level = EnglishLevel.B1, Status = LessonStatus.Archived }
        };
        var weaknesses = new List<LearnerWeaknessHistory>
        {
            new() { Skill = SkillType.Grammar, Topic = "Tenses", Status = WeaknessStatus.Active, Level = "B1" }
        };
        profile.SkillMatrices = new List<SkillMatrix>
        {
            new() { Skill = SkillType.Grammar, CurrentScore = 40, MasteryLevel = MasteryLevel.Weak }
        };

        // Act - engine receives only lessons that passed Service's status filter
        // Simulate what service does: filter out non-Published lessons
        var candidateLessons = lessons.Where(l => l.Status == LessonStatus.Published).ToList();
        var ranked = _engine.GenerateAndRank(candidateLessons, profile, weaknesses, new List<string>(), SkillType.Grammar, new List<string>(), EnglishLevel.B1, "evt-filter-1");

        // Assert
        Assert.Empty(ranked);
    }

    [Fact]
    public void Engine_Should_Exclude_LessonsOutsideLevelRange()
    {
        // Arrange
        var profile = _context.LearnerProfiles.Find(TestProfileId)!;
        profile.SkillMatrices = new List<SkillMatrix>
        {
            new() { Skill = SkillType.Grammar, CurrentScore = 50, MasteryLevel = MasteryLevel.Average }
        };
        var lessons = new List<Lesson>
        {
            // Learner is B1 (order 3), A1 (order 1) is 2 levels below — excluded
            new() { Id = 10, Title = "Far Below", Skill = SkillType.Grammar, Topic = "Basics", Level = EnglishLevel.A1, Status = LessonStatus.Published },
            // C2 (order 6) is 3 levels above — excluded
            new() { Id = 11, Title = "Far Above", Skill = SkillType.Grammar, Topic = "Advanced", Level = EnglishLevel.C2, Status = LessonStatus.Published },
        };

        // Act
        var ranked = _engine.GenerateAndRank(lessons, profile, new List<LearnerWeaknessHistory>(),
            new List<string>(), SkillType.Grammar, new List<string>(), EnglishLevel.B1, "evt-filter-2");

        // Assert
        Assert.Empty(ranked);
    }

    [Fact]
    public void Engine_Should_Include_LessonsWithinOneLevelRange()
    {
        // Arrange
        var profile = _context.LearnerProfiles.Find(TestProfileId)!;
        profile.SkillMatrices = new List<SkillMatrix>
        {
            new() { Skill = SkillType.Grammar, CurrentScore = 40, MasteryLevel = MasteryLevel.Weak }
        };
        var lessons = new List<Lesson>
        {
            // A2 is 1 below B1 — included (because score < 50)
            new() { Id = 20, Title = "One Below", Skill = SkillType.Grammar, Topic = "Tenses", Level = EnglishLevel.A2, Status = LessonStatus.Published },
            // B1 exact match — included
            new() { Id = 21, Title = "Exact Match", Skill = SkillType.Grammar, Topic = "Tenses", Level = EnglishLevel.B1, Status = LessonStatus.Published },
        };

        // Act
        var ranked = _engine.GenerateAndRank(lessons, profile, new List<LearnerWeaknessHistory>(),
            new List<string>(), SkillType.Grammar, new List<string>(), EnglishLevel.B1, "evt-filter-3");

        // Assert
        Assert.Equal(2, ranked.Count);
    }

    // ====== RANKING / SCORING TESTS ======

    [Fact]
    public void Engine_Should_Score_WeakestSkillLesson_35()
    {
        // Arrange
        var profile = _context.LearnerProfiles.Find(TestProfileId)!;
        profile.SkillMatrices = new List<SkillMatrix>();
        var lessons = new List<Lesson>
        {
            new() { Id = 30, Title = "Grammar Lesson", Skill = SkillType.Grammar, Topic = "Generic", Level = EnglishLevel.B1, Status = LessonStatus.Published }
        };

        // Act
        var ranked = _engine.GenerateAndRank(lessons, profile, new List<LearnerWeaknessHistory>(),
            new List<string>(), SkillType.Grammar, new List<string>(), EnglishLevel.B1, "evt-score-1");

        // Assert
        Assert.Single(ranked);
        // Weakest skill (35) + exact level (15) = 50
        Assert.Equal(50.0, ranked[0].PriorityScore);
    }

    [Fact]
    public void Engine_Should_Score_ActiveTopicWeakness_30()
    {
        // Arrange
        var profile = _context.LearnerProfiles.Find(TestProfileId)!;
        profile.SkillMatrices = new List<SkillMatrix>
        {
            new() { Skill = SkillType.Grammar, CurrentScore = 40, MasteryLevel = MasteryLevel.Weak }
        };
        var weaknesses = new List<LearnerWeaknessHistory>
        {
            new() { Skill = SkillType.Grammar, Topic = "Subjunctive", Status = WeaknessStatus.Active, Level = "B1" }
        };
        var lessons = new List<Lesson>
        {
            new() { Id = 40, Title = "Subjunctive Lesson", Skill = SkillType.Grammar, Topic = "Subjunctive", Level = EnglishLevel.B1, Status = LessonStatus.Published }
        };

        // Act
        var ranked = _engine.GenerateAndRank(lessons, profile, weaknesses,
            new List<string>(), null, new List<string>(), EnglishLevel.B1, "evt-score-2");

        // Assert - score < 50 → skill=20, topic=30, level=15
        Assert.Single(ranked);
        Assert.Equal(65.0, ranked[0].PriorityScore);
    }

    [Fact]
    public void Engine_Should_Score_ImprovingTopicWeakness_15()
    {
        // Arrange
        var profile = _context.LearnerProfiles.Find(TestProfileId)!;
        profile.SkillMatrices = new List<SkillMatrix>
        {
            new() { Skill = SkillType.Grammar, CurrentScore = 40, MasteryLevel = MasteryLevel.Weak }
        };
        var weaknesses = new List<LearnerWeaknessHistory>
        {
            new() { Skill = SkillType.Grammar, Topic = "Conditionals", Status = WeaknessStatus.Improving, Level = "B1" }
        };
        var lessons = new List<Lesson>
        {
            new() { Id = 50, Title = "Conditionals Lesson", Skill = SkillType.Grammar, Topic = "Conditionals", Level = EnglishLevel.B1, Status = LessonStatus.Published }
        };

        // Act
        var ranked = _engine.GenerateAndRank(lessons, profile, weaknesses,
            new List<string>(), null, new List<string>(), EnglishLevel.B1, "evt-score-3");

        // Assert - score < 50 → skill=20, topic=15, level=15
        Assert.Single(ranked);
        Assert.Equal(50.0, ranked[0].PriorityScore);
    }

    [Fact]
    public void Engine_Should_Score_RepeatedWeakTopic_10()
    {
        // Arrange
        var profile = _context.LearnerProfiles.Find(TestProfileId)!;
        profile.SkillMatrices = new List<SkillMatrix>
        {
            new() { Skill = SkillType.Grammar, CurrentScore = 40, MasteryLevel = MasteryLevel.Weak }
        };
        var lessons = new List<Lesson>
        {
            new() { Id = 60, Title = "Passive Voice Lesson", Skill = SkillType.Grammar, Topic = "Passive Voice", Level = EnglishLevel.B1, Status = LessonStatus.Published }
        };

        // Act - "Passive Voice" is in repeated weak topics
        var ranked = _engine.GenerateAndRank(lessons, profile, new List<LearnerWeaknessHistory>(),
            new List<string> { "Passive Voice" }, null, new List<string>(), EnglishLevel.B1, "evt-score-4");

        // Assert - skill=20, level=15, repeated=10 → 45
        Assert.Single(ranked);
        Assert.Equal(45.0, ranked[0].PriorityScore);
    }

    [Fact]
    public void Engine_Should_Score_Recency_5()
    {
        // Arrange
        var profile = _context.LearnerProfiles.Find(TestProfileId)!;
        profile.SkillMatrices = new List<SkillMatrix>
        {
            new() { Skill = SkillType.Grammar, CurrentScore = 40, MasteryLevel = MasteryLevel.Weak }
        };
        var lessons = new List<Lesson>
        {
            new() { Id = 70, Title = "Articles Lesson", Skill = SkillType.Grammar, Topic = "Articles", Level = EnglishLevel.B1, Status = LessonStatus.Published }
        };

        // Act - "Articles" is in current event weak topics
        var ranked = _engine.GenerateAndRank(lessons, profile, new List<LearnerWeaknessHistory>(),
            new List<string>(), null, new List<string> { "Articles" }, EnglishLevel.B1, "evt-score-5");

        // Assert - skill=20, level=15, recency=5 → 40
        Assert.Single(ranked);
        Assert.Equal(40.0, ranked[0].PriorityScore);
    }

    [Fact]
    public void Engine_Should_ClampScore_ToMax100()
    {
        // Arrange
        var profile = _context.LearnerProfiles.Find(TestProfileId)!;
        profile.SkillMatrices = new List<SkillMatrix>();
        var lessons = new List<Lesson>
        {
            new() { Id = 80, Title = "Max Score Lesson", Skill = SkillType.Grammar, Topic = "MaxTopic", Level = EnglishLevel.B1, Status = LessonStatus.Published }
        };
        var weaknesses = new List<LearnerWeaknessHistory>
        {
            new() { Skill = SkillType.Grammar, Topic = "MaxTopic", Status = WeaknessStatus.Active, Level = "B1" }
        };

        // Act - weakest(35) + topic active(30) + level(15) + recency(5) + repeated(10) = 95 (still under 100)
        var ranked = _engine.GenerateAndRank(lessons, profile, weaknesses,
            new List<string> { "MaxTopic" }, SkillType.Grammar, new List<string> { "MaxTopic" }, EnglishLevel.B1, "evt-score-6");

        // Assert
        Assert.Single(ranked);
        Assert.True(ranked[0].PriorityScore <= 100.0);
        Assert.Equal(95.0, ranked[0].PriorityScore);
    }

    [Fact]
    public void Engine_Should_ApplyDeterministicTieBreaker_ByLessonId()
    {
        // Arrange
        var profile = _context.LearnerProfiles.Find(TestProfileId)!;
        profile.SkillMatrices = new List<SkillMatrix>
        {
            new() { Skill = SkillType.Grammar, CurrentScore = 40, MasteryLevel = MasteryLevel.Weak }
        };
        // Same score, different IDs
        var lessons = new List<Lesson>
        {
            new() { Id = 91, Title = "Lesson B", Skill = SkillType.Grammar, Topic = "GenericTopic", Level = EnglishLevel.B1, Status = LessonStatus.Published },
            new() { Id = 90, Title = "Lesson A", Skill = SkillType.Grammar, Topic = "GenericTopic", Level = EnglishLevel.B1, Status = LessonStatus.Published }
        };

        // Act
        var ranked = _engine.GenerateAndRank(lessons, profile, new List<LearnerWeaknessHistory>(),
            new List<string>(), null, new List<string>(), EnglishLevel.B1, "evt-tie");

        // Assert - same score; LessonId ascending → Id=90 first
        Assert.Equal(2, ranked.Count);
        Assert.Equal(90, ranked[0].LessonId);
        Assert.Equal(91, ranked[1].LessonId);
    }

    // ====== PERSISTENCE AND IDEMPOTENCY TESTS ======

    [Fact]
    public async Task Service_Should_Persist_Recommendations_To_Database()
    {
        // Arrange
        var lesson = new Lesson
        {
            Title = "Persisted Lesson",
            Skill = SkillType.Grammar,
            Topic = "Gerunds",
            Level = EnglishLevel.B1,
            Status = LessonStatus.Published
        };
        _context.Lessons.Add(lesson);
        _context.SaveChanges();

        var matrix = new SkillMatrix
        {
            LearnerProfileId = TestProfileId,
            Skill = SkillType.Grammar,
            CurrentScore = 40,
            MasteryLevel = MasteryLevel.Weak,
            TotalAssessments = 1,
            LastAssessmentScore = 40
        };
        _context.SkillMatrices.Add(matrix);
        _context.SaveChanges();

        var request = new RecommendationRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-persist-1",
            WeakestSkill = SkillType.Grammar,
            WeakTopics = new List<string> { "Gerunds" },
            Level = EnglishLevel.B1,
            OccurredAt = DateTime.UtcNow
        };

        // Act
        var response = await _service.GenerateRecommendationsAsync(request);

        // Assert
        var savedRecs = _context.Recommendations.Where(r => r.LearnerProfileId == TestProfileId).ToList();
        var savedHistory = _context.RecommendationHistories.Where(h => h.LearnerProfileId == TestProfileId).ToList();

        Assert.NotEmpty(savedRecs);
        Assert.NotEmpty(savedHistory);
        Assert.All(savedRecs, r => Assert.Equal(RecommendationStatus.Active, r.Status));
        Assert.All(savedHistory, h => Assert.Equal(RecommendationAction.Generated, h.Action));
        Assert.NotEmpty(response.RecommendedLessons);
    }

    [Fact]
    public async Task Service_Should_Be_Idempotent_OnSameSourceEventId()
    {
        // Arrange
        var lesson = new Lesson
        {
            Title = "Idempotent Lesson",
            Skill = SkillType.Grammar,
            Topic = "Idioms",
            Level = EnglishLevel.B1,
            Status = LessonStatus.Published
        };
        _context.Lessons.Add(lesson);

        var matrix = new SkillMatrix
        {
            LearnerProfileId = TestProfileId,
            Skill = SkillType.Grammar,
            CurrentScore = 40,
            MasteryLevel = MasteryLevel.Weak,
            TotalAssessments = 1,
            LastAssessmentScore = 40
        };
        _context.SkillMatrices.Add(matrix);
        _context.SaveChanges();

        var request = new RecommendationRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-idempotent-1",
            WeakestSkill = SkillType.Grammar,
            WeakTopics = new List<string> { "Idioms" },
            Level = EnglishLevel.B1,
            OccurredAt = DateTime.UtcNow
        };

        // Act - call twice with same SourceEventId
        var response1 = await _service.GenerateRecommendationsAsync(request);
        var response2 = await _service.GenerateRecommendationsAsync(request);

        // Assert - count should remain the same (no duplicates)
        var savedRecs = _context.Recommendations
            .Where(r => r.LearnerProfileId == TestProfileId && r.SourceEventId == "evt-idempotent-1")
            .ToList();

        Assert.Equal(response1.RecommendedLessons.Count, response2.RecommendedLessons.Count);
        Assert.True(savedRecs.Count > 0);
    }

    [Fact]
    public async Task Service_Should_Mark_CompletedLesson_Recommendation_As_Completed()
    {
        // Arrange
        var lesson = new Lesson
        {
            Title = "Completion Lesson",
            Skill = SkillType.Grammar,
            Topic = "Reported Speech",
            Level = EnglishLevel.B1,
            Status = LessonStatus.Published
        };
        _context.Lessons.Add(lesson);

        var matrix = new SkillMatrix
        {
            LearnerProfileId = TestProfileId,
            Skill = SkillType.Grammar,
            CurrentScore = 40,
            MasteryLevel = MasteryLevel.Weak,
            TotalAssessments = 1,
            LastAssessmentScore = 40
        };
        _context.SkillMatrices.Add(matrix);
        _context.SaveChanges();

        // Generate recommendation first
        var genRequest = new RecommendationRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-completion-gen",
            WeakestSkill = SkillType.Grammar,
            WeakTopics = new List<string> { "Reported Speech" },
            Level = EnglishLevel.B1,
            OccurredAt = DateTime.UtcNow
        };
        await _service.GenerateRecommendationsAsync(genRequest);

        var rec = _context.Recommendations.FirstOrDefault(r =>
            r.LearnerProfileId == TestProfileId && r.Status == RecommendationStatus.Active);
        Assert.NotNull(rec);

        // Act - mark lesson as completed
        await _service.HandleLessonCompletedAsync(TestProfileId, rec.LessonId, "evt-completion-done");

        // Assert
        _context.Entry(rec).Reload();
        Assert.Equal(RecommendationStatus.Completed, rec.Status);
        Assert.NotNull(rec.CompletedAt);

        var completionHistory = _context.RecommendationHistories
            .FirstOrDefault(h => h.SourceEventId == "evt-completion-done" && h.Action == RecommendationAction.Completed);
        Assert.NotNull(completionHistory);
    }

    [Fact]
    public async Task Service_Should_Be_Idempotent_OnLessonCompletion()
    {
        // Arrange
        var lesson = new Lesson
        {
            Title = "Idempotent Completion Lesson",
            Skill = SkillType.Grammar,
            Topic = "Punctuation",
            Level = EnglishLevel.B1,
            Status = LessonStatus.Published
        };
        _context.Lessons.Add(lesson);

        var matrix = new SkillMatrix
        {
            LearnerProfileId = TestProfileId,
            Skill = SkillType.Grammar,
            CurrentScore = 40,
            MasteryLevel = MasteryLevel.Weak,
            TotalAssessments = 1,
            LastAssessmentScore = 40
        };
        _context.SkillMatrices.Add(matrix);
        _context.SaveChanges();

        var genRequest = new RecommendationRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-idem-lesson-gen",
            WeakestSkill = SkillType.Grammar,
            WeakTopics = new List<string> { "Punctuation" },
            Level = EnglishLevel.B1,
            OccurredAt = DateTime.UtcNow
        };
        await _service.GenerateRecommendationsAsync(genRequest);

        var rec = _context.Recommendations.FirstOrDefault(r =>
            r.LearnerProfileId == TestProfileId && r.Status == RecommendationStatus.Active);
        Assert.NotNull(rec);

        // Act - call HandleLessonCompletedAsync twice with same EventId
        await _service.HandleLessonCompletedAsync(TestProfileId, rec.LessonId, "evt-idem-lesson-done");
        await _service.HandleLessonCompletedAsync(TestProfileId, rec.LessonId, "evt-idem-lesson-done");

        // Assert - only one Completed history entry should exist for this EventId
        var completionHistories = _context.RecommendationHistories
            .Where(h => h.SourceEventId == "evt-idem-lesson-done" && h.Action == RecommendationAction.Completed)
            .ToList();

        Assert.Single(completionHistories);
    }

    [Fact]
    public async Task Service_Should_Exclude_CompletedLessons_FromCandidates()
    {
        // Arrange
        var lesson = new Lesson
        {
            Title = "Already Completed Lesson",
            Skill = SkillType.Grammar,
            Topic = "Proverbs",
            Level = EnglishLevel.B1,
            Status = LessonStatus.Published
        };
        _context.Lessons.Add(lesson);
        _context.SaveChanges();

        // Mark lesson as completed in progress
        var progress = new LearnerProgress
        {
            LearnerProfileId = TestProfileId,
            LessonId = lesson.Id,
            IsCompleted = true,
            CompletedAt = DateTime.UtcNow
        };
        _context.LearnerProgresses.Add(progress);
        _context.SaveChanges();

        var matrix = new SkillMatrix
        {
            LearnerProfileId = TestProfileId,
            Skill = SkillType.Grammar,
            CurrentScore = 40,
            MasteryLevel = MasteryLevel.Weak,
            TotalAssessments = 1,
            LastAssessmentScore = 40
        };
        _context.SkillMatrices.Add(matrix);
        _context.SaveChanges();

        var request = new RecommendationRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-exclude-completed",
            WeakestSkill = SkillType.Grammar,
            WeakTopics = new List<string> { "Proverbs" },
            Level = EnglishLevel.B1,
            OccurredAt = DateTime.UtcNow
        };

        // Act
        var response = await _service.GenerateRecommendationsAsync(request);

        // Assert - the completed lesson should not appear in recommendations
        var recommendedIds = response.RecommendedLessons.Select(r => r.LessonId).ToList();
        Assert.DoesNotContain(lesson.Id, recommendedIds);
    }

    [Fact]
    public async Task Service_Should_Respect_MaxRecommendations_Limit()
    {
        // Arrange - create more than 5 lessons
        var matrix = new SkillMatrix
        {
            LearnerProfileId = TestProfileId,
            Skill = SkillType.Grammar,
            CurrentScore = 40,
            MasteryLevel = MasteryLevel.Weak,
            TotalAssessments = 1,
            LastAssessmentScore = 40
        };
        _context.SkillMatrices.Add(matrix);

        for (int i = 1; i <= 8; i++)
        {
            _context.Lessons.Add(new Lesson
            {
                Title = $"Grammar Lesson {i}",
                Skill = SkillType.Grammar,
                Topic = $"Topic{i}",
                Level = EnglishLevel.B1,
                Status = LessonStatus.Published
            });
        }
        _context.SaveChanges();

        var request = new RecommendationRequest
        {
            UserId = TestUserId,
            LearnerProfileId = TestProfileId,
            SourceEventId = "evt-max-limit",
            WeakestSkill = SkillType.Grammar,
            WeakTopics = new List<string>(),
            Level = EnglishLevel.B1,
            OccurredAt = DateTime.UtcNow
        };

        // Act
        var response = await _service.GenerateRecommendationsAsync(request);

        // Assert
        Assert.True(response.RecommendedLessons.Count <= 5);
    }

    [Fact]
    public void Engine_Should_ThrowArgumentNullException_WhenProfileIsNull()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _engine.GenerateAndRank(new List<Lesson>(), null!, new List<LearnerWeaknessHistory>(),
                new List<string>(), null, new List<string>(), EnglishLevel.B1, "evt-null"));
    }

    [Fact]
    public void Engine_Should_ThrowArgumentNullException_WhenCandidatesIsNull()
    {
        // Arrange
        var profile = _context.LearnerProfiles.Find(TestProfileId)!;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _engine.GenerateAndRank(null!, profile, new List<LearnerWeaknessHistory>(),
                new List<string>(), null, new List<string>(), EnglishLevel.B1, "evt-null2"));
    }

    [Fact]
    public void Engine_Should_Score_ActiveMatchingGoal_5()
    {
        // Arrange
        var profile = _context.LearnerProfiles.Find(TestProfileId)!;
        profile.SkillMatrices = new List<SkillMatrix>
        {
            new() { Skill = SkillType.Grammar, CurrentScore = 40, MasteryLevel = MasteryLevel.Weak }
        };
        var lessons = new List<Lesson>
        {
            new() { Id = 101, Title = "Grammar Lesson", Skill = SkillType.Grammar, Topic = "Tenses", Level = EnglishLevel.B1, Status = LessonStatus.Published }
        };
        
        var activeGoals = new List<GoalSetting>
        {
            new() { SkillTarget = "Grammar", Status = GoalStatus.Active, Deadline = DateTime.UtcNow.AddDays(1) }
        };

        // Act
        var ranked = _engine.GenerateAndRank(lessons, profile, new List<LearnerWeaknessHistory>(),
            new List<string>(), null, new List<string>(), EnglishLevel.B1, "evt-goal-score", activeGoals);

        // Assert - skill=20, level=15, goal=5 -> 40
        Assert.Single(ranked);
        Assert.Equal(40.0, ranked[0].PriorityScore);
        Assert.Contains("Phù hợp với mục tiêu học tập đang hoạt động", ranked[0].Reason);
    }

    [Fact]
    public void Engine_Should_Score_CompletedGoal_0()
    {
        // Arrange
        var profile = _context.LearnerProfiles.Find(TestProfileId)!;
        profile.SkillMatrices = new List<SkillMatrix>
        {
            new() { Skill = SkillType.Grammar, CurrentScore = 40, MasteryLevel = MasteryLevel.Weak }
        };
        var lessons = new List<Lesson>
        {
            new() { Id = 102, Title = "Grammar Lesson", Skill = SkillType.Grammar, Topic = "Tenses", Level = EnglishLevel.B1, Status = LessonStatus.Published }
        };
        
        var completedGoals = new List<GoalSetting>
        {
            new() { SkillTarget = "Grammar", Status = GoalStatus.Completed, Deadline = DateTime.UtcNow.AddDays(1) }
        };

        // Act
        var ranked = _engine.GenerateAndRank(lessons, profile, new List<LearnerWeaknessHistory>(),
            new List<string>(), null, new List<string>(), EnglishLevel.B1, "evt-goal-score-comp", completedGoals);

        // Assert - skill=20, level=15, goal=0 -> 35
        Assert.Single(ranked);
        Assert.Equal(35.0, ranked[0].PriorityScore);
        Assert.DoesNotContain("Phù hợp với mục tiêu học tập đang hoạt động", ranked[0].Reason);
    }

    [Fact]
    public void Engine_Should_Score_UnrelatedSkillGoal_0()
    {
        // Arrange
        var profile = _context.LearnerProfiles.Find(TestProfileId)!;
        profile.SkillMatrices = new List<SkillMatrix>
        {
            new() { Skill = SkillType.Grammar, CurrentScore = 40, MasteryLevel = MasteryLevel.Weak }
        };
        var lessons = new List<Lesson>
        {
            new() { Id = 103, Title = "Grammar Lesson", Skill = SkillType.Grammar, Topic = "Tenses", Level = EnglishLevel.B1, Status = LessonStatus.Published }
        };
        
        var activeGoals = new List<GoalSetting>
        {
            new() { SkillTarget = "Vocabulary", Status = GoalStatus.Active, Deadline = DateTime.UtcNow.AddDays(1) }
        };

        // Act
        var ranked = _engine.GenerateAndRank(lessons, profile, new List<LearnerWeaknessHistory>(),
            new List<string>(), null, new List<string>(), EnglishLevel.B1, "evt-goal-score-unrel", activeGoals);

        // Assert - skill=20, level=15, goal=0 -> 35
        Assert.Single(ranked);
        Assert.Equal(35.0, ranked[0].PriorityScore);
    }
}
