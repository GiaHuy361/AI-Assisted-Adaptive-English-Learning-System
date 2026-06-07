namespace AdaptiveLearning.Worker.Options;

public class RecommendationGrpcOptions
{
    public const string Position = "RecommendationGrpc";

    public string RecommendationServiceUrl { get; set; } = string.Empty;
    public int RequestTimeoutSeconds { get; set; } = 30;
}
