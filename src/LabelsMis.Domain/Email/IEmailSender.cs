namespace LabelsMis.Domain.Email;

public interface IEmailSender
{
    Task SendAsync(
        string to,
        string subject,
        string body,
        IReadOnlyList<string>? attachmentPaths = null,
        CancellationToken cancellationToken = default);
}
