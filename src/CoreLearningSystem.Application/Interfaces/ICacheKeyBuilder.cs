namespace CoreLearningSystem.Application.Interfaces;

public interface ICacheKeyBuilder
{
    // Lesson cache keys
    string LessonListVersion();
    string LessonList(long version, string? skill = null, string? level = null, string? role = null);
    string LessonDetail(int lessonId);
    string LessonDetailSet(); // tracked-set key for lesson detail keys

    // Skill Matrix cache keys
    string SkillMatrix(int learnerProfileId);

    // Recommendation cache keys
    string ActiveRecommendations(int learnerProfileId);

    // Progress cache keys
    string ProgressSummary(int learnerProfileId);
    string ProgressDetails(int userId);

    // Idempotency event keys (for RedisProcessedEventStore)
    string ProcessedEventProcessing(string eventId);
    string ProcessedEventCompleted(string eventId);
}
