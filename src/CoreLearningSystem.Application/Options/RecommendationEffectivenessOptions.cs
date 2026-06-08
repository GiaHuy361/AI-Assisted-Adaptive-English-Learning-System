namespace CoreLearningSystem.Application.Options;

public class RecommendationEffectivenessOptions
{
    public const string Position = "RecommendationEffectiveness";

    public double MinimumImprovementPoints { get; set; } = 5.0; // Skill score must improve by at least 5 points
    public int EvaluationWindowDays { get; set; } = 7; // Look for subsequent quizzes within 7 days of completion
    public double MinimumQuizScoreAfter { get; set; } = 70.0; // Must achieve at least 70% in the subsequent quiz
    public bool RequireLessonCompletion { get; set; } = true;
}
