using System;
using System.Collections.Generic;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Domain.Entities;

public class Question
{
    public int Id { get; set; }
    public int QuizId { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? ReadingText { get; set; }
    public SkillType Skill { get; set; } = SkillType.General;
    public string Topic { get; set; } = string.Empty;
    public EnglishLevel Level { get; set; } = EnglishLevel.A1;
    public string CorrectAnswer { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public double Score { get; set; } = 1.0; // Score/weight of this question in the quiz package

    // Navigation Properties
    public Quiz Quiz { get; set; } = null!;
    public ICollection<AnswerOption> AnswerOptions { get; set; } = new List<AnswerOption>();
    public ICollection<QuizAttemptDetail> QuizAttemptDetails { get; set; } = new List<QuizAttemptDetail>();
}
