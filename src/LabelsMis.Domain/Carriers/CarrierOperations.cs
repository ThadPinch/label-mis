namespace LabelsMis.Domain.Carriers;

/// <summary>The names the carrier log files each call under. Stored as text, so a name is never
/// changed once rows exist; add new ones instead.</summary>
public static class CarrierOperations
{
    public const string SignIn = "signin";
    public const string Rates = "rates";
    public const string Book = "book";
    public const string BookCheck = "book.check";
    public const string Label = "label";
    public const string Void = "void";
    public const string Track = "track";

    /// <summary>What the log on Setup → Shipping carrier calls an operation.</summary>
    public static string Describe(string operation) => operation switch
    {
        SignIn => "Test the connection",
        Rates => "Get rates",
        Book => "Buy the label",
        BookCheck => "Check an unanswered purchase",
        Label => "Fetch a label file",
        Void => "Void the label",
        Track => "Check tracking",
        _ => operation
    };
}
