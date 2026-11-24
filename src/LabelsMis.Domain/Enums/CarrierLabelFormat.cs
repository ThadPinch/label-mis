namespace LabelsMis.Domain.Enums;

/// <summary>The shapes a bought label can be fetched in.</summary>
public enum CarrierLabelFormat
{
    /// <summary>One box, a 4 × 6 in PDF page (thermal label printers, from the browser).</summary>
    Pdf4x6 = 0,

    /// <summary>One box, raw ZPL for a thermal printer.</summary>
    Zpl = 1,

    /// <summary>Every box of the shipment, one per Letter page (plain paper).</summary>
    PdfLetter = 2
}
