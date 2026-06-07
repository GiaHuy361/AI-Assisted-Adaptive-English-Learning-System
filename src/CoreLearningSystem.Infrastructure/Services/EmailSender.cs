using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Application.Options;

namespace CoreLearningSystem.Infrastructure.Services;

public class EmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<EmailSender> _logger;

    // Static store for unit tests to verify emails without sending them
    public static ConcurrentBag<EmailMessage> SentEmails { get; } = new();

    public EmailSender(IOptions<EmailOptions> options, ILogger<EmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<EmailSendResult> SendEmailAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));

        if (_options.Provider.Equals("Development", StringComparison.OrdinalIgnoreCase))
        {
            // Development Mode - Log and save to transient store
            SentEmails.Add(message);
            _logger.LogInformation("Development Mode: Simulated sending email to {ToAddress}. Subject: {Subject}. Body (partial): {Body}", 
                message.ToAddress, message.Subject, message.Body.Length > 100 ? message.Body[..100] + "..." : message.Body);
            
            return new EmailSendResult
            {
                Success = true,
                MessageId = $"dev-{Guid.NewGuid()}"
            };
        }

        // SMTP Mode
        try
        {
            _logger.LogInformation("SMTP Mode: Sending email to {ToAddress} via {Host}:{Port}", message.ToAddress, _options.Host, _options.Port);

            using var mailMessage = new MailMessage();
            mailMessage.From = new MailAddress(_options.FromAddress, _options.FromName);
            mailMessage.To.Add(message.ToAddress);
            mailMessage.Subject = message.Subject;
            mailMessage.Body = message.Body;
            mailMessage.IsBodyHtml = message.IsHtml;

            using var smtpClient = new SmtpClient(_options.Host, _options.Port);
            smtpClient.EnableSsl = _options.EnableSsl;

            if (!string.IsNullOrEmpty(_options.Username))
            {
                smtpClient.Credentials = new NetworkCredential(_options.Username, _options.Password);
            }

            // Bind cancellation token
            using (cancellationToken.Register(() => smtpClient.SendAsyncCancel()))
            {
                // In System.Net.Mail, SendMailAsync supports CancellationToken from .NET 6+
                await smtpClient.SendMailAsync(mailMessage, cancellationToken);
            }

            return new EmailSendResult
            {
                Success = true,
                MessageId = Guid.NewGuid().ToString()
            };
        }
        catch (SmtpFailedRecipientException ex)
        {
            _logger.LogError(ex, "SMTP recipient failed: {Recipient}", ex.FailedRecipient);
            return new EmailSendResult
            {
                Success = false,
                ErrorMessage = $"SMTP recipient failed: {ex.Message}"
            };
        }
        catch (SmtpException ex)
        {
            _logger.LogError(ex, "SMTP error sending email to {ToAddress}", message.ToAddress);
            return new EmailSendResult
            {
                Success = false,
                ErrorMessage = $"SMTP transmission failure: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sending email to {ToAddress}", message.ToAddress);
            return new EmailSendResult
            {
                Success = false,
                ErrorMessage = $"Email error: {ex.Message}"
            };
        }
    }
}
