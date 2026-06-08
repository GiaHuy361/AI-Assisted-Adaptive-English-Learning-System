using System.Collections.Generic;

namespace AdaptiveLearning.GrpcService.Services;

public record QuizAnalysisInput
{
    public int UserId { get; init; }
    public int QuizId { get; init; }
    public int QuizAttemptId { get; init; }
    public double Score { get; init; }
    public int TotalQuestions { get; init; }
    public int CorrectAnswers { get; init; }
    public List<AnswerAnalysisDetail> Answers { get; init; } = new();
}

public record AnswerAnalysisDetail
{
    public int QuestionId { get; init; }
    public string Skill { get; init; } = string.Empty;
    public string Topic { get; init; } = string.Empty;
    public string Level { get; init; } = string.Empty;
    public bool IsCorrect { get; init; }
}

public record QuizAnalysisResult
{
    public string AnalysisId { get; init; } = string.Empty;
    public int UserId { get; init; }
    public string WeakestSkill { get; init; } = string.Empty;
    public List<string> WeakTopics { get; init; } = new();
    public List<SkillScoreResult> SkillScores { get; init; } = new();
    public string Reason { get; init; } = string.Empty;
    public string ProcessedAt { get; init; } = string.Empty;
}

public record SkillScoreResult
{
    public string Skill { get; init; } = string.Empty;
    public double Score { get; init; }
    public int TotalQuestions { get; init; }
    public int CorrectAnswers { get; init; }
    public int IncorrectAnswers { get; init; }
}
