using System.Threading;
using System.Threading.Tasks;

namespace CoreLearningSystem.Application.Interfaces;

public record EmailMessage
{
    public string ToAddress { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public bool IsHtml { get; init; } = true;
}

public record EmailSendResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? MessageId { get; init; }
}

public interface IEmailSender
{
    Task<EmailSendResult> SendEmailAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
