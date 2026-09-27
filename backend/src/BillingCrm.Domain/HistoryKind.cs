namespace BillingCrm.Domain;

/// <summary>
/// What happened to an invoice (spec 010): exactly these five. A payment that settles an invoice is one <see cref="Payment"/> entry that also
/// shows the change to Paid; there is no sixth kind for it. Overdue is never an event (nothing happens to an invoice when its due date passes).
/// </summary>
public enum HistoryKind
{
    /// <summary>The invoice was first saved.</summary>
    Created,

    /// <summary>A Draft was saved with at least one change.</summary>
    DraftEdited,

    /// <summary>The invoice went from Draft to Sent.</summary>
    Sent,

    /// <summary>A payment was recorded (and, if it settled the invoice, the status went to Paid in the same entry).</summary>
    Payment,

    /// <summary>The invoice was cancelled from Draft or Sent.</summary>
    Cancelled,
}
