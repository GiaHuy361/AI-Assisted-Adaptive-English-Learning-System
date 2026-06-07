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
    public DbSet<GoalProgressHistory> GoalProgressHistories => Set<GoalProgressHistory>();
    public DbSet<Feedback> Feedbacks => Set<Feedback>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<SkillMatrix> SkillMatrices => Set<SkillMatrix>();
    public DbSet<SkillMatrixHistory> SkillMatrixHistories => Set<SkillMatrixHistory>();
    public DbSet<LearnerWeaknessHistory> LearnerWeaknessHistories => Set<LearnerWeaknessHistory>();
    public DbSet<Recommendation> Recommendations => Set<Recommendation>();
    public DbSet<RecommendationHistory> RecommendationHistories => Set<RecommendationHistory>();
    public DbSet<NotificationDeliveryAttempt> NotificationDeliveryAttempts => Set<NotificationDeliveryAttempt>();
    public DbSet<WeeklyLearningReport> WeeklyLearningReports => Set<WeeklyLearningReport>();
    public DbSet<BackgroundJobExecution> BackgroundJobExecutions => Set<BackgroundJobExecution>();

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

            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(20);
            entity.Property(e => e.SkillTarget).HasMaxLength(50);
            entity.Property(e => e.TargetLevel).HasMaxLength(20);
            entity.Property(e => e.Unit).HasMaxLength(50);

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

            entity.Property(e => e.Code).IsRequired().HasMaxLength(50);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.Property(e => e.AchievementType)
                .HasConversion<string>()
                .HasMaxLength(30);
            entity.Property(e => e.SkillTarget).HasMaxLength(20);
        });

        // 15. LearnerBadge Configuration
        modelBuilder.Entity<LearnerBadge>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.SourceEventId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.HasIndex(e => new { e.LearnerProfileId, e.BadgeId }).IsUnique();

            entity.HasOne(e => e.LearnerProfile)
                .WithMany(lp => lp.UnlockedBadges)
                .HasForeignKey(e => e.LearnerProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Badge)
                .WithMany(ab => ab.AwardedLearners)
                .HasForeignKey(e => e.BadgeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // GoalProgressHistory Configuration
        modelBuilder.Entity<GoalProgressHistory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.Property(e => e.SourceEventId).IsRequired().HasMaxLength(100);

            entity.Property(e => e.StatusBefore)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(e => e.StatusAfter)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.HasOne(e => e.Goal)
                .WithMany(g => g.ProgressHistories)
                .HasForeignKey(e => e.GoalId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.LearnerProfile)
                .WithMany(lp => lp.GoalProgressHistories)
                .HasForeignKey(e => e.LearnerProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.GoalId, e.SourceEventId }).IsUnique();
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

            entity.Property(e => e.Type)
                .HasConversion<string>()
                .HasMaxLength(30);

            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(e => e.Channel)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(e => e.IdempotencyKey).IsRequired().HasMaxLength(150);
            entity.Property(e => e.SourceType).HasMaxLength(50);
            entity.Property(e => e.SourceId).HasMaxLength(50);
            entity.Property(e => e.SourceEventId).HasMaxLength(100);
            entity.Property(e => e.LastError).HasMaxLength(1000);

            entity.HasIndex(e => e.IdempotencyKey).IsUnique();

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

            entity.Property(e => e.DecayPeriodKey).HasMaxLength(50);

            entity.HasIndex(e => e.EventId);
            entity.HasIndex(e => e.LearnerProfileId);
            entity.HasIndex(e => new { e.SkillMatrixId, e.DecayPeriodKey }).IsUnique();
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

        // 21. Recommendation Configuration
        modelBuilder.Entity<Recommendation>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.LearnerProfile)
                .WithMany(lp => lp.Recommendations)
                .HasForeignKey(e => e.LearnerProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Lesson)
                .WithMany()
                .HasForeignKey(e => e.LessonId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(e => e.Skill)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(e => e.Level)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(e => e.Topic).IsRequired().HasMaxLength(150);
            entity.Property(e => e.Reason).IsRequired().HasMaxLength(500);
            entity.Property(e => e.SourceEventId).IsRequired().HasMaxLength(100);

            entity.HasIndex(e => new { e.LearnerProfileId, e.Status });
            entity.HasIndex(e => new { e.LearnerProfileId, e.LessonId });
            entity.HasIndex(e => e.SourceEventId);
            entity.HasIndex(e => e.LessonId);
        });

        // 22. RecommendationHistory Configuration
        modelBuilder.Entity<RecommendationHistory>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.Recommendation)
                .WithMany()
                .HasForeignKey(e => e.RecommendationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.LearnerProfile)
                .WithMany(lp => lp.RecommendationHistories)
                .HasForeignKey(e => e.LearnerProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Lesson)
                .WithMany()
                .HasForeignKey(e => e.LessonId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(e => e.Action)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(e => e.PreviousStatus)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(e => e.NewStatus)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(e => e.Reason).IsRequired().HasMaxLength(500);
            entity.Property(e => e.SourceEventId).IsRequired().HasMaxLength(100);

            entity.HasIndex(e => e.SourceEventId);
            entity.HasIndex(e => e.LearnerProfileId);
            entity.HasIndex(e => e.LessonId);
            entity.HasIndex(e => e.RecommendationId);
            entity.HasIndex(e => e.RecordedAt);
        });

        // 22. NotificationDeliveryAttempt Configuration
        modelBuilder.Entity<NotificationDeliveryAttempt>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Channel).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.ErrorMessage).HasMaxLength(1000);

            entity.HasOne(e => e.Notification)
                .WithMany(n => n.DeliveryAttempts)
                .HasForeignKey(e => e.NotificationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 23. WeeklyLearningReport Configuration
        modelBuilder.Entity<WeeklyLearningReport>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.StrongestSkill).HasMaxLength(50);
            entity.Property(e => e.WeakestSkill).HasMaxLength(50);
            entity.Property(e => e.GoalProgressSummary).HasColumnType("longtext");
            entity.Property(e => e.BadgesEarned).HasColumnType("longtext");

            entity.HasIndex(e => new { e.LearnerProfileId, e.WeekStart }).IsUnique();

            entity.HasOne(e => e.LearnerProfile)
                .WithMany(lp => lp.WeeklyLearningReports)
                .HasForeignKey(e => e.LearnerProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Notification)
                .WithMany()
                .HasForeignKey(e => e.NotificationId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // 24. BackgroundJobExecution Configuration
        modelBuilder.Entity<BackgroundJobExecution>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.JobName).IsRequired().HasMaxLength(150);
            entity.Property(e => e.ExecutionId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            entity.Property(e => e.TriggerType).IsRequired().HasMaxLength(50);
        });
    }
}
