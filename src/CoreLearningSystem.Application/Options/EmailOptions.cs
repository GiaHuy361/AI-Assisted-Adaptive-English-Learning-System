namespace CoreLearningSystem.Application.Options;

public class EmailOptions
{
    public const string Position = "Email";

    public string Provider { get; set; } = "Development"; // "Development" or "SMTP"
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 25;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = "no-reply@englishlearning.com";
    public string FromName { get; set; } = "AI English Learning System";
    public bool EnableSsl { get; set; } = true;
    public int MaxRetryAttempts { get; set; } = 3;
}
