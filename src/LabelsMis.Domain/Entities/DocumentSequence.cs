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

    /// <summary>A year's counter. <paramref name="lastNumber"/> is the number the document type had
    /// reached when the year began, so the first number of a new year follows the last of the old
    /// one; 0 for a type that has never been numbered.</summary>
    public static DocumentSequence Create(Guid id, string documentType, int year, int lastNumber = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lastNumber);
        return new DocumentSequence
        {
            Id = id,
            DocumentType = documentType,
            Year = year,
            LastNumber = lastNumber
        };
    }

    public string NextFormattedNumber(string prefix)
    {
        LastNumber++;
        return $"{prefix}-{Year}-{LastNumber:D5}";
    }
}
