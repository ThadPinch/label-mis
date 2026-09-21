using LabelsMis.Domain.Email;
using LabelsMis.Domain.Entities;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Email;

/// <summary>Sends a document email (estimate, invoice, PO) and records the attempt in
/// <see cref="EmailLog"/>, succeeded or not, so the estimate/order pages can show a send
/// history. Optionally CCs the signed-in user on the message.</summary>
public class DocumentEmailService(
    LabelsMisDbContext db,
    ICurrentUserService currentUser,
    IEmailSender emailSender)
{
    public async Task SendAsync(
        EmailDocumentType documentType,
        Guid documentId,
        Guid? salesOrderId,
        string to,
        string subject,
        string body,
        IReadOnlyList<string>? attachmentPaths,
        bool ccMe,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");

        string? cc = null;
        if (ccMe)
        {
            var user = await currentUser.GetUserAsync(cancellationToken);
            cc = user?.Email;
        }

        var sentAt = DateTime.UtcNow;
        try
        {
            await emailSender.SendAsync(
                to,
                subject,
                body,
                attachmentPaths,
                cc: cc,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            await WriteLogAsync(documentType, documentId, salesOrderId, to, cc, subject,
                attachmentPaths is { Count: > 0 }, succeeded: false, ex.Message, userId, sentAt, cancellationToken);
            throw;
        }

        await WriteLogAsync(documentType, documentId, salesOrderId, to, cc, subject,
            attachmentPaths is { Count: > 0 }, succeeded: true, null, userId, sentAt, cancellationToken);
    }

    public async Task<IReadOnlyList<EmailLogRow>> ListForDocumentAsync(
        EmailDocumentType documentType,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.EmailLogs.AsNoTracking()
            .Where(e => e.DocumentType == documentType && e.DocumentId == documentId)
            .OrderByDescending(e => e.SentAt)
            .ToListAsync(cancellationToken);
        return await ProjectAsync(rows, cancellationToken);
    }

    /// <summary>Every email tied to an order: its invoices' sends, plus anything logged
    /// directly against the order.</summary>
    public async Task<IReadOnlyList<EmailLogRow>> ListForSalesOrderAsync(
        Guid salesOrderId,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.EmailLogs.AsNoTracking()
            .Where(e => e.SalesOrderId == salesOrderId)
            .OrderByDescending(e => e.SentAt)
            .ToListAsync(cancellationToken);
        return await ProjectAsync(rows, cancellationToken);
    }

    private async Task<IReadOnlyList<EmailLogRow>> ProjectAsync(List<EmailLog> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var userIds = rows.Select(r => r.CreatedById).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, Name = u.Email ?? u.UserName ?? u.Id.ToString() })
            .ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);

        return rows.Select(r => new EmailLogRow(
            r.Id,
            r.DocumentType,
            r.DocumentId,
            r.SentAt,
            users.GetValueOrDefault(r.CreatedById) ?? "—",
            r.To,
            r.Cc,
            r.Subject,
            r.HadAttachment,
            r.Succeeded,
            r.Error)).ToList();
    }

    private async Task WriteLogAsync(
        EmailDocumentType documentType,
        Guid documentId,
        Guid? salesOrderId,
        string to,
        string? cc,
        string subject,
        bool hadAttachment,
        bool succeeded,
        string? error,
        Guid userId,
        DateTime sentAt,
        CancellationToken cancellationToken)
    {
        db.EmailLogs.Add(EmailLog.Create(
            Guid.NewGuid(), documentType, documentId, salesOrderId, to, cc, subject,
            hadAttachment, succeeded, error, userId, sentAt));
        await db.SaveChangesAsync(cancellationToken);
    }
}

public record EmailLogRow(
    Guid Id,
    EmailDocumentType DocumentType,
    Guid DocumentId,
    DateTime SentAt,
    string SentBy,
    string To,
    string? Cc,
    string Subject,
    bool HadAttachment,
    bool Succeeded,
    string? Error);
