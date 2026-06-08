using System;
using System.Collections.Generic;

namespace AdaptiveLearning.Worker.Services;

public record QuizAnalysisResultModel
{
    public bool Success { get; init; }
    public string AnalysisId { get; init; } = string.Empty;
    public int UserId { get; init; }
    public string WeakestSkill { get; init; } = string.Empty;
    public List<string> WeakTopics { get; init; } = new();
    public List<SkillScoreModel> SkillScores { get; init; } = new();
    public string Reason { get; init; } = string.Empty;
    public string ProcessedAt { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public record SkillScoreModel
{
    public string Skill { get; init; } = string.Empty;
    public double Score { get; init; }
    public int TotalQuestions { get; init; }
    public int CorrectAnswers { get; init; }
    public int IncorrectAnswers { get; init; }
}
