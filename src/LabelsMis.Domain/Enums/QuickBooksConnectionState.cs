namespace LabelsMis.Domain.Enums;

public enum QuickBooksConnectionState
{
    /// <summary>No Intuit client ID and secret saved yet.</summary>
    NoCredentials = 0,

    /// <summary>Keys saved; nobody has pressed Connect.</summary>
    NotConnected = 1,

    Connected = 2,

    /// <summary>QuickBooks stopped accepting the stored tokens; someone has to connect again.</summary>
    NeedsReconnect = 3
}
