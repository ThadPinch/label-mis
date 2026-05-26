namespace LabelsMis.Domain.Entities;

public class DocumentSequence
{
    public Guid Id { get; private set; }
    public string DocumentType { get; private set; } = string.Empty;
    public int Year { get; private set; }
    public int LastNumber { get; private set; }

    private DocumentSequence()
    {
    }

    public static DocumentSequence Create(Guid id, string documentType, int year)
    {
        return new DocumentSequence
        {
            Id = id,
            DocumentType = documentType,
            Year = year,
            LastNumber = 0
        };
    }

    public string NextFormattedNumber(string prefix)
    {
        LastNumber++;
        return $"{prefix}-{Year}-{LastNumber:D5}";
    }
}
