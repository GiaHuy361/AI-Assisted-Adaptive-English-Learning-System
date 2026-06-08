namespace CoreLearningSystem.Application.Options;

public class AchievementOptions
{
    public const string Position = "Achievement";
    public double HighScoreThresholdPercent { get; set; } = 80.0;
    public int SkillImprovementThreshold { get; set; } = 15;
}
