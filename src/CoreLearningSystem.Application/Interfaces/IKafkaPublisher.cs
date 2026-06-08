using System.Threading.Tasks;
using CoreLearningSystem.Application.DTOs.Events;

namespace CoreLearningSystem.Application.Interfaces;

public interface IKafkaPublisher
{
    Task PublishQuizSubmittedAsync(QuizSubmittedEvent ev);
    Task PublishPlacementTestCompletedAsync(PlacementTestCompletedEvent ev);
    Task PublishGoalCompletedAsync(GoalCompletedEvent ev);
    Task PublishLessonCompletedAsync(LessonCompletedEvent ev);
    Task PublishFeedbackSubmittedAsync(FeedbackSubmittedEvent ev);
    Task PublishAsync(string topic, string key, object message);
}
