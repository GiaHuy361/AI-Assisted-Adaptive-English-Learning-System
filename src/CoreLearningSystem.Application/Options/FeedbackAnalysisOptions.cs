namespace CoreLearningSystem.Application.Options;

public class FeedbackAnalysisOptions
{
    public const string Position = "FeedbackAnalysis";

    /// <summary>Minimum count before alert evaluation begins.</summary>
    public int MinimumCountForAlert { get; set; } = 3;

    /// <summary>Average rating at or below which a Warning is raised.</summary>
    public double WarningAverageRatingThreshold { get; set; } = 3.5;

    /// <summary>Average rating at or below which a Critical alert is raised.</summary>
    public double CriticalAverageRatingThreshold { get; set; } = 2.5;

    /// <summary>Low-rating rate (%) at or above which a Warning is raised (e.g. 0.30 = 30%).</summary>
    public double WarningLowRatingRateThreshold { get; set; } = 0.30;

    /// <summary>Low-rating rate (%) at or above which a Critical alert is raised (e.g. 0.50 = 50%).</summary>
    public double CriticalLowRatingRateThreshold { get; set; } = 0.50;

    /// <summary>Hour-level cooldown to avoid duplicate admin notifications within the same alert tier.</summary>
    public int AlertCooldownHours { get; set; } = 24;
}
