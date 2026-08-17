using System.Globalization;

namespace LabelsMis.Web.Services.Models;

/// <summary>
/// Orders text the way people read shelf/bin labels: runs of digits compare by numeric value and
/// everything else compares as case-insensitive text, chunk by chunk. So "11" sorts before "108",
/// and "165/188" lands right after "165" (and before "166") instead of between "16" and "17".
/// Nulls sort last. Ties fall back to an ordinal compare so the order is stable.
/// </summary>
public sealed class NaturalStringComparer : IComparer<string?>
{
    public static readonly NaturalStringComparer Instance = new();

    /// <summary>The same ordering reversed, for descending sorts.</summary>
    public static readonly IComparer<string?> Descending = Comparer<string?>.Create((a, b) => Instance.Compare(b, a));

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return 1;
        if (y is null) return -1;

        var ix = 0;
        var iy = 0;
        while (ix < x.Length && iy < y.Length)
        {
            var xDigit = char.IsDigit(x[ix]);
            var yDigit = char.IsDigit(y[iy]);
            if (xDigit && yDigit)
            {
                var xNum = ReadDigits(x, ref ix);
                var yNum = ReadDigits(y, ref iy);
                var byValue = CompareNumbers(xNum, yNum);
                if (byValue != 0) return byValue;
                continue;
            }

            if (xDigit != yDigit)
            {
                // A number sorts ahead of text at the same position ("12" before "12A" is handled by
                // length; here it decides "12" vs "A12"), which keeps purely numeric labels first.
                return xDigit ? -1 : 1;
            }

            var xText = ReadText(x, ref ix);
            var yText = ReadText(y, ref iy);
            var byText = string.Compare(xText, yText, CultureInfo.InvariantCulture, CompareOptions.IgnoreCase);
            if (byText != 0) return byText;
        }

        var byLength = (x.Length - ix).CompareTo(y.Length - iy);
        return byLength != 0 ? byLength : string.CompareOrdinal(x, y);
    }

    private static ReadOnlySpan<char> ReadDigits(string s, ref int i)
    {
        var start = i;
        while (i < s.Length && char.IsDigit(s[i])) i++;
        return s.AsSpan(start, i - start);
    }

    private static string ReadText(string s, ref int i)
    {
        var start = i;
        while (i < s.Length && !char.IsDigit(s[i])) i++;
        return s.Substring(start, i - start);
    }

    /// <summary>Numeric compare of two digit runs without parsing (no overflow on long runs):
    /// strip leading zeros, shorter run is smaller, equal length compares ordinally.</summary>
    private static int CompareNumbers(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        var ta = a.TrimStart('0');
        var tb = b.TrimStart('0');
        if (ta.Length != tb.Length) return ta.Length.CompareTo(tb.Length);
        var byValue = ta.SequenceCompareTo(tb);
        // "007" and "7" are the same number; keep the shorter spelling first for a stable order.
        return byValue != 0 ? byValue : a.Length.CompareTo(b.Length);
    }
}
