namespace LabelsMis.Domain.Enums;

/// <summary>Where an invoice is on its way to QuickBooks. "In QuickBooks" is not a value here: that
/// is an invoice with an exported date and a QuickBooks id, and it can need attention as well (a
/// total that came back different, a change that could not be sent).</summary>
public enum QuickBooksExportStatus
{
    /// <summary>Nothing is under way.</summary>
    None = 0,

    /// <summary>Waiting for the background worker, or for its next try after a temporary failure.</summary>
    Queued = 1,

    /// <summary>The worker has it right now.</summary>
    Sending = 2,

    /// <summary>Stopped until a person fixes what the message says and retries.</summary>
    NeedsAttention = 3
}
