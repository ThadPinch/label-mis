using System.Globalization;
using System.Text.RegularExpressions;

namespace LabelsMis.Web.Pdf;

/// <summary>One version of a job and how many of it to run, read from a <c>VERSIONS:</c> note line.</summary>
public record TicketVersion(string Name, int Quantity);

/// <summary>What the v2 ticket prints in a job's RUN NOTES box.</summary>
public record TicketRunNotes(IReadOnlyList<TicketVersion> Versions, string? LineText, string? JobText)
{
    public bool IsEmpty => Versions.Count == 0 && LineText is null && JobText is null;
}

/// <summary>
/// Note rules for the v2 job ticket. Display only: nothing here changes what is stored.
/// </summary>
public static partial class JobTicketNotes
{
    private const string VersionsKeyword = "VERSIONS:";

    /// <summary>
    /// A job's run notes: the order line's note, plus the job's own note only when it says something
    /// different (scheduling seeds the job note from the line note, so an untouched copy is a repeat).
    /// A <c>VERSIONS: name qty | name qty</c> line becomes a checklist when its quantities add up to
    /// <paramref name="quantityOrdered"/>; otherwise it stays as written.
    /// </summary>
    public static TicketRunNotes Build(string? lineNotes, string? jobNotes, int quantityOrdered)
    {
        var line = Clean(lineNotes);
        var job = Clean(jobNotes);
        if (job is not null && SameText(line, job))
        {
            job = null;
        }

        // The line note is the live one; fall back to the job note for jobs whose line has none.
        var versions = SplitVersions(line, quantityOrdered, out var lineRest);
        string? jobRest = job;
        if (versions.Count == 0)
        {
            versions = SplitVersions(job, quantityOrdered, out jobRest);
        }

        return new TicketRunNotes(versions, lineRest, jobRest);
    }

    /// <summary>True when two notes read the same, ignoring case and spacing.</summary>
    public static bool SameText(string? a, string? b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Pulls the version list out of <paramref name="text"/>. Returns no versions, and the text
    /// untouched, unless exactly one line starts with <c>VERSIONS:</c>, every entry on it ends in a
    /// quantity, and the quantities add up to the ordered quantity. That last check is what keeps a
    /// name that merely ends in a number ("Batch 12") from being printed as a quantity.
    /// </summary>
    public static IReadOnlyList<TicketVersion> SplitVersions(string? text, int quantityOrdered, out string? rest)
    {
        rest = Clean(text);
        if (rest is null)
        {
            return [];
        }

        var lines = rest.Replace("\r\n", "\n").Split('\n');
        var keyed = lines
            .Select((value, index) => (value, index))
            .Where(l => l.value.TrimStart().StartsWith(VersionsKeyword, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (keyed.Count != 1)
        {
            return [];
        }

        var body = keyed[0].value.TrimStart()[VersionsKeyword.Length..];
        var versions = new List<TicketVersion>();
        foreach (var piece in body.Split('|'))
        {
            var match = VersionEntry().Match(piece.Trim());
            if (!match.Success
                || !int.TryParse(match.Groups["qty"].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var quantity)
                || quantity <= 0)
            {
                return [];
            }

            versions.Add(new TicketVersion(match.Groups["name"].Value.Trim(), quantity));
        }

        if (versions.Count == 0 || versions.Sum(v => (long)v.Quantity) != quantityOrdered)
        {
            return [];
        }

        rest = Clean(string.Join("\n", lines.Where((_, index) => index != keyed[0].index)));
        return versions;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string Normalize(string? text) =>
        text is null ? string.Empty : Whitespace().Replace(text, " ").Trim();

    [GeneratedRegex(@"^(?<name>.+?)\s+(?<qty>\d[\d,]*)$")]
    private static partial Regex VersionEntry();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
