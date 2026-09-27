namespace BillingCrm.Domain;

/// <summary>
/// The legal changes of an invoice's status, in one place. Exactly four exist: Draft to Sent, Draft to Cancelled and Sent to Cancelled
/// (spec 007), and Sent to Paid (spec 008). Everything else (Sent to Draft, Cancelled to anything, Paid to anything, Draft to Paid) is
/// illegal. <b>Sent to Paid is the one change nobody requests</b>: only a payment that brings the remaining amount to exactly zero causes
/// it (see <see cref="Invoice.RecordPayment"/>), so it is never offered to a person. Who may make a change (only an Admin may cancel)
/// is a permission, decided at the API, not here.
/// </summary>
public static class InvoiceStatusRules
{
    private static readonly IReadOnlyDictionary<InvoiceStatus, IReadOnlyList<InvoiceStatus>> Changes =
        new Dictionary<InvoiceStatus, IReadOnlyList<InvoiceStatus>>
        {
            [InvoiceStatus.Draft] = [InvoiceStatus.Sent, InvoiceStatus.Cancelled],
            [InvoiceStatus.Sent] = [InvoiceStatus.Cancelled, InvoiceStatus.Paid],
            [InvoiceStatus.Paid] = [],
            [InvoiceStatus.Cancelled] = [],
        };

    /// <summary>True when an invoice may move from one status to the other.</summary>
    public static bool CanChange(InvoiceStatus from, InvoiceStatus to) => Changes[from].Contains(to);

    /// <summary>
    /// The statuses an invoice in this status may move to. This is the raw table: it includes Paid for Sent, which no person can ask for
    /// (<see cref="Invoice.AllowedTransitions"/> is what a screen may offer).
    /// </summary>
    public static IReadOnlyList<InvoiceStatus> NextStatuses(InvoiceStatus from) => Changes[from];
}
