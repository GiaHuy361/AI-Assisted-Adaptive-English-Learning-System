namespace CoreLearningSystem.Domain.Enums;

public enum UserRole
{
    Admin,
    Learner
}

public enum EnglishLevel
{
    None,
    A1,
    A2,
    B1,
    B2,
    C1,
    C2,
    PlacementTest
}

public enum ActivityStatus
{
    Active,
    Inactive,
    Suspended
}

public enum SkillType
{
    Listening,
    Reading,
    Speaking,
    Writing,
    Grammar,
    Vocabulary,
    General
}

public enum LessonStatus
{
    Draft,
    Published,
    Archived
}

public enum LearningPathStatus
{
    NotStarted,
    InProgress,
    Completed
}

public enum GoalType
{
    TOEIC,
    IELTS,
    VSTEP,
    General,
    LessonsPerWeek,
    QuizzesPerWeek,
    LearningStreak,
    SkillScore,
    TargetLevel
}

public enum GoalStatus
{
    Active,
    Completed,
    Failed,
    Cancelled,
    Expired
}

public enum AchievementType
{
    LessonCount,
    QuizHighScoreCount,
    LearningStreak,
    GoalCompletionCount,
    SkillImprovement,
    FirstPlacementTest,
    FirstQuiz,
    FirstLesson
}

public enum GoalAdvisory
{
    Keep,
    AtRisk,
    DecreaseSuggested,
    IncreaseSuggested
}


public enum MasteryLevel
{
    Weak,
    Average,
    Good
}

public enum MatrixSourceType
{
    PlacementTest,
    Quiz,
    LessonCompletion,
    ManualAdjustment,
    SkillDecay,
    PeriodicRecalculation
}

public enum WeaknessStatus
{
    Active,
    Improving,
    Resolved
}

public enum RecommendationStatus
{
    Active,
    Accepted,
    Completed,
    Dismissed,
    Expired,
    Replaced
}

public enum RecommendationAction
{
    Generated,
    Accepted,
    Opened,
    Completed,
    Dismissed,
    Expired,
    Replaced,
    Regenerated
}

public enum NotificationType
{
    LearningReminder,
    WeeklyReport,
    NewRecommendation,
    GoalCompleted,
    BadgeAwarded,
    GoalAtRisk,
    SkillDecayWarning,
    System,
    FeedbackAlert
}

public enum NotificationChannel
{
    InApp,
    Email,
    InAppAndEmail
}

public enum NotificationStatus
{
    Pending,
    Processing,
    Sent,
    Failed,
    Cancelled
}

public enum JobStatus
{
    Running,
    Succeeded,
    PartiallySucceeded,
    Failed,
    Cancelled
}

public enum FeedbackTargetType
{
    Lesson,
    Quiz,
    Recommendation,
    System
}

public enum FeedbackStatus
{
    Pending,
    Reviewed,
    Resolved,
    Rejected
}

public enum FeedbackAlertStatus
{
    Normal,
    Warning,
    Critical,
    Resolved
}

public enum CertificateType
{
    TOEIC,
    IELTS,
    VSTEP
}

public enum SessionStatus
{
    Active,
    Expired,
    Revoked
}

public enum OutboxStatus
{
    Pending,
    Published,
    Failed
}


