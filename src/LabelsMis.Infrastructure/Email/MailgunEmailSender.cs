using System.Net.Http.Headers;
using System.Text;
using LabelsMis.Domain.Email;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Storage;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LabelsMis.Infrastructure.Email;

/// <summary>
/// Sends email through the Mailgun HTTP API using the settings configured in the
/// EmailSettings singleton. When Mailgun is not configured the send fails with a clear
/// message so the caller can surface it to the user instead of silently dropping the email.
/// </summary>
public class MailgunEmailSender(
    LabelsMisDbContext db,
    HttpClient httpClient,
    IFileStorageClient fileStorage,
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
            throw new InvalidOperationException(
                "Email is not configured. Set up Mailgun under Settings → Email before sending.");
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
                    var stream = await OpenAttachmentAsync(path, cancellationToken);
                    if (stream is null)
                    {
                        logger.LogWarning("Skipping missing email attachment {Path}.", path);
                        continue;
                    }

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

    /// <summary>Attachments are file-storage keys for generated PDFs (tmp/…) or legacy local
    /// filesystem paths; missing files return null so the email still goes out without them.</summary>
    private async Task<Stream?> OpenAttachmentAsync(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (File.Exists(path))
        {
            return File.OpenRead(path);
        }

        if (await fileStorage.ExistsAsync(path, cancellationToken))
        {
            return await fileStorage.OpenReadAsync(path, cancellationToken);
        }

        return null;
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
