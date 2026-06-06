namespace CoreLearningSystem.Domain.Entities;

public class AnswerOption
{
    public int Id { get; set; }
    public int QuestionId { get; set; }
    public string OptionText { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }

    // Navigation Properties
    public Question Question { get; set; } = null!;
}
