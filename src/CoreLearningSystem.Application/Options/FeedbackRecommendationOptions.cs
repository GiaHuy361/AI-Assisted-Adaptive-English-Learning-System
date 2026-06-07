namespace CoreLearningSystem.Application.Options;

public class FeedbackRecommendationOptions
{
    public const string Position = "FeedbackRecommendation";

    public double PositiveBonus { get; set; } = 3.0;          // max +3
    public double NegativePenalty { get; set; } = 10.0;       // max -10
    public bool CriticalContentExcluded { get; set; } = true;
    public int MinimumFeedbackCountForBonus { get; set; } = 2;
    public int MinimumFeedbackCountForExclusion { get; set; } = 3;
    public int LearnerNegativeCooldownDays { get; set; } = 14; // learner-specific cooldown after rating <= 2
}
