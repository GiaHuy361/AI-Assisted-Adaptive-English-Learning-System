using System;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Application.DTOs.Events;

public record QuizSubmittedEvent(
    int AttemptId,
    int QuizId,
    int LearnerProfileId,
    double Score,
    bool IsPassed,
    DateTime Timestamp
);

public record PlacementTestCompletedEvent(
    int TestResultId,
    int LearnerProfileId,
    int Score,
    EnglishLevel RecommendedLevel,
    DateTime Timestamp
);

public record GoalCompletedEvent(
    int GoalId,
    int LearnerProfileId,
    string Target,
    DateTime Timestamp
);

public record LessonCompletedEvent(
    int LearnerProfileId,
    int LessonId,
    string SkillName,
    string Topic,
    string Level,
    DateTime Timestamp
);

public record FeedbackSubmittedEvent(
    int LearnerProfileId,
    string TargetType,
    int? TargetId,
    int Rating,
    string Comment,
    DateTime Timestamp
);

