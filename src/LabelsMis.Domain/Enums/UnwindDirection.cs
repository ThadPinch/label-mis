namespace LabelsMis.Domain.Enums;

/// <summary>
/// Standard roll unwind / copy position chart: which edge of the copy dispenses first and
/// whether the labels are wound to the outside (#1–#4) or inside (#5–#8) of the roll.
/// </summary>
public enum UnwindDirection
{
    Position1 = 1,
    Position2 = 2,
    Position3 = 3,
    Position4 = 4,
    Position5 = 5,
    Position6 = 6,
    Position7 = 7,
    Position8 = 8
}

public static class UnwindDirectionExtensions
{
    /// <summary>Human-readable chart label, e.g. "#1 — Top off, wound out".</summary>
    public static string Label(this UnwindDirection unwind) => unwind switch
    {
        UnwindDirection.Position1 => "#1 — Top off, wound out",
        UnwindDirection.Position2 => "#2 — Bottom off, wound out",
        UnwindDirection.Position3 => "#3 — Right off, wound out",
        UnwindDirection.Position4 => "#4 — Left off, wound out",
        UnwindDirection.Position5 => "#5 — Top off, wound in",
        UnwindDirection.Position6 => "#6 — Bottom off, wound in",
        UnwindDirection.Position7 => "#7 — Right off, wound in",
        UnwindDirection.Position8 => "#8 — Left off, wound in",
        _ => unwind.ToString()
    };
}
