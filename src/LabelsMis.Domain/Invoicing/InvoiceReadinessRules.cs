namespace LabelsMis.Domain.Invoicing;

/// <summary>What decides whether an unexported, billable invoice is ready to go to QuickBooks.</summary>
/// <param name="BillBeforeShip">The invoice is flagged to bill before the order ships.</param>
/// <param name="OrderMarkedShipped">The sales order's own status is Shipped, Invoiced or Closed —
/// set when every line shipped in full, or by Mark order shipped after an under-run.</param>
/// <param name="ShippedLines">Order lines whose shipped quantity covers the ordered quantity.</param>
/// <param name="TotalLines">Order lines (charge-only orders have none).</param>
/// <param name="AnyShipped">Something on the order has already gone out (a partial or short
/// shipment), so the hint points at Mark order shipped.</param>
/// <param name="Total">Invoice total.</param>
/// <param name="ShippingPendingShipments">Shipped shipments still waiting on a carrier cost (E4).</param>
/// <param name="CustomerBlockers">Customer set-up reasons (E7).</param>
/// <param name="UnrecordedShippedJobs">Jobs production moved to Shipped/Closed whose line shipped
/// nothing on a recorded shipment — named in the reason so someone records it.</param>
public sealed record InvoiceReadinessFacts(
    bool BillBeforeShip,
    bool OrderMarkedShipped,
    int ShippedLines,
    int TotalLines,
    bool AnyShipped,
    decimal Total,
    IReadOnlyList<string> ShippingPendingShipments,
    IReadOnlyList<string> CustomerBlockers,
    IReadOnlyList<string>? UnrecordedShippedJobs = null);

/// <summary>Ready = (the order counts as shipped, or the invoice is flagged bill-before-ship)
/// AND total &gt; 0 AND no blockers (carrier cost still pending, customer not set up). Non-billable
/// invoices never get here. Anything else waits, with the reasons listed.</summary>
/// <remarks>The order counts as shipped when every line's shipped quantity covers its ordered
/// quantity, or when the order itself is marked Shipped / Invoiced / Closed. Under-runs are normal
/// in label printing, so a short line doesn't hold the invoice once the shop marks the order
/// shipped. A partial shipment alone does not make it ready: the order bills once, when it has all
/// shipped.</remarks>
public static class InvoiceReadinessRules
{
    public const string UnderRunHint = "If it under-ran, open its shipment and use Mark order shipped";
    public const string UnrecordedShipmentHint = "record the shipment from the job page";

    public static bool CountsAsShipped(InvoiceReadinessFacts facts) =>
        facts.OrderMarkedShipped || (facts.TotalLines > 0 && facts.ShippedLines >= facts.TotalLines);

    public static IReadOnlyList<string> WaitingReasons(InvoiceReadinessFacts facts)
    {
        var reasons = new List<string>();
        if (!facts.BillBeforeShip && !CountsAsShipped(facts))
        {
            if (facts.TotalLines == 0)
            {
                reasons.Add("Nothing on the order ships — use Bill before shipping");
            }
            else
            {
                var reason = $"Not shipped yet ({facts.ShippedLines} of {facts.TotalLines} lines)";
                if (facts.UnrecordedShippedJobs is { Count: > 0 } unrecorded)
                {
                    reason += $". {string.Join(", ", unrecorded)} {(unrecorded.Count == 1 ? "was" : "were")} marked shipped "
                        + $"with no shipment recorded — {UnrecordedShipmentHint}";
                }

                if (facts.AnyShipped)
                {
                    reason += $". {UnderRunHint}";
                }

                reasons.Add(reason);
            }
        }

        if (facts.Total <= 0m)
        {
            reasons.Add("$0 total — close it as non-billable or add charges");
        }

        if (facts.ShippingPendingShipments.Count > 0)
        {
            reasons.Add($"Waiting on carrier cost ({string.Join(", ", facts.ShippingPendingShipments)})");
        }

        reasons.AddRange(facts.CustomerBlockers);
        return reasons;
    }
}
