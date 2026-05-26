namespace LabelsMis.Infrastructure.Fedex;

public class FedexOptions
{
    public const string SectionName = "Fedex";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public bool UseSandbox { get; set; } = true;
    public string LabelStoragePath { get; set; } = "./data/labels/fedex";
}
