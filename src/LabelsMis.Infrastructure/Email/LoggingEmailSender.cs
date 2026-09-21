using LabelsMis.Domain.Email;
using Microsoft.Extensions.Logging;

namespace LabelsMis.Infrastructure.Email;

public class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(
        string to,
        string subject,
        string body,
        IReadOnlyList<string>? attachmentPaths = null,
        string? cc = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Email to {To} cc {Cc} subject {Subject} attachments {AttachmentCount}",
            to,
            cc ?? "-",
            subject,
            attachmentPaths?.Count ?? 0);
        return Task.CompletedTask;
    }
}
