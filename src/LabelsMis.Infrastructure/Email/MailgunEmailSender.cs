using System.Net.Http.Headers;
using System.Text;
using LabelsMis.Domain.Email;
using LabelsMis.Domain.Entities;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LabelsMis.Infrastructure.Email;

/// <summary>
/// Sends email through the Mailgun HTTP API using the settings configured in the
/// EmailSettings singleton. When Mailgun is not configured the message is logged only,
/// matching <see cref="LoggingEmailSender"/> behavior so the app keeps working without a key.
/// </summary>
public class MailgunEmailSender(
    LabelsMisDbContext db,
    HttpClient httpClient,
    ILogger<MailgunEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(
        string to,
        string subject,
        string body,
        IReadOnlyList<string>? attachmentPaths = null,
        CancellationToken cancellationToken = default)
    {
        var settings = await db.EmailSettings.AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        if (settings is null || !settings.IsConfigured)
        {
            logger.LogWarning(
                "Mailgun is not configured; email to {To} subject {Subject} was not sent (attachments {AttachmentCount}).",
                to,
                subject,
                attachmentPaths?.Count ?? 0);
            return;
        }

        using var form = new MultipartFormDataContent
        {
            { new StringContent(settings.FromHeader), "from" },
            { new StringContent(to), "to" },
            { new StringContent(subject), "subject" },
            { new StringContent(body), "text" }
        };

        var openStreams = new List<Stream>();
        try
        {
            if (attachmentPaths is not null)
            {
                foreach (var path in attachmentPaths)
                {
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    {
                        logger.LogWarning("Skipping missing email attachment {Path}.", path);
                        continue;
                    }

                    var stream = File.OpenRead(path);
                    openStreams.Add(stream);
                    var fileContent = new StreamContent(stream);
                    fileContent.Headers.ContentType =
                        new MediaTypeHeaderValue(ResolveContentType(path));
                    form.Add(fileContent, "attachment", Path.GetFileName(path));
                }
            }

            var requestUri = $"{settings.ApiBaseUrl}/v3/{settings.Domain}/messages";
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = form
            };
            var credentials = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"api:{settings.ApiKey}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError(
                    "Mailgun send failed for {To} subject {Subject}: {StatusCode} {Error}",
                    to,
                    subject,
                    (int)response.StatusCode,
                    error);
                throw new InvalidOperationException(
                    $"Mailgun rejected the email ({(int)response.StatusCode}). {error}");
            }

            logger.LogInformation(
                "Mailgun email sent to {To} subject {Subject} attachments {AttachmentCount}.",
                to,
                subject,
                attachmentPaths?.Count ?? 0);
        }
        finally
        {
            foreach (var stream in openStreams)
            {
                await stream.DisposeAsync();
            }
        }
    }

    private static string ResolveContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".csv" => "text/csv",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };
}
