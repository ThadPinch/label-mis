namespace LabelsMis.Domain.Estimating;

/// <summary>
/// Converts between the two ways of stating profit on a price. The engine stores and prices with
/// <b>markup on cost</b> (<c>price = cost × (1 + markup)</c>, see MarkupRules); the shop thinks in
/// <b>gross margin on price</b> (<c>margin = (price − cost) / price</c>). Both describe the same
/// price, so forms convert at their boundary and nothing stored changes: a stored 0.45 markup is,
/// and always was, a 31.0% margin.
/// </summary>
public static class PricingMath
{
    /// <summary>
    /// Half of the one-decimal-percent resolution margins are displayed at (0.05 percentage points,
    /// as a fraction). A re-entered margin this close to what a stored markup displays as is treated
    /// as "unchanged", so the stored value survives a display → edit → save round trip exactly.
    /// </summary>
    public const decimal MarginDisplayTolerance = 0.0005m;

    /// <summary>
    /// Largest margin percent a form may accept: <c>MarkupToMarginPercent(10)</c>, the 1000% markup cap
    /// that Product and GeneralSettings enforce, stated in the unit the forms show. A <c>double</c>
    /// because <c>[Range]</c> needs a compile-time constant.
    /// </summary>
    public const double MaxFormMarginPercent = 90.9;

    /// <summary>Gross margin on price for a markup on cost: <c>m / (1 + m)</c>.</summary>
    public static decimal MarkupToMargin(decimal markup)
    {
        if (markup <= -1m)
        {
            throw new ArgumentOutOfRangeException(nameof(markup), markup, "Markup must be greater than -1 (-100%).");
        }

        return markup / (1m + markup);
    }

    /// <summary>Markup on cost for a gross margin on price: <c>g / (1 − g)</c>. Margin must be below 1 (100%).</summary>
    public static decimal MarginToMarkup(decimal margin)
    {
        if (margin >= 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(margin), margin, "Margin must be less than 1 (100%); no finite markup produces it.");
        }

        return margin / (1m - margin);
    }

    /// <summary>Margin for display as a percent rounded to one decimal (0.45 markup → 31.0).</summary>
    public static decimal MarkupToMarginPercent(decimal markup) =>
        EstimatingMath.RoundOneDecimal(MarkupToMargin(markup) * 100m);

    /// <summary>Markup fraction for a margin entered as a percent (31.0 → 0.449275…).</summary>
    public static decimal MarginPercentToMarkup(decimal marginPercent) =>
        MarginToMarkup(marginPercent / 100m);

    /// <summary>
    /// Markup fraction for a margin entered as a percent, returning <paramref name="currentMarkup"/>
    /// unchanged when the entered margin is within <see cref="MarginDisplayTolerance"/> of what that
    /// markup displays as. Without this, showing a stored 0.45 as 31.0% and saving the form untouched
    /// would store 0.449275… instead.
    /// </summary>
    /// <param name="marginPercent">Margin the user entered, as a percent (31.0 = 31%).</param>
    /// <param name="currentMarkup">Markup the field was pre-filled from: the stored value on an edit
    /// form, or the shop-wide default a create form was seeded with. Null converts unconditionally.</param>
    public static decimal MarginPercentToMarkup(decimal marginPercent, decimal? currentMarkup)
    {
        var margin = marginPercent / 100m;
        if (currentMarkup is { } current && Math.Abs(margin - MarkupToMargin(current)) <= MarginDisplayTolerance)
        {
            return current;
        }

        return MarginToMarkup(margin);
    }
}
