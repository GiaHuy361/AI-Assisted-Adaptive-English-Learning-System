using System;
using System.Collections.Generic;

namespace AdaptiveLearning.Contracts.Events;

public record QuizAnswerDetail
{
    public int QuestionId { get; init; }
    public string SkillName { get; init; } = string.Empty;
    public string Topic { get; init; } = string.Empty;
    public string Level { get; init; } = string.Empty;
    public bool IsCorrect { get; init; }
}

public record QuizSubmittedEvent : BaseEvent
{
    public int UserId { get; init; }
    public int QuizId { get; init; }
    public int QuizAttemptId { get; init; }
    public double Score { get; init; }
    public int TotalQuestions { get; init; }
    public int CorrectAnswers { get; init; }
    public DateTimeOffset SubmittedAt { get; init; }
    public List<QuizAnswerDetail> AnswerDetails { get; init; } = new();
}
