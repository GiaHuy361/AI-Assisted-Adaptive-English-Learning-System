namespace AdaptiveLearning.Worker.Options;

public class RecommendationGrpcOptions
{
    public const string Position = "RecommendationGrpc";

    public string ServiceUrl { get; set; } = string.Empty;
    public int RequestTimeoutSeconds { get; set; } = 30;
    public int MaxRetryAttempts { get; set; } = 2;
    public int RetryDelayMilliseconds { get; set; } = 500;
}
