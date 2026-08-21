using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.ValueObjects;

namespace LabelsMis.Domain.Entities;

public class Estimate : EntityBase
{
    private readonly List<EstimateLine> _lines = [];
    private readonly List<EstimateCharge> _charges = [];
    private readonly List<EstimateRevision> _revisions = [];

    private Estimate()
    {
    }

    public string EstimateNumber { get; private set; } = string.Empty;
    public int RevisionNumber { get; private set; } = 1;
    public Guid CustomerId { get; private set; }
    public Customer Customer { get; private set; } = null!;
    public Guid? SalesRepId { get; private set; }
    public EstimateStatus Status { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>Internal billing instructions; carried onto the sales order. Never printed on a customer document.</summary>
    public string? BillingNotes { get; private set; }
    public DateOnly? ValidUntilDate { get; private set; }
    public DateTime? WonAt { get; private set; }

    /// <summary>When the estimate first went to the customer (email). Null on a draft, and on an
    /// estimate that was marked won without ever being sent — the page flags that as "not sent".</summary>
    public DateTime? SentAt { get; private set; }
    public bool IsSentToCustomer => SentAt.HasValue;
    public DateTime? LostAt { get; private set; }
    public string? LostReason { get; private set; }
    public string? PdfFilePath { get; private set; }

    /// <summary>The email address the estimate was last sent to. Editable on every send.</summary>
    public string? ContactEmail { get; private set; }

    public Guid? ShippingMethodId { get; private set; }
    public ShippingMethod? ShippingMethod { get; private set; }
    public decimal ShippingCost { get; private set; }
    public string? ShipToName { get; private set; }
    public string? ShipToStreet1 { get; private set; }
    public string? ShipToStreet2 { get; private set; }
    public string? ShipToCity { get; private set; }
    public string? ShipToState { get; private set; }
    public string? ShipToZip { get; private set; }
    public string? ShipToCountry { get; private set; }

    public ShippingAddress ShippingAddress => new(
        ShipToName, ShipToStreet1, ShipToStreet2, ShipToCity, ShipToState, ShipToZip, ShipToCountry);

    public IReadOnlyCollection<EstimateLine> Lines => _lines;
    public IReadOnlyCollection<EstimateCharge> Charges => _charges;
    public IReadOnlyCollection<EstimateRevision> Revisions => _revisions;

    public static Estimate CreateDraft(
        Guid id,
        string estimateNumber,
        Guid customerId,
        Guid? salesRepId,
        string? notes,
        string? billingNotes,
        DateOnly? validUntilDate,
        Guid? shippingMethodId,
        decimal shippingCost,
        ShippingAddress shippingAddress,
        Guid createdById,
        DateTime createdAt)
    {
        var estimate = new Estimate
        {
            EstimateNumber = estimateNumber,
            CustomerId = customerId,
            SalesRepId = salesRepId,
            Status = EstimateStatus.Draft,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            BillingNotes = string.IsNullOrWhiteSpace(billingNotes) ? null : billingNotes.Trim(),
            ValidUntilDate = validUntilDate
        };
        estimate.SetShipping(shippingMethodId, shippingCost, shippingAddress);
        estimate.SetCreated(id, createdById, createdAt);
        return estimate;
    }

    public void UpdateDraft(
        Guid? salesRepId,
        string? notes,
        string? billingNotes,
        DateOnly? validUntilDate,
        Guid? shippingMethodId,
        decimal shippingCost,
        ShippingAddress shippingAddress,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        EnsureDraft();

        SalesRepId = salesRepId;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        BillingNotes = string.IsNullOrWhiteSpace(billingNotes) ? null : billingNotes.Trim();
        ValidUntilDate = validUntilDate;
        SetShipping(shippingMethodId, shippingCost, shippingAddress);
        SetModified(modifiedById, modifiedAt);
    }

    private void SetShipping(Guid? shippingMethodId, decimal shippingCost, ShippingAddress shippingAddress)
    {
        var address = (shippingAddress ?? ShippingAddress.Empty).Normalized();
        ShippingMethodId = shippingMethodId;
        ShippingCost = shippingCost < 0 ? 0 : shippingCost;
        ShipToName = address.RecipientName;
        ShipToStreet1 = address.Street1;
        ShipToStreet2 = address.Street2;
        ShipToCity = address.City;
        ShipToState = address.State;
        ShipToZip = address.Zip;
        ShipToCountry = address.Country;
    }

    public void ReplaceLines(IEnumerable<EstimateLine> lines)
    {
        EnsureDraft();
        _lines.Clear();
        _lines.AddRange(lines);
    }

    public void AddLine(EstimateLine line) => _lines.Add(line);

    public void ReplaceCharges(IEnumerable<EstimateCharge> charges)
    {
        EnsureDraft();
        _charges.Clear();
        _charges.AddRange(charges);
    }

    public void AddCharge(EstimateCharge charge) => _charges.Add(charge);

    /// <summary>Records the recipient address used for the latest send. Allowed in any status
    /// so resends can update it.</summary>
    public void SetContactEmail(string? contactEmail, Guid modifiedById, DateTime modifiedAt)
    {
        ContactEmail = string.IsNullOrWhiteSpace(contactEmail) ? null : contactEmail.Trim();
        SetModified(modifiedById, modifiedAt);
    }

    public void MarkSent(string? pdfFilePath, Guid modifiedById, DateTime modifiedAt)
    {
        EnsureDraft();
        if (_lines.Count == 0)
        {
            throw new InvalidOperationException("Estimate must have at least one line before it can be sent.");
        }
        Status = EstimateStatus.Sent;
        PdfFilePath = pdfFilePath;
        SentAt ??= modifiedAt;
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>An email actually went to the customer outside the draft→sent transition (a resend,
    /// or the first send of an estimate that was marked won directly). Keeps the first send time.</summary>
    public void RecordSentToCustomer(string? pdfFilePath, Guid modifiedById, DateTime modifiedAt)
    {
        SentAt ??= modifiedAt;
        PdfFilePath ??= pdfFilePath;
        SetModified(modifiedById, modifiedAt);
    }

    public void MarkWon(Guid modifiedById, DateTime modifiedAt)
    {
        if (Status is not (EstimateStatus.Sent or EstimateStatus.Draft))
        {
            throw new InvalidOperationException("Only draft or sent estimates can be marked won.");
        }
        if (_lines.Count == 0)
        {
            throw new InvalidOperationException("Estimate must have at least one line before it can be marked won.");
        }

        Status = EstimateStatus.Won;
        WonAt = modifiedAt;
        LostAt = null;
        LostReason = null;
        SetModified(modifiedById, modifiedAt);
    }

    public void MarkLost(string? reason, Guid modifiedById, DateTime modifiedAt)
    {
        if (Status is EstimateStatus.Won or EstimateStatus.Cancelled)
        {
            throw new InvalidOperationException("Won or cancelled estimates cannot be marked lost.");
        }

        Status = EstimateStatus.Lost;
        LostAt = modifiedAt;
        LostReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        SetModified(modifiedById, modifiedAt);
    }

    public void MarkExpired(Guid modifiedById, DateTime modifiedAt)
    {
        if (Status is EstimateStatus.Won or EstimateStatus.Lost or EstimateStatus.Cancelled)
        {
            throw new InvalidOperationException("Closed estimates cannot be marked expired.");
        }

        Status = EstimateStatus.Expired;
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>Admin escape hatch for a misentered estimate — allowed from any status, including
    /// Won, unlike MarkLost. The caller cascades the cancellation to any linked sales order.</summary>
    public void Cancel(Guid modifiedById, DateTime modifiedAt)
    {
        if (Status is EstimateStatus.Cancelled)
        {
            throw new InvalidOperationException("Estimate is already cancelled.");
        }

        Status = EstimateStatus.Cancelled;
        SetModified(modifiedById, modifiedAt);
    }

    public EstimateRevision CreateRevisionSnapshot(
        Guid revisionId,
        string snapshotJson,
        Guid createdById,
        DateTime createdAt)
    {
        if (Status is EstimateStatus.Draft)
        {
            throw new InvalidOperationException("Draft estimates do not require a revision.");
        }

        var revision = EstimateRevision.Create(revisionId, Id, RevisionNumber, snapshotJson, createdById, createdAt);
        RevisionNumber++;
        Status = EstimateStatus.Draft;
        PdfFilePath = null;
        SentAt = null;
        WonAt = null;
        LostAt = null;
        LostReason = null;
        SetModified(createdById, createdAt);
        return revision;
    }

    public EstimateRevision BeginRevision(Guid revisionId, string snapshotJson, Guid createdById, DateTime createdAt)
    {
        var revision = CreateRevisionSnapshot(revisionId, snapshotJson, createdById, createdAt);
        _revisions.Add(revision);
        return revision;
    }

    public void EnsureCanTransitionToDraft()
    {
        if (Status is EstimateStatus.Won)
        {
            throw new InvalidOperationException("Won estimates cannot return to draft without creating a revision.");
        }
    }

    public void EnsureCanDelete()
    {
        if (Status is not EstimateStatus.Draft)
        {
            throw new InvalidOperationException("Only draft estimates can be deleted.");
        }
    }

    public void AddRevision(EstimateRevision revision) => _revisions.Add(revision);

    private void EnsureDraft()
    {
        if (Status is not EstimateStatus.Draft)
        {
            throw new InvalidOperationException("Only draft estimates can be edited.");
        }
    }
}
