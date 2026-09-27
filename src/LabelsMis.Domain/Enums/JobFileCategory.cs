namespace LabelsMis.Domain.Enums;

/// <summary>What a file attached to a job is for. <see cref="PrintFile"/> is the one prepress
/// imposes; the rest are reference material kept with the job.</summary>
public enum JobFileCategory
{
    CustomerArtwork = 0,
    ApprovedArtwork = 1,
    PrintFile = 2,
    Embellishment = 3,
    Imposition = 4,
    Other = 5
}
