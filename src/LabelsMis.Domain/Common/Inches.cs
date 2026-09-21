using System.Globalization;

namespace LabelsMis.Domain.Common;

/// <summary>
/// Inch → millimetre conversion for documents that show both units. Shrink-sleeve converters
/// spec layflat and sleeve size in mm, so the ticket prints the imperial value with its metric
/// equivalent alongside.
/// </summary>
public static class Inches
{
    public const decimal MmPerInch = 25.4m;

    /// <summary>Converts inches to millimetres, rounded half-away-from-zero to one decimal.</summary>
    public static decimal ToMm(decimal inches) =>
        decimal.Round(inches * MmPerInch, 1, MidpointRounding.AwayFromZero);

    /// <summary>"4.125 in (104.8 mm)" — inches to at most four decimals, millimetres to one.</summary>
    public static string FormatWithMm(decimal inches) =>
        string.Create(CultureInfo.InvariantCulture, $"{inches:0.####} in ({ToMm(inches):0.0} mm)");

    /// <summary>"4 × 3 in (101.6 × 76.2 mm)" — a two-dimensional size in both units.</summary>
    public static string FormatSizeWithMm(decimal acrossIn, decimal aroundIn) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{acrossIn:0.####} × {aroundIn:0.####} in ({ToMm(acrossIn):0.0} × {ToMm(aroundIn):0.0} mm)");
}
