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
