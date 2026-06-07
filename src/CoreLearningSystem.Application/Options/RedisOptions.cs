namespace CoreLearningSystem.Application.Options;

public class RedisOptions
{
    public const string Position = "Redis";

    public string ConnectionString { get; set; } = "localhost:6379";
    public string InstanceName { get; set; } = "AdaptiveLearning:";
    
    public int DefaultTtlMinutes { get; set; } = 60;
    public int SkillMatrixTtlMinutes { get; set; } = 60;
    public int RecommendationTtlMinutes { get; set; } = 60;
    public int LessonTtlMinutes { get; set; } = 60;
    public int ProgressTtlMinutes { get; set; } = 60;
    public int ProcessedEventTtlHours { get; set; } = 24;
    
    public int ConnectRetry { get; set; } = 3;
    public bool AbortOnConnectFail { get; set; } = false;
}
