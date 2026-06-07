using System;
using System.Threading.Tasks;
using CoreLearningSystem.Application.DTOs.Events;
using CoreLearningSystem.Application.Interfaces;

namespace CoreLearningSystem.Infrastructure.Services;

public class MockKafkaPublisher : IKafkaPublisher
{
    public Task PublishQuizSubmittedAsync(QuizSubmittedEvent ev)
    {
        Console.WriteLine($"[Kafka Mock] QuizSubmittedEvent fired: AttemptId={ev.AttemptId}, LearnerId={ev.LearnerProfileId}, Score={ev.Score}%, IsPassed={ev.IsPassed}");
        return Task.CompletedTask;
    }

    public Task PublishPlacementTestCompletedAsync(PlacementTestCompletedEvent ev)
    {
        Console.WriteLine($"[Kafka Mock] PlacementTestCompletedEvent fired: ResultId={ev.TestResultId}, LearnerId={ev.LearnerProfileId}, Score={ev.Score}, Recommended={ev.RecommendedLevel}");
        return Task.CompletedTask;
    }

    public Task PublishGoalCompletedAsync(GoalCompletedEvent ev)
    {
        Console.WriteLine($"[Kafka Mock] GoalCompletedEvent fired: GoalId={ev.GoalId}, LearnerId={ev.LearnerProfileId}, Target={ev.Target}");
        return Task.CompletedTask;
    }

    public Task PublishLessonCompletedAsync(LessonCompletedEvent ev)
    {
        Console.WriteLine($"[Kafka Mock] LessonCompletedEvent fired: LearnerId={ev.LearnerProfileId}, LessonId={ev.LessonId}");
        return Task.CompletedTask;
    }

    public Task PublishFeedbackSubmittedAsync(FeedbackSubmittedEvent ev)
    {
        Console.WriteLine($"[Kafka Mock] FeedbackSubmittedEvent fired: LearnerId={ev.LearnerProfileId}, Rating={ev.Rating}");
        return Task.CompletedTask;
    }
}
