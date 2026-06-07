using Microsoft.EntityFrameworkCore;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<LearnerProfile> LearnerProfiles => Set<LearnerProfile>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<Quiz> Quizzes => Set<Quiz>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<AnswerOption> AnswerOptions => Set<AnswerOption>();
    public DbSet<PlacementTestResult> PlacementTestResults => Set<PlacementTestResult>();
    public DbSet<QuizAttempt> QuizAttempts => Set<QuizAttempt>();
    public DbSet<QuizAttemptDetail> QuizAttemptDetails => Set<QuizAttemptDetail>();
    public DbSet<LearningPath> LearningPaths => Set<LearningPath>();
    public DbSet<LearningPathItem> LearningPathItems => Set<LearningPathItem>();
    public DbSet<LearnerProgress> LearnerProgresses => Set<LearnerProgress>();
    public DbSet<GoalSetting> GoalSettings => Set<GoalSetting>();
    public DbSet<AchievementBadge> AchievementBadges => Set<AchievementBadge>();
    public DbSet<LearnerBadge> LearnerBadges => Set<LearnerBadge>();
    public DbSet<Feedback> Feedbacks => Set<Feedback>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<SkillMatrix> SkillMatrices => Set<SkillMatrix>();
    public DbSet<SkillMatrixHistory> SkillMatrixHistories => Set<SkillMatrixHistory>();
    public DbSet<LearnerWeaknessHistory> LearnerWeaknessHistories => Set<LearnerWeaknessHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 1. User Configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Username).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.Username).IsUnique();
            entity.Property(e => e.Email).IsRequired().HasMaxLength(150);
            entity.Property(e => e.PasswordHash).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Role)
                .HasConversion<string>()
                .HasMaxLength(20);
        });

        // 2. LearnerProfile Configuration (1-to-1 with User)
        modelBuilder.Entity<LearnerProfile>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.HasOne(e => e.User)
                .WithOne(u => u.LearnerProfile)
                .HasForeignKey<LearnerProfile>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.Level)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(e => e.ActivityStatus)
                .HasConversion<string>()
                .HasMaxLength(20);
        });

        // 3. Lesson Configuration
        modelBuilder.Entity<Lesson>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Content).IsRequired().HasColumnType("longtext");
            entity.Property(e => e.Topic).IsRequired().HasMaxLength(150);
            entity.Property(e => e.Skill)
                .HasConversion<string>()
                .HasMaxLength(20);
            entity.Property(e => e.Level)
                .HasConversion<string>()
                .HasMaxLength(20);
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.HasOne(e => e.Quiz)
                .WithMany()
                .HasForeignKey(e => e.QuizId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // 4. Quiz Configuration
        modelBuilder.Entity<Quiz>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Level)
                .HasConversion<string>()
                .HasMaxLength(20);
            entity.Property(e => e.IsPlacementTest)
                .IsRequired()
                .HasDefaultValue(false);
            entity.Property(e => e.MaxScore).IsRequired().HasDefaultValue(10.0);
        });

        // 5. Question Configuration
        modelBuilder.Entity<Question>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Content).IsRequired().HasMaxLength(1000);
            entity.Property(e => e.CorrectAnswer).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Explanation).HasMaxLength(1000);
            entity.Property(e => e.Score).IsRequired().HasDefaultValue(1.0);

            entity.HasOne(e => e.Quiz)
                .WithMany(q => q.Questions)
                .HasForeignKey(e => e.QuizId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.Skill)
                .HasConversion<string>()
                .HasMaxLength(20);
            entity.Property(e => e.Level)
                .HasConversion<string>()
                .HasMaxLength(20);
        });

        // 6. AnswerOption Configuration
        modelBuilder.Entity<AnswerOption>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OptionText).IsRequired().HasMaxLength(500);

            entity.HasOne(e => e.Question)
                .WithMany(q => q.AnswerOptions)
                .HasForeignKey(e => e.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 7. PlacementTestResult Configuration
        modelBuilder.Entity<PlacementTestResult>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.LearnerProfile)
                .WithMany(lp => lp.PlacementTestResults)
                .HasForeignKey(e => e.LearnerProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.RecommendedLevel)
                .HasConversion<string>()
                .HasMaxLength(20);
        });

        // 8. QuizAttempt Configuration
        modelBuilder.Entity<QuizAttempt>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Quiz)
                .WithMany(q => q.QuizAttempts)
                .HasForeignKey(e => e.QuizId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.LearnerProfile)
                .WithMany(lp => lp.QuizAttempts)
                .HasForeignKey(e => e.LearnerProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 9. QuizAttemptDetail Configuration
        modelBuilder.Entity<QuizAttemptDetail>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.HasOne(e => e.QuizAttempt)
                .WithMany(qa => qa.Details)
                .HasForeignKey(e => e.QuizAttemptId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Question)
                .WithMany(q => q.QuizAttemptDetails)
                .HasForeignKey(e => e.QuestionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.SelectedAnswerOption)
                .WithMany()
                .HasForeignKey(e => e.SelectedAnswerOptionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // 10. LearningPath Configuration (1-to-1 with LearnerProfile)
        modelBuilder.Entity<LearningPath>(entity =>
        {
            entity.HasKey(e => e.PathId);

            entity.HasOne(e => e.LearnerProfile)
                .WithOne(lp => lp.LearningPath)
                .HasForeignKey<LearningPath>(e => e.LearnerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(20);
        });

        // 11. LearningPathItem Configuration
        modelBuilder.Entity<LearningPathItem>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.LearningPath)
                .WithMany(lp => lp.Items)
                .HasForeignKey(e => e.LearningPathId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Lesson)
                .WithMany(l => l.LearningPathItems)
                .HasForeignKey(e => e.LessonId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(20);
        });

        // 12. LearnerProgress Configuration
        modelBuilder.Entity<LearnerProgress>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.LearnerProfile)
                .WithMany(lp => lp.ProgressHistory)
                .HasForeignKey(e => e.LearnerProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Lesson)
                .WithMany(l => l.ProgressHistory)
                .HasForeignKey(e => e.LessonId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 13. GoalSetting Configuration
        modelBuilder.Entity<GoalSetting>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Target).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Type)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.HasOne(e => e.LearnerProfile)
                .WithMany(lp => lp.Goals)
                .HasForeignKey(e => e.LearnerProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 14. AchievementBadge Configuration
        modelBuilder.Entity<AchievementBadge>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(150);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.ImageUrl).HasMaxLength(300);
            entity.Property(e => e.Criteria).HasMaxLength(500);
        });

        // 15. LearnerBadge Configuration
        modelBuilder.Entity<LearnerBadge>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.LearnerProfile)
                .WithMany(lp => lp.UnlockedBadges)
                .HasForeignKey(e => e.LearnerProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Badge)
                .WithMany(ab => ab.AwardedLearners)
                .HasForeignKey(e => e.BadgeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 16. Feedback Configuration
        modelBuilder.Entity<Feedback>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Subject).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Content).IsRequired().HasColumnType("longtext");
            entity.Property(e => e.ReviewComment).HasMaxLength(1000);

            entity.HasOne(e => e.LearnerProfile)
                .WithMany(lp => lp.SubmittedFeedbacks)
                .HasForeignKey(e => e.LearnerProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 17. Notification Configuration
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Message).IsRequired().HasMaxLength(1000);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 18. SkillMatrix Configuration
        modelBuilder.Entity<SkillMatrix>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.LearnerProfile)
                .WithMany(lp => lp.SkillMatrices)
                .HasForeignKey(e => e.LearnerProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.Skill)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(e => e.MasteryLevel)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.HasIndex(e => new { e.LearnerProfileId, e.Skill }).IsUnique();
        });

        // 19. SkillMatrixHistory Configuration
        modelBuilder.Entity<SkillMatrixHistory>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.LearnerProfile)
                .WithMany(lp => lp.SkillMatrixHistories)
                .HasForeignKey(e => e.LearnerProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.Skill)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(e => e.SourceType)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.HasIndex(e => e.EventId);
            entity.HasIndex(e => e.LearnerProfileId);
        });

        // 20. LearnerWeaknessHistory Configuration
        modelBuilder.Entity<LearnerWeaknessHistory>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.LearnerProfile)
                .WithMany(lp => lp.WeaknessHistories)
                .HasForeignKey(e => e.LearnerProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.Skill)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(e => e.Topic).IsRequired().HasMaxLength(150);
            entity.Property(e => e.Level).HasMaxLength(20);

            entity.HasIndex(e => new { e.LearnerProfileId, e.Skill, e.Topic }).IsUnique();
        });
    }
}
