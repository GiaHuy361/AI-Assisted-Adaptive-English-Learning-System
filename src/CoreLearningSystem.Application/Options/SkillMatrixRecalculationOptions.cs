namespace CoreLearningSystem.Application.Options;

public class SkillMatrixRecalculationOptions
{
    public const string Position = "SkillMatrixRecalculation";

    public bool Enabled { get; set; } = true;
    public double DifferenceThreshold { get; set; } = 5.0; // Update matrix only if score difference >= 5 points
    public string RecalculationCron { get; set; } = "0 4 * * 1"; // Weekly on Mondays at 4:00 AM UTC
    public string PeriodKey { get; set; } = "Weekly";
}
