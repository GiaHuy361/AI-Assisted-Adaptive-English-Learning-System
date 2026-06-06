using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CoreLearningSystem.Infrastructure.Persistence;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;

class Program
{
    static async Task Main()
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseMySql(
            "Server=localhost;Port=3306;Database=AdaptiveEnglishLearningDb;Uid=root;Pwd=12345;",
            new MySqlServerVersion(new Version(8, 0, 30))
        );

        using var dbContext = new AppDbContext(optionsBuilder.Options);

        var profile = await dbContext.LearnerProfiles.FirstOrDefaultAsync(p => p.Id == 1);
        if (profile == null)
        {
            Console.WriteLine("Profile ID 1 not found!");
            return;
        }

        // Keep original level to restore later
        var originalLevel = profile.Level;
        profile.Level = EnglishLevel.A1;
        await dbContext.SaveChangesAsync();

        Console.WriteLine($"[SIMULATION START] Profile 1 Level forced to A1. Original: {originalLevel}");

        // Clean up any existing progress/attempts for profile 1 to have a clean slate
        var existingProgresses = await dbContext.LearnerProgresses.Where(p => p.LearnerProfileId == 1 && (p.LessonId == 73 || p.LessonId == 74)).ToListAsync();
        dbContext.LearnerProgresses.RemoveRange(existingProgresses);

        var existingAttempts = await dbContext.QuizAttempts.Where(a => a.LearnerProfileId == 1 && a.QuizId == 44).ToListAsync();
        dbContext.QuizAttempts.RemoveRange(existingAttempts);
        await dbContext.SaveChangesAsync();

        // Step A: Complete Lesson 73 (10 mins ago)
        var progress73 = new LearnerProgress
        {
            LearnerProfileId = 1,
            LessonId = 73,
            IsCompleted = true,
            CompletedAt = DateTime.UtcNow.AddMinutes(-10),
            LastAccessedAt = DateTime.UtcNow.AddMinutes(-10)
        };
        await dbContext.LearnerProgresses.AddAsync(progress73);

        // Step B: Pass Quiz 44 (5 mins ago)
        var attempt1 = new QuizAttempt
        {
            QuizId = 44,
            LearnerProfileId = 1,
            Score = 100,
            CorrectAnswersCount = 5,
            IncorrectAnswersCount = 0,
            IsPassed = true,
            AttemptedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        await dbContext.QuizAttempts.AddAsync(attempt1);
        await dbContext.SaveChangesAsync();

        Console.WriteLine("\n--- Step 1: Lesson 73 completed, Quiz 44 passed. Lesson 74 NOT completed. ---");
        await CheckAndPromoteUserLevelAsync(dbContext, profile);
        Console.WriteLine($"Profile Level: {profile.Level} (Expected: A1)");

        // Step D: Complete Lesson 74 (2 mins ago)
        var progress74 = new LearnerProgress
        {
            LearnerProfileId = 1,
            LessonId = 74,
            IsCompleted = true,
            CompletedAt = DateTime.UtcNow.AddMinutes(-2),
            LastAccessedAt = DateTime.UtcNow.AddMinutes(-2)
        };
        await dbContext.LearnerProgresses.AddAsync(progress74);
        await dbContext.SaveChangesAsync();

        Console.WriteLine("\n--- Step 2: Lesson 74 completed. Quiz 44 has old pass from 5 mins ago. ---");
        await CheckAndPromoteUserLevelAsync(dbContext, profile);
        Console.WriteLine($"Profile Level: {profile.Level} (Expected: A1 - because Quiz 44 attempt is older than Lesson 74 completion)");

        // Step F: Pass Quiz 44 again (Now)
        var attempt2 = new QuizAttempt
        {
            QuizId = 44,
            LearnerProfileId = 1,
            Score = 100,
            CorrectAnswersCount = 5,
            IncorrectAnswersCount = 0,
            IsPassed = true,
            AttemptedAt = DateTime.UtcNow
        };
        await dbContext.QuizAttempts.AddAsync(attempt2);
        await dbContext.SaveChangesAsync();

        Console.WriteLine("\n--- Step 3: Pass Quiz 44 again now (after Lesson 74 completed). ---");
        await CheckAndPromoteUserLevelAsync(dbContext, profile);
        Console.WriteLine($"Profile Level: {profile.Level} (Expected: A2 - promoted!)");

        // Clean up simulation records and restore original level
        dbContext.QuizAttempts.Remove(attempt1);
        dbContext.QuizAttempts.Remove(attempt2);
        dbContext.LearnerProgresses.Remove(progress73);
        dbContext.LearnerProgresses.Remove(progress74);
        profile.Level = originalLevel;
        await dbContext.SaveChangesAsync();

        Console.WriteLine("\n[SIMULATION CLEANUP] Cleaned up test data and restored Profile 1 Level.");
    }

    private static async Task CheckAndPromoteUserLevelAsync(AppDbContext dbContext, LearnerProfile learner)
    {
        var currentLevel = learner.Level;
        if (currentLevel == EnglishLevel.PlacementTest || currentLevel == EnglishLevel.None) return;

        var lessonsInTier = await dbContext.Lessons
            .Where(l => l.Level == currentLevel && l.Status == LessonStatus.Published)
            .ToListAsync();

        if (lessonsInTier.Count == 0) return;

        foreach (var lesson in lessonsInTier)
        {
            var progress = await dbContext.LearnerProgresses
                .FirstOrDefaultAsync(p => p.LearnerProfileId == learner.Id && p.LessonId == lesson.Id && p.IsCompleted);
            
            if (progress == null)
            {
                Console.WriteLine($"  [Check] Lesson {lesson.Id} NOT completed yet.");
                return;
            }

            if (lesson.QuizId.HasValue)
            {
                var completedAt = progress.CompletedAt ?? DateTime.MinValue;
                var isQuizPassed = await dbContext.QuizAttempts
                    .AnyAsync(a => a.LearnerProfileId == learner.Id 
                                   && a.QuizId == lesson.QuizId.Value 
                                   && (a.Score >= 50.0 || a.IsPassed)
                                   && a.AttemptedAt >= completedAt.AddSeconds(-10));

                if (!isQuizPassed)
                {
                    Console.WriteLine($"  [Check] Lesson {lesson.Id} quiz {lesson.QuizId.Value} NOT passed yet (after completion).");
                    return;
                }
            }
        }

        EnglishLevel nextLevel = currentLevel switch
        {
            EnglishLevel.A1 => EnglishLevel.A2,
            EnglishLevel.A2 => EnglishLevel.B1,
            EnglishLevel.B1 => EnglishLevel.B2,
            EnglishLevel.B2 => EnglishLevel.C1,
            EnglishLevel.C1 => EnglishLevel.C2,
            _ => currentLevel
        };

        if (nextLevel != currentLevel)
        {
            learner.Level = nextLevel;
            learner.LastActiveAt = DateTime.UtcNow;
            dbContext.LearnerProfiles.Update(learner);
            await dbContext.SaveChangesAsync();
            Console.WriteLine($"  [Check] LEVEL UP! Promoted to {nextLevel}.");
        }
    }
}
