namespace LabelsMis.Domain.Enums;

public enum JobStatus
{
    /// <summary>At an outside vendor; the item skips press/finishing and moves to Rewound (ready to
    /// ship) when it is received. Sits below PrePress so "status only moves forward" still holds.</summary>
    Outsourced = -1,
    PrePress = 0,
    Queued = 1,
    Printed = 2,
    Finished = 3,
    Rewound = 4,
    Shipped = 5,
    Closed = 6
}
