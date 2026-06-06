namespace CoreLearningSystem.Domain.Entities;

public class QuizAttemptDetail
{
    public int Id { get; set; }
    public int QuizAttemptId { get; set; }
    public int QuestionId { get; set; }
    public int? SelectedAnswerOptionId { get; set; }
    public bool IsCorrect { get; set; }

    // Navigation Properties
    public QuizAttempt QuizAttempt { get; set; } = null!;
    public Question Question { get; set; } = null!;
    public AnswerOption? SelectedAnswerOption { get; set; }
}
