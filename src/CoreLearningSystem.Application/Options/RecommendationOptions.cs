namespace CoreLearningSystem.Application.Options;

public class RecommendationOptions
{
    public const string Position = "RecommendationOptions";

    public int MaxRecommendations { get; set; } = 5;
    public int RecommendationExpirationDays { get; set; } = 7;
    public int DismissedCooldownDays { get; set; } = 3;
    public double MinimumPriorityScore { get; set; } = 30.0;
}
